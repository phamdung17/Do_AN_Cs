"""
Grad-CAM – tạo Heatmap giải thích (XAI) cho mô hình ResNet50 trong fusion model.
Dùng thư viện tf-keras-vis (tf_keras_vis).
"""

import cv2
import numpy as np
import tensorflow as tf
from tf_keras_vis.gradcam_plus_plus import GradcamPlusPlus
from tf_keras_vis.utils.model_modifiers import ReplaceToLinear
from tf_keras_vis.utils.scores import CategoricalScore


def make_gradcam_heatmap(
    fusion_model: tf.keras.Model,
    img_tensor: np.ndarray,           # shape (1, 224, 224, 3)
    ehr_tensor: np.ndarray,           # shape (1, n_features)
    class_index: int,
    original_gray: np.ndarray         # ảnh gốc xám (H, W)
) -> np.ndarray:
    """
    Tạo ảnh Heatmap Grad-CAM++ và overlay lên ảnh X-quang gốc.
    Trả về ảnh RGB uint8 (H, W, 3).
    """
    # Tạo sub-model chỉ gồm image branch + output (để Grad-CAM chạy)
    img_input   = fusion_model.get_layer("input_1").output
    penultimate  = fusion_model.get_layer("conv5_block3_out").output    # Layer cuối ResNet50
    sub_model    = tf.keras.Model(inputs=fusion_model.input, outputs=penultimate)

    gradcam = GradcamPlusPlus(
        fusion_model,
        model_modifier=ReplaceToLinear(),
        clone=True
    )
    score = CategoricalScore([class_index])

    # Grad-CAM cần input đúng định dạng model
    cam = gradcam(
        score,
        [img_tensor, ehr_tensor],
        penultimate_layer=-1
    )                     # shape (1, H', W')

    # Chuyển heatmap thành ảnh màu
    heatmap = cam[0]
    heatmap = cv2.resize(heatmap, (original_gray.shape[1], original_gray.shape[0]))
    heatmap = (heatmap * 255).astype(np.uint8)
    heatmap_color = cv2.applyColorMap(heatmap, cv2.COLORMAP_JET)

    # Overlay lên ảnh gốc
    base_rgb = cv2.cvtColor(original_gray, cv2.COLOR_GRAY2RGB)
    superimposed = cv2.addWeighted(base_rgb, 0.55, heatmap_color, 0.45, 0)
    return superimposed


def encode_image_to_base64(img: np.ndarray) -> str:
    """Encode ảnh OpenCV (H,W,3) sang base64 string để nhúng vào JSON."""
    import base64
    _, buf = cv2.imencode(".png", img)
    return base64.b64encode(buf.tobytes()).decode("utf-8")
