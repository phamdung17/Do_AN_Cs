"""
GIAI ĐOẠN 1 - NHÁNH 4: Multimodal Fusion HOÀN CHỈNH theo yêu cầu đề
=======================================================================
Đầu vào (3 modality):
  - Ảnh X-quang (Image)   → ResNet50 CNN features (2048-dim)
  - Ghi chú lâm sàng (Text) → Bio_ClinicalBERT embedding (768-dim)
  - EHR có cấu trúc       → vitals + lab tests + demographics (13-dim)

Đầu ra:
  - 14 nhãn bệnh (label), xác suất đã calibrate, ICD codes
  - Sensitivity & Specificity per class

Pipeline:
  1. Feature extraction (ResNet50 offline) + ClinicalBERT embeddings
  2. XGBoost ensemble + MLP deep net
  3. Probability calibration (Isotonic Regression)
  4. Clinical evaluation: ROC-AUC, sensitivity, specificity

Dataset: ChestX-ray14 (NIH)
Chạy trên Kaggle Notebook – GPU P100
"""

import os
import gc
import pickle
import numpy as np
import pandas as pd
import cv2
import torch
import tensorflow as tf
from pathlib import Path
from typing import List, Tuple
from sklearn.model_selection import train_test_split
from sklearn.calibration import CalibratedClassifierCV
from sklearn.isotonic import IsotonicRegression
from sklearn.preprocessing import StandardScaler
from sklearn.metrics import roc_auc_score, confusion_matrix
from xgboost import XGBClassifier
from transformers import AutoTokenizer, AutoModel

# ─── CẤU HÌNH ────────────────────────────────────────────────────────────────
DATA_DIR       = "/kaggle/input/nih-chest-xrays"
# NIH dataset chia ảnh thành nhiều thư mục con: images_001 ... images_012
IMAGE_DIRS     = [os.path.join(DATA_DIR, f"images_{i:03d}") for i in range(1, 13)]
LABEL_CSV      = os.path.join(DATA_DIR, "Data_Entry_2017.csv")
SAVE_DIR       = "/kaggle/working"
IMG_SIZE       = 224
BATCH_CNN      = 64       # Batch khi trích xuất CNN features
BERT_MODEL     = "emilyalsentzer/Bio_ClinicalBERT"  # HuggingFace ClinicalBERT
MAX_TEXT_LEN   = 128
FUSION_EPOCHS  = 20
FUSION_LR      = 1e-4
FUSION_BATCH   = 32

DISEASES = [
    "Atelectasis", "Cardiomegaly", "Effusion", "Infiltration", "Mass",
    "Nodule", "Pneumonia", "Pneumothorax", "Consolidation", "Edema",
    "Emphysema", "Fibrosis", "Pleural_Thickening", "Hernia"
]

# ICD-10 codes tương ứng
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

N_CLASSES = len(DISEASES)
N_EHR     = 13
N_IMAGE   = 2048
N_TEXT    = 768
N_TOTAL   = N_IMAGE + N_TEXT + N_EHR


# ═══════════════════════════════════════════════════════════════════════════════
# BƯỚC 1: XỬ LÝ NHÃN
# ═══════════════════════════════════════════════════════════════════════════════
def process_labels(df: pd.DataFrame) -> np.ndarray:
    """Chuyển chuỗi nhãn 'Atelectasis|Effusion' → vector multi-hot (N_CLASSES)."""
    labels = np.zeros((len(df), N_CLASSES), dtype=np.float32)
    for i, row in df.iterrows():
        for disease in str(row["Finding Labels"]).split("|"):
            if disease in DISEASES:
                labels[i][DISEASES.index(disease)] = 1.0
    return labels


# ═══════════════════════════════════════════════════════════════════════════════
# BƯỚC 2: EHR CÓ CẤU TRÚC (Vitals + Lab Tests + Demographics)
# ═══════════════════════════════════════════════════════════════════════════════
def generate_ehr_features(df: pd.DataFrame, seed: int = 42) -> np.ndarray:
    """
    Tạo 13 đặc trưng lâm sàng mô phỏng từ metadata ChestX-ray14.
    Trong hệ thống thực tế, các giá trị này lấy từ HIS / EMR.

    Features:
      [0]  age_norm       – tuổi / 100
      [1]  gender         – 1=Nam, 0=Nữ
      [2]  view_pa        – 1=PA, 0=AP
      [3]  smoking        – hút thuốc lá
      [4]  history        – tiền sử bệnh phổi
      [5]  hr_norm        – nhịp tim (normalize 60-120 → 0-1)
      [6]  spo2_norm      – SpO2 (normalize 88-100 → 0-1)
      [7]  temp_norm      – nhiệt độ (normalize 36-40 → 0-1)
      [8]  sbp_norm       – huyết áp tâm thu (normalize 90-160 → 0-1)
      [9]  rr_norm        – nhịp thở (normalize 12-30 → 0-1)
      [10] wbc_norm       – bạch cầu (normalize 4-15 → 0-1)
      [11] crp_norm       – CRP (normalize 0-100 → 0-1)
      [12] lactate_norm   – lactate (normalize 0-4 → 0-1)
    """
    rng = np.random.default_rng(seed)
    n = len(df)

    ehr = np.zeros((n, N_EHR), dtype=np.float32)
    ehr[:, 0] = (pd.to_numeric(df["Patient Age"], errors="coerce").fillna(40) / 100.0).values
    ehr[:, 1] = (df["Patient Gender"] == "M").astype(float).values
    ehr[:, 2] = (df["View Position"] == "PA").astype(float).values
    ehr[:, 3] = rng.integers(0, 2, n).astype(float)       # smoking
    ehr[:, 4] = rng.integers(0, 2, n).astype(float)       # history
    # Vitals (simulate với Gaussian noise tương quan với tuổi)
    ages = ehr[:, 0] * 100
    ehr[:, 5] = np.clip((rng.normal(75, 10, n) + ages * 0.2) / 120.0, 0, 1)  # HR
    ehr[:, 6] = np.clip(1 - rng.exponential(0.02, n), 0, 1)                   # SpO2
    ehr[:, 7] = np.clip((rng.normal(37, 0.5, n) - 36) / 4.0, 0, 1)           # Temp
    ehr[:, 8] = np.clip((rng.normal(120, 15, n) - 90) / 70.0, 0, 1)           # SBP
    ehr[:, 9] = np.clip((rng.normal(18, 4, n) - 12) / 18.0, 0, 1)             # RR
    # Lab tests
    ehr[:, 10] = np.clip((rng.exponential(7, n) - 4) / 11.0, 0, 1)            # WBC
    ehr[:, 11] = np.clip(rng.exponential(10, n) / 100.0, 0, 1)                # CRP
    ehr[:, 12] = np.clip(rng.exponential(1, n) / 4.0, 0, 1)                   # Lactate

    return ehr


# ═══════════════════════════════════════════════════════════════════════════════
# BƯỚC 3: SINH VĂN BẢN LÂM SÀNG (Clinical Notes → Text Modality)
# ═══════════════════════════════════════════════════════════════════════════════
def generate_clinical_notes(df: pd.DataFrame, ehr: np.ndarray) -> List[str]:
    """
    Tổng hợp ghi chú lâm sàng dạng text từ metadata + EHR.
    Trong hệ thống thực tế, đây là discharge summary / radiology report text.
    Format: "Patient: [age]yo [gender]. View: [PA/AP]. [Symptoms/History]."
    """
    notes = []
    for i, (_, row) in enumerate(df.iterrows()):
        age    = int(ehr[i, 0] * 100)
        gender = "male" if ehr[i, 1] > 0.5 else "female"
        view   = "PA view" if ehr[i, 2] > 0.5 else "AP view"
        smoke  = "history of smoking" if ehr[i, 3] > 0.5 else "non-smoker"
        hist   = "with prior lung disease" if ehr[i, 4] > 0.5 else ""

        hr  = int(ehr[i, 5] * 120)
        spo2 = int(ehr[i, 6] * 12 + 88)
        temp = round(ehr[i, 7] * 4 + 36, 1)
        rr   = int(ehr[i, 9] * 18 + 12)

        finding = row["Finding Labels"].replace("|", ", ").replace("No Finding", "no abnormality")

        note = (
            f"Patient: {age}-year-old {gender}. {view}. {smoke} {hist}. "
            f"Vitals: HR {hr}, SpO2 {spo2}%, T {temp}°C, RR {rr}. "
            f"Chest X-ray findings: {finding}."
        )
        notes.append(note)
    return notes


# ═══════════════════════════════════════════════════════════════════════════════
# BƯỚC 4: TRÍCH XUẤT BERT EMBEDDINGS (ClinicalBERT)
# ═══════════════════════════════════════════════════════════════════════════════
def extract_bert_embeddings(
    notes: List[str],
    model_name: str = BERT_MODEL,
    batch_size: int = 32,
    max_len: int = MAX_TEXT_LEN,
    device: str = "cuda"
) -> np.ndarray:
    """
    Dùng Bio_ClinicalBERT (HuggingFace) để trích [CLS] token embedding.
    Output: (N, 768) float32
    """
    print(f"Đang tải ClinicalBERT: {model_name}...")
    tokenizer = AutoTokenizer.from_pretrained(model_name)
    bert_model = AutoModel.from_pretrained(model_name).to(device).eval()

    all_embeddings = []
    for i in range(0, len(notes), batch_size):
        batch = notes[i : i + batch_size]
        enc = tokenizer(
            batch, padding=True, truncation=True,
            max_length=max_len, return_tensors="pt"
        ).to(device)
        with torch.no_grad():
            out = bert_model(**enc)
        cls_emb = out.last_hidden_state[:, 0, :].cpu().numpy()  # [CLS] token
        all_embeddings.append(cls_emb)

        if (i // batch_size) % 10 == 0:
            print(f"  BERT: {i}/{len(notes)} samples processed...")

    del bert_model
    torch.cuda.empty_cache()
    gc.collect()
    return np.concatenate(all_embeddings, axis=0).astype(np.float32)


# ═══════════════════════════════════════════════════════════════════════════════
# BƯỚC 5: TRÍCH XUẤT CNN IMAGE FEATURES (ResNet50 offline)
# ═══════════════════════════════════════════════════════════════════════════════
def build_resnet_extractor(img_size: int = IMG_SIZE) -> tf.keras.Model:
    """ResNet50 pretrained → GlobalAveragePooling → 2048-dim feature vector."""
    base = tf.keras.applications.ResNet50(
        include_top=False, weights="imagenet",
        input_shape=(img_size, img_size, 3)
    )
    base.trainable = False
    x = tf.keras.layers.GlobalAveragePooling2D()(base.output)
    return tf.keras.Model(inputs=base.input, outputs=x)


def load_image(img_path: str, size: int = IMG_SIZE) -> np.ndarray:
    img = cv2.imread(img_path, cv2.IMREAD_GRAYSCALE)
    if img is None:
        return np.zeros((size, size, 3), dtype=np.float32)
    img = cv2.resize(img, (size, size))
    img = cv2.equalizeHist(img)
    img = np.stack([img] * 3, axis=-1).astype(np.float32) / 255.0
    return img


def find_image_path(filename: str, image_dirs: List[str]) -> str:
    """Tìm file ảnh trong các thư mục con images_001 ... images_012."""
    for d in image_dirs:
        p = os.path.join(d, filename)
        if os.path.exists(p):
            return p
    return ""  # Không tìm thấy


def extract_image_features(
    img_filenames: List[str],
    extractor: tf.keras.Model,
    image_dirs: List[str],
    batch_size: int = BATCH_CNN
) -> np.ndarray:
    """Offline feature extraction: ảnh → ResNet50 → 2048-dim vector."""
    all_feats = []
    for i in range(0, len(img_filenames), batch_size):
        batch_names = img_filenames[i : i + batch_size]
        batch_imgs  = np.array([load_image(find_image_path(fn, image_dirs)) for fn in batch_names])
        feats = extractor.predict(batch_imgs, verbose=0)
        all_feats.append(feats)
        if (i // batch_size) % 20 == 0:
            print(f"  CNN: {i}/{len(img_filenames)} images extracted...")
    return np.concatenate(all_feats, axis=0).astype(np.float32)


# ═══════════════════════════════════════════════════════════════════════════════
# BƯỚC 6: FUSION FEATURE VECTOR
# ═══════════════════════════════════════════════════════════════════════════════
def fuse_features(
    img_feats: np.ndarray,   # (N, 2048)
    bert_feats: np.ndarray,  # (N, 768)
    ehr_feats: np.ndarray,   # (N, 13)
) -> np.ndarray:
    """Concatenate 3 modality → (N, 2829) fusion vector."""
    scaler = StandardScaler()
    ehr_scaled = scaler.fit_transform(ehr_feats)
    #  Lưu scaler để dùng khi inference
    with open(os.path.join(SAVE_DIR, "ehr_scaler.pkl"), "wb") as f:
        pickle.dump(scaler, f)
    return np.concatenate([img_feats, bert_feats, ehr_scaled], axis=1)


# ═══════════════════════════════════════════════════════════════════════════════
# BƯỚC 7: XGBoost ENSEMBLE + MLP DEEP NET
# ═══════════════════════════════════════════════════════════════════════════════
def train_xgboost_per_class(
    X_train: np.ndarray, y_train: np.ndarray,
    X_val:   np.ndarray, y_val:   np.ndarray,
) -> List[XGBClassifier]:
    """
    Huấn luyện XGBoost binary classifier cho mỗi trong 14 bệnh (OvR).
    Trả về danh sách 14 model.
    """
    print("\n=== Huấn luyện XGBoost (14 classifiers) ===")
    models_xgb = []
    for c, disease in enumerate(DISEASES):
        y_c   = y_train[:, c]
        pos_w = max((y_c == 0).sum() / (y_c == 1).sum(), 1.0)  # Handle imbalance

        clf = XGBClassifier(
            n_estimators     = 300,
            max_depth        = 5,
            learning_rate    = 0.05,
            subsample        = 0.8,
            colsample_bytree = 0.7,
            scale_pos_weight = pos_w,
            use_label_encoder= False,
            eval_metric      = "auc",
            tree_method      = "gpu_hist",
            device           = "cuda",
            verbosity        = 0,
        )
        clf.fit(
            X_train, y_c,
            eval_set     = [(X_val, y_val[:, c])],
            early_stopping_rounds = 20,
            verbose      = False,
        )
        auc = roc_auc_score(y_val[:, c], clf.predict_proba(X_val)[:, 1])
        print(f"  [{c+1:2d}/14] {disease:<20} XGB AUC: {auc:.4f}")
        models_xgb.append(clf)

    # Lưu XGBoost models
    with open(os.path.join(SAVE_DIR, "xgb_models.pkl"), "wb") as f:
        pickle.dump(models_xgb, f)
    print("Đã lưu xgb_models.pkl")
    return models_xgb


def build_mlp_model(input_dim: int = N_TOTAL) -> tf.keras.Model:
    """MLP deep net cho multimodal fusion vector (2829-dim input)."""
    inp = tf.keras.layers.Input(shape=(input_dim,), name="fusion_vec")
    x = tf.keras.layers.Dense(1024, activation="gelu")(inp)
    x = tf.keras.layers.BatchNormalization()(x)
    x = tf.keras.layers.Dropout(0.4)(x)
    x = tf.keras.layers.Dense(512, activation="gelu")(x)
    x = tf.keras.layers.BatchNormalization()(x)
    x = tf.keras.layers.Dropout(0.3)(x)
    x = tf.keras.layers.Dense(256, activation="gelu")(x)
    x = tf.keras.layers.Dropout(0.2)(x)
    out = tf.keras.layers.Dense(N_CLASSES, activation="sigmoid", name="output")(x)
    return tf.keras.Model(inputs=inp, outputs=out, name="mlp_fusion")


def train_mlp(
    X_train: np.ndarray, y_train: np.ndarray,
    X_val:   np.ndarray, y_val:   np.ndarray,
) -> tf.keras.Model:
    print("\n=== Huấn luyện MLP deep net ===")
    mlp = build_mlp_model(X_train.shape[1])
    mlp.compile(
        optimizer = tf.keras.optimizers.Adam(FUSION_LR),
        loss      = "binary_crossentropy",
        metrics   = [tf.keras.metrics.AUC(name="auc", multi_label=True)]
    )
    mlp.fit(
        X_train, y_train,
        validation_data = (X_val, y_val),
        epochs          = FUSION_EPOCHS,
        batch_size      = FUSION_BATCH,
        callbacks       = [
            tf.keras.callbacks.ModelCheckpoint(
                os.path.join(SAVE_DIR, "mlp_model.keras"),
                monitor="val_auc", mode="max", save_best_only=True, verbose=1
            ),
            tf.keras.callbacks.ReduceLROnPlateau(factor=0.5, patience=3),
            tf.keras.callbacks.EarlyStopping(monitor="val_auc", patience=7, mode="max"),
        ],
        verbose = 1,
    )
    return mlp


# ═══════════════════════════════════════════════════════════════════════════════
# BƯỚC 8: PROBABILITY CALIBRATION (Isotonic Regression)
# ═══════════════════════════════════════════════════════════════════════════════
def calibrate_ensemble(
    xgb_models: List[XGBClassifier],
    mlp_model:  tf.keras.Model,
    X_cal: np.ndarray,
    y_cal: np.ndarray,
) -> List[IsotonicRegression]:
    """
    Calibrate probabilities per class:
    raw_prob = mean(xgb_prob, mlp_prob)  →  IsotonicRegression  →  calibrated_prob
    """
    print("\n=== Calibrating probabilities ===")
    # XGBoost ensemble predictions
    xgb_probs = np.column_stack([
        clf.predict_proba(X_cal)[:, 1] for clf in xgb_models
    ])  # (N, 14)
    # MLP predictions
    mlp_probs = mlp_model.predict(X_cal, verbose=0)  # (N, 14)
    # Raw ensemble
    raw_probs = 0.5 * xgb_probs + 0.5 * mlp_probs

    calibrators = []
    for c in range(N_CLASSES):
        ir = IsotonicRegression(out_of_bounds="clip")
        ir.fit(raw_probs[:, c], y_cal[:, c])
        calibrators.append(ir)

    with open(os.path.join(SAVE_DIR, "calibrators.pkl"), "wb") as f:
        pickle.dump(calibrators, f)
    print("Đã lưu calibrators.pkl")
    return calibrators


# ═══════════════════════════════════════════════════════════════════════════════
# BƯỚC 9: CLINICAL EVALUATION (Sensitivity / Specificity / AUC)
# ═══════════════════════════════════════════════════════════════════════════════
def evaluate_clinical(
    xgb_models:  List[XGBClassifier],
    mlp_model:   tf.keras.Model,
    calibrators: List[IsotonicRegression],
    X_test: np.ndarray,
    y_test: np.ndarray,
    threshold: float = 0.5,
):
    """
    Đánh giá lâm sàng: AUC, Sensitivity (Recall), Specificity per class.
    - Sensitivity cao quan trọng cho tình trạng nguy kịch (Pneumonia, Pneumothorax).
    """
    # Dự đoán
    xgb_probs = np.column_stack([
        clf.predict_proba(X_test)[:, 1] for clf in xgb_models
    ])
    mlp_probs = mlp_model.predict(X_test, verbose=0)
    raw_probs = 0.5 * xgb_probs + 0.5 * mlp_probs
    # Apply calibration
    cal_probs = np.column_stack([
        cal.predict(raw_probs[:, c]) for c, cal in enumerate(calibrators)
    ])

    print("\n" + "=" * 70)
    print(f"{'Disease':<22} {'AUC':>6}  {'Sens':>6}  {'Spec':>6}  {'ICD':>8}")
    print("-" * 70)

    results = {}
    total_auc = 0.0
    for c, disease in enumerate(DISEASES):
        y_true  = y_test[:, c]
        y_score = cal_probs[:, c]
        y_pred  = (y_score >= threshold).astype(int)

        auc  = roc_auc_score(y_true, y_score) if y_true.sum() > 0 else 0.0
        cm   = confusion_matrix(y_true, y_pred)
        if cm.shape == (2, 2):
            tn, fp, fn, tp = cm.ravel()
            sens = tp / (tp + fn) if (tp + fn) > 0 else 0.0  # Sensitivity = Recall
            spec = tn / (tn + fp) if (tn + fp) > 0 else 0.0  # Specificity
        else:
            sens = spec = 0.0

        icd = DISEASE_ICD[disease]
        print(f"  {disease:<20}  {auc:.4f}   {sens:.4f}   {spec:.4f}   {icd}")
        results[disease] = {"auc": auc, "sensitivity": sens, "specificity": spec, "icd": icd}
        total_auc += auc

    print("-" * 70)
    print(f"  {'Mean AUC':<20}  {total_auc/N_CLASSES:.4f}")
    print("=" * 70)

    # Lưu kết quả
    pd.DataFrame(results).T.to_csv(os.path.join(SAVE_DIR, "clinical_evaluation.csv"))
    print("Đã lưu clinical_evaluation.csv")
    return results


# ═══════════════════════════════════════════════════════════════════════════════
# BƯỚC 10: LƯU TOÀN BỘ MODEL + METADATA
# ═══════════════════════════════════════════════════════════════════════════════
def save_all_artifacts(
    extractor: tf.keras.Model,
    xgb_models: List[XGBClassifier],
    calibrators: List[IsotonicRegression],
    bert_model_name: str,
):
    """Lưu tất cả các artifact cần thiết cho AI Server."""
    # ResNet50 feature extractor
    extractor.save(os.path.join(SAVE_DIR, "resnet50_extractor.keras"))
    # Danh sách bệnh + ICD codes
    meta = {"diseases": DISEASES, "icd_codes": DISEASE_ICD, "n_classes": N_CLASSES,
            "bert_model": bert_model_name, "ehr_dim": N_EHR,
            "image_dim": N_IMAGE, "text_dim": N_TEXT}
    with open(os.path.join(SAVE_DIR, "model_meta.pkl"), "wb") as f:
        pickle.dump(meta, f)
    print("\n=== Các file đã lưu ===")
    for fname in ["xgb_models.pkl", "mlp_model.keras", "calibrators.pkl",
                  "ehr_scaler.pkl", "resnet50_extractor.keras",
                  "model_meta.pkl", "clinical_evaluation.csv"]:
        fpath = os.path.join(SAVE_DIR, fname)
        if os.path.exists(fpath):
            size = os.path.getsize(fpath) / 1_000_000
            print(f"  ✓ {fname} ({size:.1f} MB)")


# ═══════════════════════════════════════════════════════════════════════════════
# MAIN
# ═══════════════════════════════════════════════════════════════════════════════
if __name__ == "__main__":
    # 1. Đọc metadata
    print("=" * 60)
    print("ChestAI – Multimodal Fusion Training (ClinicalBERT + XGBoost)")
    print("=" * 60)
    df = pd.read_csv(LABEL_CSV)
    df = df.sample(frac=1, random_state=42).reset_index(drop=True)
    print(f"Tổng số mẫu: {len(df)}")

    # 2. Nhãn multi-hot
    labels = process_labels(df)

    # 3. EHR features (13-dim)
    print("\nTạo EHR features (vitals + labs + demographics)...")
    ehr_feats = generate_ehr_features(df)

    # 4. Clinical notes text
    print("Tổng hợp ghi chú lâm sàng (clinical notes)...")
    notes = generate_clinical_notes(df, ehr_feats)
    print(f"  Ví dụ: {notes[0][:80]}...")

    # 5. BERT embeddings
    device = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"\nTrích xuất ClinicalBERT embeddings (device={device})...")
    bert_feats = extract_bert_embeddings(notes, BERT_MODEL, batch_size=64, device=device)
    print(f"  BERT features shape: {bert_feats.shape}")

    # 6. CNN image features (ResNet50 offline)
    print("\nTrích xuất CNN features (ResNet50)...")
    extractor      = build_resnet_extractor()
    img_filenames  = df["Image Index"].tolist()
    img_feats      = extract_image_features(img_filenames, extractor, IMAGE_DIRS, BATCH_CNN)
    print(f"  Image features shape: {img_feats.shape}")

    # 7. Fusion vector
    print("\nGhép 3 modality (concat fusion)...")
    X = fuse_features(img_feats, bert_feats, ehr_feats)
    print(f"  Fusion vector shape: {X.shape}  (= {N_IMAGE} + {N_TEXT} + {N_EHR})")

    # 8. Chia tập train / val / test / calibration
    idx = np.arange(len(X))
    idx_trainval, idx_test   = train_test_split(idx, test_size=0.10, random_state=42)
    idx_train,    idx_calval = train_test_split(idx_trainval, test_size=0.20, random_state=42)
    idx_val,      idx_cal    = train_test_split(idx_calval, test_size=0.50, random_state=42)

    X_train, y_train = X[idx_train], labels[idx_train]
    X_val,   y_val   = X[idx_val],   labels[idx_val]
    X_cal,   y_cal   = X[idx_cal],   labels[idx_cal]
    X_test,  y_test  = X[idx_test],  labels[idx_test]
    print(f"\nTập dữ liệu: Train={len(X_train)} | Val={len(X_val)} | Cal={len(X_cal)} | Test={len(X_test)}")

    # 9. Huấn luyện XGBoost
    xgb_models = train_xgboost_per_class(X_train, y_train, X_val, y_val)

    # 10. Huấn luyện MLP
    mlp_model = train_mlp(X_train, y_train, X_val, y_val)

    # 11. Calibrate probabilities
    calibrators = calibrate_ensemble(xgb_models, mlp_model, X_cal, y_cal)

    # 12. Clinical evaluation
    evaluate_clinical(xgb_models, mlp_model, calibrators, X_test, y_test)

    # 13. Lưu artifact
    save_all_artifacts(extractor, xgb_models, calibrators, BERT_MODEL)

    print("\n✅ Huấn luyện hoàn tất!")
    print("Tải về các file: xgb_models.pkl, mlp_model.keras, calibrators.pkl,")
    print("                 ehr_scaler.pkl, resnet50_extractor.keras, model_meta.pkl")
    print("Đặt vào thư mục: AI_Server/models/")
