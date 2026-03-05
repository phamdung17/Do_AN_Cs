"""
Tiền xử lý ảnh X-quang: resize, chuẩn hóa, chuyển kênh màu.
"""

import cv2
import numpy as np
from PIL import Image
import io


IMG_SIZE_YOLO   = 640   # YOLOv8 inference size
IMG_SIZE_UNET   = 256   # U-Net input size
IMG_SIZE_FUSION = 224   # ResNet50 input size


def bytes_to_cv2(data: bytes) -> np.ndarray:
    """Chuyển bytes nhận từ HTTP multipart upload sang mảng OpenCV BGR."""
    arr = np.frombuffer(data, np.uint8)
    img = cv2.imdecode(arr, cv2.IMREAD_GRAYSCALE)
    if img is None:
        raise ValueError("Không thể giải mã ảnh – kiểm tra định dạng file (PNG/JPG/DICOM).")
    return img


def preprocess_for_yolo(gray: np.ndarray) -> np.ndarray:
    """Trả về ảnh RGB uint8 kích thước (H, W, 3) cho YOLOv8."""
    img = cv2.resize(gray, (IMG_SIZE_YOLO, IMG_SIZE_YOLO))
    return cv2.cvtColor(img, cv2.COLOR_GRAY2RGB)


def preprocess_for_unet(gray: np.ndarray) -> np.ndarray:
    """Trả về tensor float32 shape (1, 256, 256, 3) cho U-Net."""
    img = cv2.resize(gray, (IMG_SIZE_UNET, IMG_SIZE_UNET))
    img = cv2.equalizeHist(img)                              # Tăng tương phản
    img = np.stack([img] * 3, axis=-1).astype(np.float32) / 255.0
    return np.expand_dims(img, axis=0)                       # (1, 256, 256, 3)


def preprocess_for_fusion(gray: np.ndarray) -> np.ndarray:
    """Trả về tensor float32 shape (1, 224, 224, 3) cho ResNet50."""
    img = cv2.resize(gray, (IMG_SIZE_FUSION, IMG_SIZE_FUSION))
    img = cv2.equalizeHist(img)
    img = np.stack([img] * 3, axis=-1).astype(np.float32) / 255.0
    return np.expand_dims(img, axis=0)                       # (1, 224, 224, 3)


def ndarray_to_png_bytes(img: np.ndarray) -> bytes:
    """Chuyển mảng numpy (H, W, 3) uint8 thành bytes PNG để gửi qua HTTP."""
    _, buf = cv2.imencode(".png", img)
    return buf.tobytes()


def overlay_mask(original_gray: np.ndarray, mask: np.ndarray, alpha: float = 0.5) -> np.ndarray:
    """
    Tô màu đỏ vùng segmentation lên ảnh X-quang gốc.
    Args:
        original_gray: ảnh xám gốc (H, W)
        mask:          binary mask float32 (H, W) – giá trị 0/1
        alpha:         độ trong suốt
    Returns:
        ảnh RGB uint8
    """
    h, w = original_gray.shape[:2]
    mask_resized = cv2.resize(mask, (w, h))
    mask_bin     = (mask_resized > 0.5).astype(np.uint8)

    rgb = cv2.cvtColor(original_gray, cv2.COLOR_GRAY2RGB)
    overlay = rgb.copy()
    overlay[mask_bin == 1] = [255, 50, 50]                  # Tô màu đỏ

    blended = cv2.addWeighted(overlay, alpha, rgb, 1 - alpha, 0)
    return blended
