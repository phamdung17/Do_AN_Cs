"""
GIAI ĐOẠN 1 - NHÁNH 2: Huấn luyện U-Net cho Segmentation
Dataset: Montgomery County X-ray Set hoặc SIIM-ACR Pneumothorax Segmentation
Mục tiêu: Tô màu (Mask) vùng phổi / vùng tổn thương trên ảnh X-quang
Sản phẩm: unet_model.h5

Chạy trên Kaggle Notebook với GPU P100 (miễn phí)
"""

import os
import cv2
import numpy as np
import tensorflow as tf
from pathlib import Path
from sklearn.model_selection import train_test_split
import segmentation_models as sm

# ─── CẤU HÌNH ────────────────────────────────────────────────────────────────
DATA_DIR   = "/kaggle/input/siim-acr-pneumothorax-segmentation"
IMG_SIZE   = 256          # Resize ảnh về 256x256
BATCH_SIZE = 8
EPOCHS     = 40
LR         = 1e-4
BACKBONE   = "resnet34"   # Backbone cho U-Net encoder

sm.set_framework("tf.keras")
sm.framework()


# ─── BƯỚC 1: ĐỌC DỮ LIỆU ────────────────────────────────────────────────────
def load_paths(data_dir: str):
    """Lấy danh sách đường dẫn ảnh và mask tương ứng."""
    img_paths  = sorted(Path(data_dir).glob("train_images/*.png"))
    mask_paths = sorted(Path(data_dir).glob("train_masks/*.png"))
    assert len(img_paths) == len(mask_paths), "Số ảnh và mask không khớp!"
    print(f"Tổng số mẫu: {len(img_paths)}")
    return img_paths, mask_paths


# ─── BƯỚC 2: TIỀN XỬ LÝ ─────────────────────────────────────────────────────
def preprocess(img_path: str, mask_path: str, size: int = IMG_SIZE):
    """Đọc ảnh xám -> chuyển sang RGB (3 kênh) và chuẩn hóa [0, 1]."""
    img  = cv2.imread(str(img_path), cv2.IMREAD_GRAYSCALE)
    mask = cv2.imread(str(mask_path), cv2.IMREAD_GRAYSCALE)

    img  = cv2.resize(img, (size, size))
    mask = cv2.resize(mask, (size, size))

    # Chuyển ảnh xám -> RGB để dùng với pretrained ResNet
    img  = np.stack([img] * 3, axis=-1).astype(np.float32) / 255.0
    mask = (mask > 127).astype(np.float32)                  # Binary mask [0/1]
    mask = mask[..., np.newaxis]                             # (H, W, 1)
    return img, mask


# ─── BƯỚC 3: TẠO tf.data PIPELINE ───────────────────────────────────────────
def build_dataset(img_paths, mask_paths, batch_size: int):
    imgs  = np.array([preprocess(i, m)[0] for i, m in zip(img_paths, mask_paths)])
    masks = np.array([preprocess(i, m)[1] for i, m in zip(img_paths, mask_paths)])

    dataset = tf.data.Dataset.from_tensor_slices((imgs, masks))
    dataset = dataset.shuffle(512).batch(batch_size).prefetch(tf.data.AUTOTUNE)
    return dataset, imgs, masks


# ─── BƯỚC 4: XÂY DỰNG MÔ HÌNH U-NET ─────────────────────────────────────────
def build_unet(backbone: str = BACKBONE, img_size: int = IMG_SIZE):
    """
    Dùng thư viện segmentation_models để tạo U-Net với encoder pretrained.
    """
    model = sm.Unet(
        backbone_name = backbone,
        input_shape   = (img_size, img_size, 3),
        classes       = 1,
        activation    = "sigmoid",
        encoder_weights = "imagenet"
    )
    return model


# ─── BƯỚC 5: COMPILE & TRAIN ─────────────────────────────────────────────────
def train_model(model, train_ds, val_ds, epochs: int = EPOCHS):
    # Dice Loss + BCE cho Segmentation
    dice_loss    = sm.losses.DiceLoss()
    bce_loss     = tf.keras.losses.BinaryCrossentropy()
    total_loss   = dice_loss + bce_loss

    dice_coef    = sm.metrics.FScore(threshold=0.5, name="dice_coef")
    iou_score    = sm.metrics.IOUScore(threshold=0.5, name="iou_score")

    model.compile(
        optimizer = tf.keras.optimizers.Adam(LR),
        loss      = total_loss,
        metrics   = [dice_coef, iou_score]
    )

    callbacks = [
        tf.keras.callbacks.ModelCheckpoint(
            "/kaggle/working/unet_model.h5",
            monitor="val_dice_coef",
            mode="max",
            save_best_only=True,
            verbose=1
        ),
        tf.keras.callbacks.ReduceLROnPlateau(
            monitor="val_loss", factor=0.5, patience=5, verbose=1
        ),
        tf.keras.callbacks.EarlyStopping(
            monitor="val_dice_coef", patience=10, restore_best_weights=True
        )
    ]

    history = model.fit(
        train_ds,
        validation_data = val_ds,
        epochs          = epochs,
        callbacks       = callbacks
    )
    return history


# ─── BƯỚC 6: ĐÁNH GIÁ (Dice Coefficient) ────────────────────────────────────
def evaluate_model(model, val_ds):
    results = model.evaluate(val_ds, verbose=1)
    print(f"\n=== KẾT QUẢ ĐÁNH GIÁ ===")
    for name, val in zip(model.metrics_names, results):
        print(f"  {name}: {val:.4f}")


# ─── MAIN ────────────────────────────────────────────────────────────────────
if __name__ == "__main__":
    img_paths, mask_paths = load_paths(DATA_DIR)

    # Chia train / val
    tr_imgs, val_imgs, tr_masks, val_masks = train_test_split(
        img_paths, mask_paths, test_size=0.15, random_state=42
    )

    print(f"Train: {len(tr_imgs)} | Val: {len(val_imgs)}")

    # Build dataset
    train_ds, _, _   = build_dataset(tr_imgs, tr_masks, BATCH_SIZE)
    val_ds, _, _     = build_dataset(val_imgs, val_masks, BATCH_SIZE)

    # Xây dựng và huấn luyện
    model   = build_unet()
    history = train_model(model, train_ds, val_ds)
    evaluate_model(model, val_ds)

    print("\nĐã lưu unet_model.h5 — tải file này về máy để đưa vào AI_Server/models/")
