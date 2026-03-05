"""
GIAI ĐOẠN 1 - NHÁNH 3: Huấn luyện Multimodal Fusion (ResNet50 + MLP)
Dataset: ChestX-ray14 (NIH) + dữ liệu lâm sàng tổng hợp
Mục tiêu: Kết hợp ảnh X-quang + EHR (tuổi, giới tính, hút thuốc...)
          để phân loại 14 bệnh phổi.
Sản phẩm: fusion_model.h5

Chạy trên Kaggle Notebook với GPU P100 (miễn phí)
"""

import os
import cv2
import numpy as np
import pandas as pd
import tensorflow as tf
from sklearn.model_selection import train_test_split
from sklearn.preprocessing import StandardScaler
from sklearn.utils.class_weight import compute_sample_weight

# ─── CẤU HÌNH ────────────────────────────────────────────────────────────────
DATA_DIR   = "/kaggle/input/data"               # ChestX-ray14
IMAGE_DIR  = os.path.join(DATA_DIR, "images")
LABEL_CSV  = os.path.join(DATA_DIR, "Data_Entry_2017.csv")
IMG_SIZE   = 224
BATCH      = 32
EPOCHS     = 30
LR         = 1e-4

DISEASES = [
    "Atelectasis", "Cardiomegaly", "Effusion", "Infiltration", "Mass",
    "Nodule", "Pneumonia", "Pneumothorax", "Consolidation", "Edema",
    "Emphysema", "Fibrosis", "Pleural_Thickening", "Hernia"
]
N_CLASSES = len(DISEASES)


# ─── BƯỚC 1: XỬ LÝ NHÃN ─────────────────────────────────────────────────────
def process_labels(df: pd.DataFrame) -> np.ndarray:
    """Chuyển chuỗi nhãn 'Atelectasis|Effusion' thành vector multi-hot."""
    label_matrix = np.zeros((len(df), N_CLASSES), dtype=np.float32)
    for i, row in df.iterrows():
        for disease in str(row["Finding Labels"]).split("|"):
            if disease in DISEASES:
                label_matrix[i][DISEASES.index(disease)] = 1.0
    return label_matrix


# ─── BƯỚC 2: TẠO DỮ LIỆU EHR GIẢ LẬP ───────────────────────────────────────
def generate_ehr_features(df: pd.DataFrame) -> np.ndarray:
    """
    Trích xuất các đặc trưng lâm sàng từ metadata ChestX-ray14.
    Trong thực tế sẽ lấy từ bảng bệnh nhân trong SQL Server.
    Features: [tuổi, giới tính(0/1), số lần chụp, vị trí (PA/AP)]
    """
    ehr = pd.DataFrame()
    ehr["age"]     = pd.to_numeric(df["Patient Age"], errors="coerce").fillna(40) / 100.0
    ehr["gender"]  = (df["Patient Gender"] == "M").astype(float)
    ehr["view_pa"] = (df["View Position"] == "PA").astype(float)
    # Đặc trưng giả lập: hút thuốc và tiền sử (sẽ thay bằng dữ liệu thật)
    np.random.seed(42)
    ehr["smoking"]  = np.random.randint(0, 2, len(df)).astype(float)
    ehr["history"]  = np.random.randint(0, 2, len(df)).astype(float)
    return ehr.values.astype(np.float32)


# ─── BƯỚC 3: PIPELINE ĐỌC ẢNH ────────────────────────────────────────────────
def load_image(img_path: str, size: int = IMG_SIZE) -> np.ndarray:
    img = cv2.imread(img_path, cv2.IMREAD_GRAYSCALE)
    if img is None:
        img = np.zeros((size, size), dtype=np.uint8)
    img = cv2.resize(img, (size, size))
    img = cv2.equalizeHist(img)                      # CLAHE tăng tương phản
    img = np.stack([img] * 3, axis=-1).astype(np.float32) / 255.0
    return img


# ─── BƯỚC 4: XÂY DỰNG MÔ HÌNH FUSION ────────────────────────────────────────
def build_fusion_model(ehr_dim: int = 5):
    """
    Image Branch: ResNet50 pretrained (ImageNet) -> GlobalAveragePooling -> Dense(256)
    EHR Branch:   Dense(64) -> Dense(128)
    Fusion:       Concatenate -> Dense(256) -> Dropout -> Dense(N_CLASSES, sigmoid)
    """
    # ── Image Branch ──────────────────────────────────────────────────────────
    base = tf.keras.applications.ResNet50(
        include_top   = False,
        weights       = "imagenet",
        input_shape   = (IMG_SIZE, IMG_SIZE, 3)
    )
    # Fine-tune từ block4 trở đi
    for layer in base.layers:
        layer.trainable = False
    for layer in base.layers[-30:]:
        layer.trainable = True

    img_input = base.input
    x_img = base.output
    x_img = tf.keras.layers.GlobalAveragePooling2D()(x_img)
    x_img = tf.keras.layers.Dense(256, activation="relu")(x_img)
    x_img = tf.keras.layers.BatchNormalization()(x_img)
    x_img = tf.keras.layers.Dropout(0.4)(x_img)

    # ── EHR Branch ────────────────────────────────────────────────────────────
    ehr_input = tf.keras.layers.Input(shape=(ehr_dim,), name="ehr_input")
    x_ehr = tf.keras.layers.Dense(64,  activation="relu")(ehr_input)
    x_ehr = tf.keras.layers.Dense(128, activation="relu")(x_ehr)
    x_ehr = tf.keras.layers.BatchNormalization()(x_ehr)

    # ── Fusion ────────────────────────────────────────────────────────────────
    fused = tf.keras.layers.Concatenate()([x_img, x_ehr])
    fused = tf.keras.layers.Dense(256, activation="relu")(fused)
    fused = tf.keras.layers.Dropout(0.3)(fused)
    output = tf.keras.layers.Dense(N_CLASSES, activation="sigmoid", name="output")(fused)

    model = tf.keras.Model(inputs=[img_input, ehr_input], outputs=output)
    return model


# ─── BƯỚC 5: TRAIN ───────────────────────────────────────────────────────────
def train_model(model, train_data, val_data, epochs: int = EPOCHS):
    model.compile(
        optimizer = tf.keras.optimizers.Adam(LR),
        loss      = tf.keras.losses.BinaryCrossentropy(),
        metrics   = [
            tf.keras.metrics.AUC(name="auc", multi_label=True),
            tf.keras.metrics.BinaryAccuracy(name="acc")
        ]
    )

    callbacks = [
        tf.keras.callbacks.ModelCheckpoint(
            "/kaggle/working/fusion_model.h5",
            monitor="val_auc", mode="max", save_best_only=True, verbose=1
        ),
        tf.keras.callbacks.ReduceLROnPlateau(factor=0.5, patience=4),
        tf.keras.callbacks.EarlyStopping(monitor="val_auc", patience=8, mode="max")
    ]

    (tr_imgs, tr_ehr), tr_labels = train_data
    (val_imgs, val_ehr), val_labels = val_data

    model.fit(
        x               = {"input_1": tr_imgs, "ehr_input": tr_ehr},
        y               = tr_labels,
        validation_data = ({"input_1": val_imgs, "ehr_input": val_ehr}, val_labels),
        batch_size      = BATCH,
        epochs          = epochs,
        callbacks       = callbacks
    )


# ─── MAIN ────────────────────────────────────────────────────────────────────
if __name__ == "__main__":
    print("Đọc metadata...")
    df = pd.read_csv(LABEL_CSV)
    df = df.sample(frac=1, random_state=42).reset_index(drop=True)   # Xáo trộn

    labels   = process_labels(df)
    ehr_feats = generate_ehr_features(df)

    print("Đọc ảnh (có thể mất vài phút)...")
    img_paths = [os.path.join(IMAGE_DIR, fn) for fn in df["Image Index"]]
    images = np.array([load_image(p) for p in img_paths])

    # Chia tập
    idx = np.arange(len(images))
    tr_idx, val_idx = train_test_split(idx, test_size=0.15, random_state=42)

    train_data = (images[tr_idx], ehr_feats[tr_idx]), labels[tr_idx]
    val_data   = (images[val_idx], ehr_feats[val_idx]), labels[val_idx]

    model = build_fusion_model(ehr_dim=ehr_feats.shape[1])
    model.summary()

    train_model(model, train_data, val_data)
    print("\nĐã lưu fusion_model.h5 — tải file này về máy để đưa vào AI_Server/models/")
