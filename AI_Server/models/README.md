# Thư mục chứa các file model đã huấn luyện

Sau khi train xong trên Kaggle, tải **7 file** sau về đây:

| File | Script tạo ra | Kích thước ước tính |
|------|--------------|---------------------|
| `best.pt` | `train_yolo.py` | ~6 MB |
| `unet_model.h5` | `train_unet.py` | ~120 MB |
| `resnet50_extractor.keras` | `train_multimodal_fusion.py` (bước 5) | ~90 MB |
| `mlp_model.keras` | `train_multimodal_fusion.py` (bước 8) | ~5 MB |
| `xgb_models.pkl` | `train_multimodal_fusion.py` (bước 7) | ~50 MB |
| `calibrators.pkl` | `train_multimodal_fusion.py` (bước 9) | ~1 MB |
| `ehr_scaler.pkl` | `train_multimodal_fusion.py` (bước 2) | ~1 KB |

> **Lưu ý**: Tất cả file model được liệt kê trong `.gitignore` – không commit lên Git.
