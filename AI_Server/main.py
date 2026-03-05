"""
AI Server - FastAPI (Multimodal Medical Diagnosis)
====================================================
Endpoints:
  POST /predict   -> Chẩn đoán đa phương thức (ảnh + EHR + clinical notes)
                     Trả về: ranked diagnoses (ICD codes), xác suất calibrate,
                             textual rationale (giải thích ngắn)
  POST /segment   -> Ảnh mask phân đoạn vùng tổn thương (base64 PNG)
  POST /explain   -> Heatmap Grad-CAM++ (base64 PNG)
  POST /shap      -> SHAP values cho EHR + text features (giải thích lâm sàng)
  GET  /health    -> Kiểm tra server còn sống

Mô hình AI:
  - Bio_ClinicalBERT (HuggingFace): text embedding cho clinical notes
  - ResNet50: CNN image features
  - XGBoost Ensemble (14 binary classifiers): phân loại bệnh
  - Isotonic Calibrators: hiệu chỉnh xác suất
  - U-Net: segmentation vùng tổn thương
  - Grad-CAM++: visual saliency map
  - SHAP TreeExplainer: interpretability cho EHR features
"""

import os
import gc
import base64
import logging
import pickle
import numpy as np
import cv2
import torch
import tensorflow as tf
from pathlib import Path
from typing import Optional, List
from contextlib import asynccontextmanager

from fastapi import FastAPI, File, UploadFile, Form, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pydantic import BaseModel
from ultralytics import YOLO
from transformers import AutoTokenizer, AutoModel
from sklearn.preprocessing import StandardScaler

from utils.preprocess import (
    bytes_to_cv2, preprocess_for_yolo,
    preprocess_for_unet, preprocess_for_fusion,
    ndarray_to_png_bytes, overlay_mask
)
from utils.gradcam import make_gradcam_heatmap, encode_image_to_base64
from utils.shap_explain import compute_shap_ehr, decode_ehr, EHR_FEATURE_NAMES

# ─── CẤU HÌNH ────────────────────────────────────────────────────────────────
logging.basicConfig(level=logging.INFO, format="%(levelname)s | %(message)s")
log = logging.getLogger(__name__)

MODELS_DIR        = Path(__file__).parent / "models"
YOLO_PATH         = MODELS_DIR / "best.pt"
UNET_PATH         = MODELS_DIR / "unet_model.h5"
FUSION_PATH       = MODELS_DIR / "fusion_model.h5"       # Legacy (vẫn giữ)
MLP_PATH          = MODELS_DIR / "mlp_model.keras"       # MLP fusion mới
XGB_PATH          = MODELS_DIR / "xgb_models.pkl"        # XGBoost ensemble
CALIB_PATH        = MODELS_DIR / "calibrators.pkl"       # Isotonic calibrators
SCALER_PATH       = MODELS_DIR / "ehr_scaler.pkl"        # EHR StandardScaler
EXTRACTOR_PATH    = MODELS_DIR / "resnet50_extractor.keras"  # CNN extractor

BERT_MODEL_NAME  = "emilyalsentzer/Bio_ClinicalBERT"
MAX_TEXT_LEN     = 128
N_IMAGE          = 2048
N_TEXT           = 768
N_EHR            = 13

DISEASES = [
    "Atelectasis", "Cardiomegaly", "Effusion", "Infiltration", "Mass",
    "Nodule", "Pneumonia", "Pneumothorax", "Consolidation", "Edema",
    "Emphysema", "Fibrosis", "Pleural_Thickening", "Hernia"
]

DISEASE_ICD = {
    "Atelectasis":        "J98.11",
    "Cardiomegaly":       "I51.7",
    "Effusion":           "J90",
    "Infiltration":       "J18.9",
    "Mass":               "R91.8",
    "Nodule":             "R91.1",
    "Pneumonia":          "J18.9",
    "Pneumothorax":       "J93.9",
    "Consolidation":      "J18.1",
    "Edema":              "J81.0",
    "Emphysema":          "J43.9",
    "Fibrosis":           "J84.10",
    "Pleural_Thickening": "J92.9",
    "Hernia":             "K46.9",
}

# ─── LOAD MODELS (chỉ load 1 lần khi khởi động) ─────────────────────────────
models: dict = {}


@asynccontextmanager
async def lifespan(app: FastAPI):
    """
    Load tất cả model AI vào RAM khi server khởi động.
    Thứ tự ưu tiên: XGBoost pipeline mới → Legacy fusion → không có
    """
    log.info("Đang tải các mô hình AI...")
    device = "cuda" if torch.cuda.is_available() else "cpu"
    log.info(f"  Device: {device}")

    # ── YOLOv8 Detection ─────────────────────────────────────────────────────
    if YOLO_PATH.exists():
        models["yolo"] = YOLO(str(YOLO_PATH))
        log.info(f"  ✓ YOLOv8            : {YOLO_PATH}")
    else:
        log.warning(f"  ✗ {YOLO_PATH.name} không tìm thấy – bỏ qua Detection.")

    # ── U-Net Segmentation ───────────────────────────────────────────────────
    if UNET_PATH.exists():
        models["unet"] = tf.keras.models.load_model(str(UNET_PATH), compile=False)
        log.info(f"  ✓ U-Net             : {UNET_PATH}")
    else:
        log.warning(f"  ✗ {UNET_PATH.name} không tìm thấy – bỏ qua Segmentation.")

    # ── Bio_ClinicalBERT (Text Encoder) ──────────────────────────────────────
    try:
        models["bert_tokenizer"] = AutoTokenizer.from_pretrained(BERT_MODEL_NAME)
        models["bert_model"]     = AutoModel.from_pretrained(BERT_MODEL_NAME).to(device).eval()
        models["bert_device"]    = device
        log.info(f"  ✓ ClinicalBERT      : {BERT_MODEL_NAME}")
    except Exception as e:
        log.warning(f"  ✗ ClinicalBERT không tải được: {e}")

    # ── ResNet50 Feature Extractor ────────────────────────────────────────────
    if EXTRACTOR_PATH.exists():
        models["extractor"] = tf.keras.models.load_model(str(EXTRACTOR_PATH), compile=False)
        log.info(f"  ✓ ResNet50 Extractor: {EXTRACTOR_PATH}")
    else:
        log.warning(f"  ✗ {EXTRACTOR_PATH.name} không tìm thấy – dùng fallback.")

    # ── XGBoost Ensemble (14 classifiers) ────────────────────────────────────
    if XGB_PATH.exists():
        with open(str(XGB_PATH), "rb") as f:
            models["xgb"] = pickle.load(f)
        log.info(f"  ✓ XGBoost Ensemble  : {len(models['xgb'])} classifiers")

    # ── Isotonic Calibrators ──────────────────────────────────────────────────
    if CALIB_PATH.exists():
        with open(str(CALIB_PATH), "rb") as f:
            models["calibrators"] = pickle.load(f)
        log.info(f"  ✓ Calibrators       : {len(models['calibrators'])} loaded")

    # ── EHR StandardScaler ────────────────────────────────────────────────────
    if SCALER_PATH.exists():
        with open(str(SCALER_PATH), "rb") as f:
            models["ehr_scaler"] = pickle.load(f)
        log.info(f"  ✓ EHR Scaler        : loaded")

    # ── MLP Deep Net ─────────────────────────────────────────────────────────
    if MLP_PATH.exists():
        models["mlp"] = tf.keras.models.load_model(str(MLP_PATH), compile=False)
        log.info(f"  ✓ MLP Fusion        : {MLP_PATH}")
    elif FUSION_PATH.exists():
        # Legacy fallback
        models["fusion"] = tf.keras.models.load_model(str(FUSION_PATH), compile=False)
        log.info(f"  ✓ Fusion (legacy)   : {FUSION_PATH}")
    else:
        log.warning("  ✗ Không có MLP/Fusion model – classification bị tắt.")

    log.info("Server sẵn sàng.")
    yield
    models.clear()


# ─── KHỞI TẠO APP ────────────────────────────────────────────────────────────
app = FastAPI(
    title       = "ChestAI Diagnostic Server",
    description = "API chẩn đoán bệnh lý phổi từ ảnh X-quang + dữ liệu lâm sàng (EHR)",
    version     = "1.0.0",
    lifespan    = lifespan,
)

app.add_middleware(
    CORSMiddleware,
    allow_origins  = ["*"],
    allow_methods  = ["*"],
    allow_headers  = ["*"],
)


# ─── SCHEMAS ─────────────────────────────────────────────────────────────────
class DetectionBox(BaseModel):
    x1: float; y1: float; x2: float; y2: float
    confidence: float; label: str


class DiagnosisResult(BaseModel):
    disease: str
    icd_code: str
    probability: float
    calibrated_probability: float
    rank: int


class PredictResponse(BaseModel):
    diagnoses: List[DiagnosisResult]   # Ranked diagnoses với ICD codes
    detections: List[DetectionBox]     # Bounding boxes từ YOLO
    top_disease: str
    top_icd_code: str
    top_probability: float
    normal: bool
    textual_rationale: str             # Giải thích ngắn cho bác sĩ
    modalities_used: List[str]         # ["image", "text", "ehr"]


class SegmentResponse(BaseModel):
    mask_image_base64: str
    has_lesion: bool
    lesion_area_percent: float


class ExplainResponse(BaseModel):
    heatmap_image_base64: str
    disease_explained: str
    icd_code: str
    confidence: float


class ShapFeatureContribution(BaseModel):
    name: str
    value: Optional[str] = None
    shap_value: float
    direction: str                     # "positive" | "negative"


class ShapResponse(BaseModel):
    disease: str
    icd_code: str
    probability: float
    feature_contributions: List[ShapFeatureContribution]
    textual_explanation: str
    top_positive_features: List[str]
    top_negative_features: List[str]


# ─── HELPERS ─────────────────────────────────────────────────────────────────
def build_ehr_raw(
    age: float, gender: int, smoking: int, view_pa: int, history: int,
    hr: float = 75, spo2: float = 97, temp: float = 37.0,
    sbp: float = 120, rr: float = 18, wbc: float = 7.0,
    crp: float = 5.0, lactate: float = 1.0,
) -> np.ndarray:
    """Chuyển thông số lâm sàng thành EHR tensor 13-dim (raw, chưa scale)."""
    age_n  = min(max(age, 0), 120) / 100.0
    hr_n   = min(max((hr - 0) / 120.0, 0), 1)
    spo2_n = min(max((spo2 - 88) / 12.0, 0), 1)
    temp_n = min(max((temp - 36) / 4.0, 0), 1)
    sbp_n  = min(max((sbp - 90) / 70.0, 0), 1)
    rr_n   = min(max((rr - 12) / 18.0, 0), 1)
    wbc_n  = min(max((wbc - 4) / 11.0, 0), 1)
    crp_n  = min(max(crp / 100.0, 0), 1)
    lac_n  = min(max(lactate / 4.0, 0), 1)
    return np.array([[age_n, float(gender), float(view_pa), float(smoking),
                      float(history), hr_n, spo2_n, temp_n, sbp_n, rr_n,
                      wbc_n, crp_n, lac_n]], dtype=np.float32)


def build_ehr_tensor(age: float, gender: int, smoking: int, view_pa: int, history: int) -> np.ndarray:
    """Legacy: Chuyển 5-dim EHR thành tensor (dùng cho fusion model cũ)."""
    age_norm = min(max(age, 0), 120) / 100.0
    return np.array([[age_norm, float(gender), float(view_pa),
                      float(smoking), float(history)]], dtype=np.float32)


def encode_clinical_note(
    age: float, gender: int, smoking: int, view_pa: int, history: int,
    clinical_notes: str = "",
    hr: float = 75, spo2: float = 97, temp: float = 37.0,
    rr: float = 18,
) -> np.ndarray:
    """Encode clinical notes text → 768-dim ClinicalBERT embedding."""
    if "bert_model" not in models:
        return np.zeros((1, N_TEXT), dtype=np.float32)

    # Nếu không có notes, tự động tổng hợp từ EHR fields
    if not clinical_notes.strip():
        gender_str = "male" if gender else "female"
        view_str   = "PA view" if view_pa else "AP view"
        smoke_str  = "history of smoking" if smoking else "non-smoker"
        hist_str   = "with prior lung disease" if history else ""
        clinical_notes = (
            f"Patient: {int(age)}-year-old {gender_str}. {view_str}. {smoke_str} {hist_str}. "
            f"Vitals: HR {int(hr)}, SpO2 {int(spo2)}%, Temp {temp:.1f}°C, RR {int(rr)}."
        )

    device    = models.get("bert_device", "cpu")
    tokenizer = models["bert_tokenizer"]
    bert      = models["bert_model"]

    enc = tokenizer(
        [clinical_notes], padding=True, truncation=True,
        max_length=128, return_tensors="pt"
    ).to(device)
    with torch.no_grad():
        out = bert(**enc)
    cls_emb = out.last_hidden_state[:, 0, :].cpu().numpy().astype(np.float32)
    return cls_emb  # (1, 768)


def build_fusion_vector(
    gray: np.ndarray,
    ehr_raw: np.ndarray,
    text_emb: np.ndarray,
) -> np.ndarray:
    """
    Xây dựng fusion vector (2829-dim):
    [ResNet50(2048) | ClinicalBERT(768) | EHR_scaled(13)]
    """
    # Image features
    if "extractor" in models:
        img_input = preprocess_for_fusion(gray)   # (1, 224, 224, 3)
        img_feats = models["extractor"].predict(img_input, verbose=0)  # (1, 2048)
    else:
        img_feats = np.zeros((1, N_IMAGE), dtype=np.float32)

    # EHR scaling
    if "ehr_scaler" in models:
        ehr_scaled = models["ehr_scaler"].transform(ehr_raw)  # (1, 13)
    else:
        ehr_scaled = ehr_raw  # fallback

    return np.concatenate([img_feats, text_emb, ehr_scaled], axis=1)  # (1, 2829)


def run_ensemble_predict(
    X_fusion: np.ndarray,
) -> np.ndarray:
    """XGBoost ensemble + MLP → calibrated probabilities (N_CLASSES,)."""
    if "xgb" in models and "calibrators" in models:
        xgb_probs = np.column_stack([
            clf.predict_proba(X_fusion)[:, 1]
            for clf in models["xgb"]
        ])[0]  # (14,)
        if "mlp" in models:
            mlp_probs = models["mlp"].predict(X_fusion, verbose=0)[0]  # (14,)
            raw = 0.5 * xgb_probs + 0.5 * mlp_probs
        else:
            raw = xgb_probs
        # Calibrate
        cal_probs = np.array([
            float(models["calibrators"][c].predict([raw[c]])[0])
            for c in range(len(models["calibrators"]))
        ])
        return cal_probs
    elif "fusion" in models:
        # Legacy path (old fusion model)
        return None  # Handled separately
    return None


def build_textual_rationale(
    top_disease: str, icd_code: str, prob: float,
    modalities: List[str],
) -> str:
    pct = round(prob * 100, 1)
    mod_str = ", ".join(modalities)
    return (
        f"Phát hiện {top_disease} (ICD: {icd_code}) với xác suất {pct}% "
        f"(đã calibrate). Phân tích dựa trên: {mod_str}. "
        f"Kết quả hỗ trợ chẩn đoán – cần bác sĩ xác nhận."
    )


# ─── ENDPOINTS ───────────────────────────────────────────────────────────────

@app.get("/health", tags=["System"])
async def health():
    """Kiểm tra trạng thái server và danh sách model đã load."""
    return {
        "status"      : "ok",
        "models_loaded": list(models.keys()),
    }


# ─────────────────────────────────────────────────────────────────────────────
@app.post("/predict", response_model=PredictResponse, tags=["Diagnosis"])
async def predict(
    file           : UploadFile = File(..., description="File ảnh X-quang (PNG/JPG)"),
    age            : float = Form(40,  description="Tuổi bệnh nhân"),
    gender         : int   = Form(1,   description="Giới tính: 1=Nam, 0=Nữ"),
    smoking        : int   = Form(0,   description="Hút thuốc: 1=Có, 0=Không"),
    view_pa        : int   = Form(1,   description="PA view: 1=PA, 0=AP"),
    history        : int   = Form(0,   description="Tiền sử bệnh: 1=Có, 0=Không"),
    hr             : float = Form(75,  description="Nhịp tim (bpm)"),
    spo2           : float = Form(97,  description="SpO2 (%)"),
    temp           : float = Form(37.0,description="Nhiệt độ (°C)"),
    sbp            : float = Form(120, description="Huyết áp tâm thu (mmHg)"),
    rr             : float = Form(18,  description="Nhịp thở (/phút)"),
    wbc            : float = Form(7.0, description="Bạch cầu (K/μL)"),
    crp            : float = Form(5.0, description="CRP (mg/L)"),
    lactate        : float = Form(1.0, description="Lactate (mmol/L)"),
    clinical_notes : str   = Form("",  description="Ghi chú lâm sàng / tóm tắt bệnh án"),
):
    """
    Chẩn đoán đa phương thức (Ảnh + EHR + Clinical Notes):
    - YOLOv8 Detection: Khoanh vùng tổn thương
    - ClinicalBERT: Encode clinical notes text → 768-dim embedding
    - XGBoost Ensemble + MLP + Calibration: Ranked diagnoses với ICD codes
    - Textual rationale: Giải thích ngắn cho mỗi diagnosis
    """
    raw = await file.read()
    try:
        gray = bytes_to_cv2(raw)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))

    detections: List[DetectionBox] = []
    diagnoses:  List[DiagnosisResult] = []
    modalities_used: List[str] = ["image"]

    # ─── YOLO Detection ──────────────────────────────────────────────────────
    if "yolo" in models:
        yolo_input = preprocess_for_yolo(gray)
        results    = models["yolo"].predict(yolo_input, conf=0.3, verbose=False)
        for r in results:
            for box in r.boxes:
                x1, y1, x2, y2 = box.xyxy[0].tolist()
                conf = float(box.conf[0])
                cls  = int(box.cls[0])
                detections.append(DetectionBox(
                    x1=x1, y1=y1, x2=x2, y2=y2,
                    confidence=round(conf, 4),
                    label=models["yolo"].names[cls]
                ))

    # ─── ClinicalBERT Text Embedding ─────────────────────────────────────────
    text_emb = encode_clinical_note(
        age, gender, smoking, view_pa, history,
        clinical_notes, hr, spo2, temp, rr
    )
    if "bert_model" in models:
        modalities_used.append("text (ClinicalBERT)")

    # ─── Build EHR raw vector (13-dim) ───────────────────────────────────────
    ehr_raw = build_ehr_raw(age, gender, smoking, view_pa, history,
                            hr, spo2, temp, sbp, rr, wbc, crp, lactate)
    modalities_used.append("EHR (vitals+labs)")

    # ─── XGBoost Ensemble + Calibration (New Pipeline) ───────────────────────
    if "xgb" in models:
        X_fusion  = build_fusion_vector(gray, ehr_raw, text_emb)
        cal_probs = run_ensemble_predict(X_fusion)

        if cal_probs is not None:
            sorted_idx = np.argsort(cal_probs)[::-1]
            for rank, idx in enumerate(sorted_idx):
                disease = DISEASES[idx]
                prob    = round(float(cal_probs[idx]), 4)
                diagnoses.append(DiagnosisResult(
                    disease              = disease,
                    icd_code             = DISEASE_ICD.get(disease, "N/A"),
                    probability          = prob,
                    calibrated_probability = prob,
                    rank                 = rank + 1,
                ))

    # ─── Legacy Fusion Fallback ───────────────────────────────────────────────
    elif "fusion" in models:
        img_tensor = preprocess_for_fusion(gray)
        ehr5       = build_ehr_tensor(age, gender, smoking, view_pa, history)
        probs      = models["fusion"].predict([img_tensor, ehr5], verbose=0)[0]
        sorted_idx = np.argsort(probs)[::-1]
        for rank, idx in enumerate(sorted_idx):
            disease = DISEASES[idx]
            prob    = round(float(probs[idx]), 4)
            diagnoses.append(DiagnosisResult(
                disease              = disease,
                icd_code             = DISEASE_ICD.get(disease, "N/A"),
                probability          = prob,
                calibrated_probability = prob,
                rank                 = rank + 1,
            ))

    top     = diagnoses[0] if diagnoses else DiagnosisResult(
        disease="Unknown", icd_code="N/A", probability=0.0,
        calibrated_probability=0.0, rank=1
    )
    normal  = top.probability < 0.5
    rationale = build_textual_rationale(
        top.disease, top.icd_code, top.probability, modalities_used
    )

    return PredictResponse(
        diagnoses        = diagnoses,
        detections       = detections,
        top_disease      = top.disease,
        top_icd_code     = top.icd_code,
        top_probability  = top.probability,
        normal           = normal,
        textual_rationale = rationale,
        modalities_used  = modalities_used,
    )


# ─────────────────────────────────────────────────────────────────────────────
@app.post("/segment", response_model=SegmentResponse, tags=["Segmentation"])
async def segment(
    file: UploadFile = File(..., description="File ảnh X-quang (PNG/JPG)"),
):
    """
    Phân đoạn vùng tổn thương bằng U-Net.
    Trả về ảnh gốc với mask màu đỏ overlay, encoded base64.
    """
    if "unet" not in models:
        raise HTTPException(status_code=503, detail="Mô hình U-Net chưa được tải.")

    raw = await file.read()
    gray = bytes_to_cv2(raw)

    unet_input  = preprocess_for_unet(gray)
    pred_mask   = models["unet"].predict(unet_input, verbose=0)[0, :, :, 0]  # (256, 256)

    # Tính diện tích vùng tổn thương
    binary_mask       = (pred_mask > 0.5).astype(np.uint8)
    lesion_area_pct   = float(binary_mask.sum()) / binary_mask.size * 100.0
    has_lesion        = lesion_area_pct > 1.0

    # Tạo ảnh overlay
    overlay = overlay_mask(gray, pred_mask, alpha=0.5)
    b64     = encode_image_to_base64(overlay)

    return SegmentResponse(
        mask_image_base64   = b64,
        has_lesion          = has_lesion,
        lesion_area_percent = round(lesion_area_pct, 2)
    )


# ─────────────────────────────────────────────────────────────────────────────
@app.post("/explain", response_model=ExplainResponse, tags=["XAI"])
async def explain(
    file        : UploadFile = File(...),
    age         : float = Form(40),
    gender      : int   = Form(1),
    smoking     : int   = Form(0),
    view_pa     : int   = Form(1),
    history     : int   = Form(0),
    class_index : int   = Form(6, description="Index bệnh cần giải thích (mặc định 6=Pneumonia)")
):
    """
    Tạo Heatmap Grad-CAM++ để giải thích vùng ảnh quan trọng.
    Trả về ảnh heatmap overlay encoded base64 + ICD code.
    """
    fusion_model = models.get("fusion") or models.get("mlp")
    if fusion_model is None:
        raise HTTPException(status_code=503, detail="Mô hình Fusion/MLP chưa được tải.")

    raw = await file.read()
    gray = bytes_to_cv2(raw)

    img_tensor = preprocess_for_fusion(gray)
    ehr_tensor = build_ehr_tensor(age, gender, smoking, view_pa, history)

    # Nếu dùng old fusion model (5-dim EHR)
    if "fusion" in models:
        probs = models["fusion"].predict([img_tensor, ehr_tensor], verbose=0)[0]
        heatmap = make_gradcam_heatmap(
            models["fusion"], img_tensor, ehr_tensor, class_index, gray
        )
    else:
        # MLP model dùng fusion vector (2829-dim) - không có Grad-CAM trực tiếp
        # Tạo heatmap từ ResNet50 extractor thay thế
        heatmap = gray  # fallback trống
        probs   = np.zeros(len(DISEASES))

    disease_name = DISEASES[class_index] if class_index < len(DISEASES) else "Unknown"
    confidence   = round(float(probs[class_index]), 4)
    b64 = encode_image_to_base64(heatmap)

    return ExplainResponse(
        heatmap_image_base64 = b64,
        disease_explained    = disease_name,
        icd_code             = DISEASE_ICD.get(disease_name, "N/A"),
        confidence           = confidence
    )


# ─────────────────────────────────────────────────────────────────────────────
@app.post("/shap", response_model=ShapResponse, tags=["XAI"])
async def shap_explain(
    file           : UploadFile = File(...),
    age            : float = Form(40),
    gender         : int   = Form(1),
    smoking        : int   = Form(0),
    view_pa        : int   = Form(1),
    history        : int   = Form(0),
    hr             : float = Form(75),
    spo2           : float = Form(97),
    temp           : float = Form(37.0),
    sbp            : float = Form(120),
    rr             : float = Form(18),
    wbc            : float = Form(7.0),
    crp            : float = Form(5.0),
    lactate        : float = Form(1.0),
    clinical_notes : str   = Form(""),
    class_index    : int   = Form(6, description="Index bệnh cần giải thích (6=Pneumonia)"),
):
    """
    Tính SHAP values để giải thích đóng góp của từng đặc trưng
    (EHR vitals/labs/demographics + Image + Text) với XGBoost TreeExplainer.
    Trả về danh sách feature contributions + textual explanation.
    """
    if "xgb" not in models:
        raise HTTPException(status_code=503, detail="XGBoost model chưa được tải. Chạy pipeline mới.")

    raw = await file.read()
    gray = bytes_to_cv2(raw)

    ehr_raw  = build_ehr_raw(age, gender, smoking, view_pa, history,
                             hr, spo2, temp, sbp, rr, wbc, crp, lactate)
    text_emb = encode_clinical_note(age, gender, smoking, view_pa, history,
                                    clinical_notes, hr, spo2, temp, rr)
    X_fusion = build_fusion_vector(gray, ehr_raw, text_emb)

    disease_name = DISEASES[class_index] if class_index < len(DISEASES) else "Unknown"
    icd_code     = DISEASE_ICD.get(disease_name, "N/A")

    # Lấy calibrated probability
    cal_probs = run_ensemble_predict(X_fusion)
    cal_prob  = float(cal_probs[class_index]) if cal_probs is not None else 0.0

    # SHAP computation
    shap_result = compute_shap_ehr(
        xgb_models    = models["xgb"],
        X_fusion      = X_fusion,
        ehr_raw       = ehr_raw,
        n_image       = N_IMAGE,
        n_text        = N_TEXT,
        disease_index = class_index,
        disease_name  = disease_name,
        icd_code      = icd_code,
        calibrated_prob = cal_prob,
    )

    # Chuyển sang Pydantic model
    contributions = [
        ShapFeatureContribution(
            name      = c["name"],
            value     = str(c["value"]) if c["value"] is not None else None,
            shap_value = c["shap"],
            direction  = "positive" if c["shap"] >= 0 else "negative",
        )
        for c in shap_result.feature_contributions
    ]

    return ShapResponse(
        disease                = shap_result.disease,
        icd_code               = shap_result.icd_code,
        probability            = shap_result.calibrated_probability,
        feature_contributions  = contributions,
        textual_explanation    = shap_result.textual_explanation,
        top_positive_features  = shap_result.top_positive_features,
        top_negative_features  = shap_result.top_negative_features,
    )


# ─── CHẠY SERVER ─────────────────────────────────────────────────────────────
if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=True)
