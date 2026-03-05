"""
GIAI ĐOẠN 1 - NHÁNH 1: Huấn luyện YOLOv8 cho Detection
Dataset: RSNA Pneumonia Detection Challenge
Mục tiêu: Khoanh vùng (Bounding Box) các khối mờ / nốt trong ảnh X-quang
Sản phẩm: best.pt

Chạy trên Kaggle Notebook với GPU P100 (miễn phí)
"""

import os
import yaml
import shutil
import numpy as np
import pandas as pd
from pathlib import Path
from sklearn.model_selection import train_test_split
from ultralytics import YOLO

# ─── CẤU HÌNH ────────────────────────────────────────────────────────────────
DATA_DIR = "/kaggle/input/competitions/rsna-pneumonia-detection-challenge"
WORK_DIR = "/kaggle/working/yolo_dataset"
EPOCHS   = 50
IMG_SIZE = 640
BATCH    = 16
MODEL_BASE = "yolov8n.pt"       # Dùng nano để nhanh; chuyển sang yolov8m.pt nếu GPU đủ mạnh


# ─── BƯỚC 1: ĐỌC VÀ LỌC NHÃN ────────────────────────────────────────────────
def load_rsna_labels(data_dir: str) -> pd.DataFrame:
    """Đọc file CSV nhãn của RSNA, chỉ giữ ảnh có box (Target == 1)."""
    df = pd.read_csv(os.path.join(data_dir, "stage_2_train_labels.csv"))
    df = df[df["Target"] == 1].copy()
    df.dropna(subset=["x", "y", "width", "height"], inplace=True)
    print(f"Tổng số box hợp lệ: {len(df)}")
    return df


# ─── BƯỚC 2: CHUYỂN ĐỊNH DẠNG SANG YOLO ─────────────────────────────────────
def convert_to_yolo_format(row, img_w: int = 1024, img_h: int = 1024):
    """
    RSNA: x, y là top-left; YOLO cần (cx, cy, w, h) normalized [0..1]
    Class index = 0 (chỉ có 1 class: pneumonia)
    """
    x, y, w, h = row["x"], row["y"], row["width"], row["height"]
    cx = (x + w / 2) / img_w
    cy = (y + h / 2) / img_h
    nw = w / img_w
    nh = h / img_h
    return f"0 {cx:.6f} {cy:.6f} {nw:.6f} {nh:.6f}"


# ─── BƯỚC 3: TẠO THƯ MỤC DATASET ────────────────────────────────────────────
def build_dataset(df: pd.DataFrame, data_dir: str, work_dir: str):
    images_src = os.path.join(data_dir, "stage_2_train_images")
    patient_ids = df["patientId"].unique()

    train_ids, val_ids = train_test_split(patient_ids, test_size=0.15, random_state=42)

    for split, ids in [("train", train_ids), ("val", val_ids)]:
        img_dst = os.path.join(work_dir, "images", split)
        lbl_dst = os.path.join(work_dir, "labels", split)
        os.makedirs(img_dst, exist_ok=True)
        os.makedirs(lbl_dst, exist_ok=True)

        for pid in ids:
            # Sao chép ảnh (dcm -> cần chuyển sang png nếu chưa)
            src_img = os.path.join(images_src, f"{pid}.dcm")
            if not os.path.exists(src_img):
                continue

            # Nếu RSNA đã cung cấp PNG thì dùng trực tiếp
            src_png = os.path.join(images_src, f"{pid}.png")
            dst_img = os.path.join(img_dst, f"{pid}.png")
            if os.path.exists(src_png):
                shutil.copy(src_png, dst_img)

            # Tạo file label
            rows = df[df["patientId"] == pid]
            lines = [convert_to_yolo_format(r) for _, r in rows.iterrows()]
            with open(os.path.join(lbl_dst, f"{pid}.txt"), "w") as f:
                f.write("\n".join(lines))

    print("Dataset đã tạo xong.")


# ─── BƯỚC 4: VIẾT FILE data.yaml ─────────────────────────────────────────────
def write_data_yaml(work_dir: str) -> str:
    yaml_content = {
        "path": work_dir,
        "train": "images/train",
        "val":   "images/val",
        "nc": 1,
        "names": ["pneumonia"]
    }
    yaml_path = os.path.join(work_dir, "data.yaml")
    with open(yaml_path, "w") as f:
        yaml.dump(yaml_content, f, allow_unicode=True)
    return yaml_path


# ─── BƯỚC 5: TRAIN ───────────────────────────────────────────────────────────
def train(yaml_path: str):
    model = YOLO(MODEL_BASE)
    results = model.train(
        data    = yaml_path,
        epochs  = EPOCHS,
        imgsz   = IMG_SIZE,
        batch   = BATCH,
        name    = "chest_yolo",
        project = "/kaggle/working/runs",
        device  = 0,            # GPU
        augment = True,
        patience= 10,           # Early stopping
    )
    print("Training hoàn tất!")
    best_model = "/kaggle/working/runs/chest_yolo/weights/best.pt"
    print(f"Model tốt nhất: {best_model}")
    return best_model


# ─── BƯỚC 6: ĐÁNH GIÁ (mAP) ─────────────────────────────────────────────────
def evaluate(best_pt: str, yaml_path: str):
    model = YOLO(best_pt)
    metrics = model.val(data=yaml_path)
    print(f"mAP@0.5     : {metrics.box.map50:.4f}")
    print(f"mAP@0.5:0.95: {metrics.box.map:.4f}")
    return metrics


# ─── MAIN ────────────────────────────────────────────────────────────────────
if __name__ == "__main__":
    df = load_rsna_labels(DATA_DIR)
    build_dataset(df, DATA_DIR, WORK_DIR)
    yaml_path = write_data_yaml(WORK_DIR)
    best_pt   = train(yaml_path)
    evaluate(best_pt, yaml_path)

    # Tải model về để dùng trong AI Server
    shutil.copy(best_pt, "/kaggle/working/best.pt")
    print("Đã lưu best.pt — tải file này về máy để đưa vào AI_Server/models/")
