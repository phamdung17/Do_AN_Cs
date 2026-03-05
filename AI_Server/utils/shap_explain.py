"""
SHAP Explanation Utilities
===========================
Tạo SHAP values để giải thích đóng góp của từng đặc trưng (EHR + text) 
đối với kết quả chẩn đoán của XGBoost ensemble.

Tài liệu tham khảo:
  Lundberg & Lee (2017), "A Unified Approach to Interpreting Model Predictions"
"""

import numpy as np
import shap
import pickle
from typing import List, Dict, Tuple
from dataclasses import dataclass


# ─── TÊN ĐẶC TRƯNG ────────────────────────────────────────────────────────────
EHR_FEATURE_NAMES = [
    "Tuổi (Age)",
    "Giới tính (Gender)",
    "Hướng chụp (View PA)",
    "Hút thuốc (Smoking)",
    "Tiền sử bệnh (History)",
    "Nhịp tim (HR)",
    "SpO₂ (%)",
    "Nhiệt độ (Temp)",
    "Huyết áp tâm thu (SBP)",
    "Nhịp thở (RR)",
    "Bạch cầu (WBC)",
    "CRP",
    "Lactate",
]


@dataclass
class ShapResult:
    """Kết quả SHAP cho 1 bệnh cụ thể."""
    disease: str
    icd_code: str
    probability: float
    calibrated_probability: float
    feature_contributions: List[Dict]     # [{"name": str, "value": float, "shap": float}]
    textual_explanation: str
    top_positive_features: List[str]      # Đặc trưng đẩy xác suất LÊN
    top_negative_features: List[str]      # Đặc trưng đẩy xác suất XUỐNG


def compute_shap_ehr(
    xgb_models: List,
    X_fusion: np.ndarray,          # shape (1, N_IMAGE+N_TEXT+N_EHR)
    ehr_raw: np.ndarray,           # shape (1, 13) — raw EHR values (chưa scale)
    n_image: int = 2048,
    n_text: int = 768,
    n_ehr: int = 13,
    disease_index: int = 6,        # mặc định: Pneumonia
    disease_name: str = "Pneumonia",
    icd_code: str = "J18.9",
    calibrated_prob: float = 0.0,
) -> ShapResult:
    """
    Tính SHAP values cho đặc trưng EHR + sử dụng TreeExplainer trên XGBoost.
    
    Do feature vector quá lớn (2048+768+13=2829 dim), chỉ dùng phần EHR (13 features)
    để giải thích với TreeExplainer (nhanh, chính xác với tree-based models).
    Image/Text contributions được tóm tắt dưới dạng aggregate.
    """
    clf = xgb_models[disease_index]

    # Tách phần EHR từ fusion vector
    ehr_start = n_image + n_text
    X_ehr_only = X_fusion[:, ehr_start:]  # (1, 13)

    # ─── SHAP TreeExplainer trên EHR ──────────────────────────────────────────
    # Tạo lightweight XGB model chỉ dùng EHR cho SHAP (để đảm bảo dim matching)
    # Full model dùng cả 2829 features; SHAP approximation dùng EHR portion
    try:
        explainer = shap.TreeExplainer(clf)
        shap_vals = explainer.shap_values(X_fusion)  # (1, 2829) hoặc (1,)
        
        # Lấy SHAP values cho phần EHR
        if shap_vals.ndim == 2:
            ehr_shap = shap_vals[0, ehr_start:]  # 13 values
        else:
            ehr_shap = shap_vals[ehr_start:]
    except Exception:
        # Fallback: dùng Linear SHAP nếu TreeExplainer không tương thích
        explainer = shap.LinearExplainer(clf, X_fusion, feature_perturbation="correlation_dependent")
        shap_vals = explainer.shap_values(X_fusion)
        ehr_shap  = shap_vals[0, ehr_start:] if shap_vals.ndim == 2 else shap_vals[ehr_start:]

    # Chuẩn hóa giá trị EHR raw về dạng đọc được
    ehr_decoded = decode_ehr(ehr_raw[0])

    # ─── Xây dựng danh sách contributions ────────────────────────────────────
    contributions = []
    for i, (fname, shap_v) in enumerate(zip(EHR_FEATURE_NAMES, ehr_shap)):
        contributions.append({
            "name":  fname,
            "value": ehr_decoded.get(fname, float(ehr_raw[0, i])),
            "shap":  round(float(shap_v), 5),
        })

    # Tính aggregate contribution từ Image + Text
    img_shap_sum  = float(np.sum(shap_vals[0, :n_image])) if shap_vals.ndim == 2 else 0.0
    text_shap_sum = float(np.sum(shap_vals[0, n_image:n_image+n_text])) if shap_vals.ndim == 2 else 0.0

    # Thêm image và text aggregate
    contributions.append({"name": "Ảnh X-quang (Image)",        "value": None, "shap": round(img_shap_sum, 5)})
    contributions.append({"name": "Ghi chú lâm sàng (Text)",    "value": None, "shap": round(text_shap_sum, 5)})

    # Sắp xếp theo |SHAP|
    contributions_sorted = sorted(contributions, key=lambda x: abs(x["shap"]), reverse=True)

    top_pos = [c["name"] for c in contributions_sorted if c["shap"] > 0][:3]
    top_neg = [c["name"] for c in contributions_sorted if c["shap"] < 0][:3]

    # ─── Sinh giải thích dạng văn bản ────────────────────────────────────────
    explanation = generate_textual_rationale(
        disease_name, icd_code, calibrated_prob, contributions_sorted,
        top_pos, top_neg, img_shap_sum, text_shap_sum
    )

    return ShapResult(
        disease                = disease_name,
        icd_code               = icd_code,
        probability            = calibrated_prob,
        calibrated_probability = calibrated_prob,
        feature_contributions  = contributions_sorted,
        textual_explanation    = explanation,
        top_positive_features  = top_pos,
        top_negative_features  = top_neg,
    )


def decode_ehr(ehr_raw: np.ndarray) -> dict:
    """Chuyển EHR normalized [0-1] về giá trị lâm sàng đọc được."""
    return {
        "Tuổi (Age)":              f"{int(ehr_raw[0] * 100)} tuổi",
        "Giới tính (Gender)":      "Nam" if ehr_raw[1] > 0.5 else "Nữ",
        "Hướng chụp (View PA)":    "PA" if ehr_raw[2] > 0.5 else "AP",
        "Hút thuốc (Smoking)":     "Có" if ehr_raw[3] > 0.5 else "Không",
        "Tiền sử bệnh (History)":  "Có" if ehr_raw[4] > 0.5 else "Không",
        "Nhịp tim (HR)":           f"{int(ehr_raw[5] * 120)} bpm",
        "SpO₂ (%)":                f"{int(ehr_raw[6] * 12 + 88)}%",
        "Nhiệt độ (Temp)":         f"{round(ehr_raw[7] * 4 + 36, 1)}°C",
        "Huyết áp tâm thu (SBP)":  f"{int(ehr_raw[8] * 70 + 90)} mmHg",
        "Nhịp thở (RR)":           f"{int(ehr_raw[9] * 18 + 12)} /min",
        "Bạch cầu (WBC)":          f"{round(ehr_raw[10] * 11 + 4, 1)} K/μL",
        "CRP":                     f"{round(ehr_raw[11] * 100, 1)} mg/L",
        "Lactate":                 f"{round(ehr_raw[12] * 4, 2)} mmol/L",
    }


def generate_textual_rationale(
    disease: str,
    icd_code: str,
    prob: float,
    contributions: list,
    top_pos: List[str],
    top_neg: List[str],
    img_shap: float,
    text_shap: float,
) -> str:
    """
    Sinh giải thích dạng văn bản ngắn gọn cho bác sĩ.
    """
    pct = round(prob * 100, 1)
    dominant = "hình ảnh X-quang" if abs(img_shap) > abs(text_shap) else "ghi chú lâm sàng"

    pos_str = ", ".join(top_pos[:2]) if top_pos else "không có"
    neg_str = ", ".join(top_neg[:2]) if top_neg else "không có"

    rationale = (
        f"Mô hình AI đánh giá xác suất mắc {disease} (ICD: {icd_code}) là {pct}%. "
        f"Đóng góp chủ yếu đến từ {dominant}. "
        f"Các yếu tố làm TĂNG nguy cơ: {pos_str}. "
        f"Các yếu tố làm GIẢM nguy cơ: {neg_str}. "
        f"Kết quả này chỉ mang tính hỗ trợ – bác sĩ cần xem xét toàn diện lâm sàng."
    )
    return rationale


def batch_shap_top_disease(
    xgb_models: List,
    calibrators: List,
    X_fusion: np.ndarray,
    ehr_raw: np.ndarray,
    disease_names: List[str],
    icd_map: Dict[str, str],
    n_image: int = 2048,
    n_text: int = 768,
    top_k: int = 3,
) -> List[ShapResult]:
    """
    Tính SHAP cho top-K bệnh có xác suất cao nhất.
    Trả về danh sách ShapResult.
    """
    # Lấy raw probs
    xgb_probs = np.array([
        clf.predict_proba(X_fusion)[:, 1][0] for clf in xgb_models
    ])
    cal_probs = np.array([
        cal.predict([xgb_probs[c]])[0] for c, cal in enumerate(calibrators)
    ])

    top_indices = np.argsort(cal_probs)[::-1][:top_k]
    results = []
    for idx in top_indices:
        result = compute_shap_ehr(
            xgb_models   = xgb_models,
            X_fusion     = X_fusion,
            ehr_raw      = ehr_raw,
            n_image      = n_image,
            n_text       = n_text,
            disease_index = int(idx),
            disease_name = disease_names[idx],
            icd_code     = icd_map.get(disease_names[idx], "N/A"),
            calibrated_prob = float(cal_probs[idx]),
        )
        results.append(result)
    return results
