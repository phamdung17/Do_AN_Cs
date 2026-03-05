# ChestAI – Hệ Thống Chẩn Đoán X-Quang Ngực Đa Phương Thức

> **Stack**: Bio_ClinicalBERT · ResNet50 · XGBoost · MLP · FastAPI · WinForms .NET 8 · SQL Server  
> **Giao diện**: Windows Forms  
> **Mô hình**: 14 bệnh lý ngực · Grad-CAM · SHAP · Isotonic Calibration

---

## Mục Lục

1. [Kiến Trúc Tổng Quan](#1-kiến-trúc-tổng-quan)
2. [Cấu Trúc Thư Mục](#2-cấu-trúc-thư-mục)
3. [Chi Tiết Từng File](#3-chi-tiết-từng-file)
   - [AI_Models/](#ai_models)
   - [AI_Server/](#ai_server)
   - [WinForms_App/](#winforms_app)
   - [Database/](#database)
4. [Luồng Dữ Liệu](#4-luồng-dữ-liệu)
5. [Cài Đặt & Chạy](#5-cài-đặt--chạy)
6. [Huấn Luyện Mô Hình trên Kaggle](#6-huấn-luyện-mô-hình-trên-kaggle)
7. [Biến Môi Trường](#7-biến-môi-trường)
8. [Đánh Giá Mô Hình](#8-đánh-giá-mô-hình)

---

## 1. Kiến Trúc Tổng Quan

```
Ảnh X-Quang ──► ResNet50 (2048-dim)  ──┐
Ghi Chú Lâm Sàng ──► ClinicalBERT (768-dim) ──┼──► Concat (2829-dim) ──► XGBoost×14 ──► Isotonic Calibration ──► 14 nhãn bệnh lý
EHR Vitals (13 features) ─────────────┘                                   └──► MLP (4 lớp) ─────────────────────────►
```

| Phương Thức | Mô Hình | Chiều Vector | Vai Trò |
|-------------|---------|-------------|---------|
| Ảnh X-Quang | ResNet50 (ImageNet) | 2048 | Đặc trưng hình ảnh |
| Ghi Chú | Bio_ClinicalBERT | 768 | Đặc trưng văn bản lâm sàng |
| EHR Vitals | Scaler + concat | 13 | Tuổi, giới, dấu hiệu sinh tồn |
| **Fusion** | XGBoost + MLP | 2829 → 14 | Phân loại đa nhãn |
| Calibration | Isotonic Regression | – | Hiệu chỉnh xác suất |

---

## 2. Cấu Trúc Thư Mục

```
Do_An_Cs/
├── README.md                         # Tài liệu dự án (file này)
│
├── AI_Models/                        # Scripts huấn luyện mô hình trên Kaggle/Colab
│   ├── requirements_training.txt     # Thư viện cần cho quá trình huấn luyện
│   ├── train_yolo.py                 # Huấn luyện YOLO v8 phát hiện phổi / phân đoạn thô
│   ├── train_unet.py                 # Huấn luyện U-Net phân đoạn phổi pixel-level
│   ├── train_fusion.py               # Huấn luyện pipeline fusion đơn giản (thử nghiệm)
│   └── train_multimodal_fusion.py    # Pipeline chính: ResNet50 + BERT + EHR → XGBoost + MLP
│
├── AI_Server/                        # FastAPI server phục vụ suy luận thời gian thực
│   ├── main.py                       # Entry point – 5 endpoints: /predict /segment /explain /shap /health
│   ├── requirements.txt              # Thư viện runtime cho server
│   ├── models/
│   │   └── README.md                 # Danh sách 7 file mô hình cần đặt vào đây
│   └── utils/
│       ├── __init__.py               # Export tiện ích chung
│       ├── preprocess.py             # Tiền xử lý ảnh, DICOM, EHR
│       ├── gradcam.py                # Grad-CAM trực quan hóa vùng chú ý
│       └── shap_explain.py           # SHAP giải thích đặc trưng quan trọng
│
├── WinForms_App/
│   └── ChestAI/
│       ├── Program.cs                # Entry point WinForms
│       ├── ChestAI.csproj            # Project file (.NET 8, 4 NuGet packages)
│       ├── Forms/
│       │   ├── LoginForm.cs/.Designer.cs   # Form đăng nhập BCrypt
│       │   ├── MainForm.cs/.Designer.cs    # Dashboard – menu điều hướng
│       │   ├── PatientForm.cs              # Quản lý hồ sơ bệnh nhân
│       │   └── DiagnosisForm.cs            # Form chính: upload ảnh + AI diagnosis
│       ├── Models/
│       │   ├── Patient.cs            # DTO hồ sơ bệnh nhân
│       │   └── Diagnosis.cs          # DTO kết quả chẩn đoán
│       ├── Services/
│       │   ├── ApiService.cs         # HTTP client gọi AI Server
│       │   └── DatabaseService.cs    # ADO.NET – tất cả truy vấn SQL Server
│       └── Resources/                # Ảnh, icon nhúng vào app
│
└── Database/
    ├── schema.sql                    # Tạo toàn bộ DB từ đầu (6 bảng + 3 stored procedures)
    └── migrations/
        └── 001_add_TopIcdCode.sql    # Migration thêm cột TopIcdCode vào Diagnoses
```

---

## 3. Chi Tiết Từng File

### AI_Models/

---

#### `requirements_training.txt`

Danh sách thư viện cần cài trong môi trường Kaggle/Colab trước khi huấn luyện:

| Thư Viện | Phiên Bản | Dùng Cho |
|----------|-----------|---------|
| ultralytics | ≥8.0 | Huấn luyện YOLO v8 |
| tensorflow / keras | ≥2.13 | U-Net, ResNet50, MLP |
| transformers | ≥4.35 | Bio_ClinicalBERT tokenizer + model |
| xgboost | ≥2.0 | XGBoost OvR classifiers |
| scikit-learn | ≥1.3 | Scaler, Isotonic Calibration, metrics |
| torch / torchvision | ≥2.0 | BERT inference backend |
| pandas, numpy | latest | Data handling |
| matplotlib, seaborn | latest | Visualization |
| pydicom | ≥2.4 | Đọc file DICOM (nếu có) |

---

#### `train_yolo.py`

**Mục đích**: Huấn luyện YOLO v8 để phát hiện và định vị sơ bộ vùng phổi trên X-Quang ngực.

**Output**: `best.pt` – file weights YOLO (đặt vào `AI_Server/models/`)

**Các bước chính**:

| Bước | Hàm / Lệnh | Mô Tả |
|------|-----------|-------|
| 1 | `YOLO('yolov8n.pt')` | Load pretrained backbone từ Ultralytics |
| 2 | `model.train(data=...)` | Train trên dataset NIH + VinBigData annotations |
| 3 | `model.val()` | Đánh giá mAP50, mAP50-95 trên validation set |
| 4 | export `runs/.../best.pt` | Lưu weights tốt nhất |

---

#### `train_unet.py`

**Mục đích**: Huấn luyện U-Net cho bài toán **phân đoạn phổi pixel-level** (semantic segmentation).

**Output**: `unet_model.h5` – Keras H5 model

**Kiến trúc**:
- Encoder: đường nén xuống (Conv2D + MaxPool × 4)
- Bottleneck: Conv2D 1024 filters
- Decoder: UpSampling + skip connections × 4
- Output: Sigmoid mask (0/1 mỗi pixel)

**Key functions**:

| Hàm | Mô Tả |
|-----|-------|
| `build_unet(input_shape)` | Xây dựng kiến trúc U-Net với skip connections |
| `DataGenerator` | Keras Sequence cung cấp (ảnh, mask) theo batch |
| `dice_loss(y_true, y_pred)` | Hàm mất mát Dice Coefficient |
| `combined_loss` | BCE + Dice Loss kết hợp |
| `train()` | EarlyStopping + ReduceLROnPlateau + ModelCheckpoint |

---

#### `train_fusion.py`

**Mục đích**: Pipeline thử nghiệm – phiên bản đơn giản hơn `train_multimodal_fusion.py`, dùng để kiểm thử nhanh.

**Lưu ý**: File này không tạo ra mô hình production. Mọi huấn luyện production đều chạy qua `train_multimodal_fusion.py`.

---

#### `train_multimodal_fusion.py` ⭐ (File chính)

**Mục đích**: Pipeline huấn luyện **đầy đủ** cho hệ thống đa phương thức. Tạo ra 5 files mô hình production.

**Output files**:
- `resnet50_extractor.keras` – ResNet50 đã bỏ FC layer cuối
- `mlp_model.keras` – MLP 4 lớp phân loại 14 bệnh
- `xgb_models.pkl` – dict 14 XGBoost OvR classifiers
- `calibrators.pkl` – dict 14 Isotonic Regressor
- `ehr_scaler.pkl` – StandardScaler cho 13 EHR features
- `clinical_evaluation.csv` – Bảng metrics AUC/F1/Recall/Precision cho 14 bệnh

**10 bước pipeline**:

| Bước | Hàm | Mô Tả |
|------|-----|-------|
| 1 | `process_labels()` | Đọc Data_Entry_2017.csv, binary multi-label cho 14 bệnh |
| 2 | `generate_ehr_features()` | Sinh dữ liệu EHR tổng hợp (tuổi, giới, SpO₂, HR, v.v.) |
| 3 | `generate_clinical_notes()` | Tạo ghi chú lâm sàng dạng văn bản từ EHR |
| 4 | `extract_bert_embeddings()` | Tokenize + encode với `emilyalsentzer/Bio_ClinicalBERT` → 768-dim |
| 5 | `extract_image_features()` | ResNet50 offline feature extraction → 2048-dim |
| 6 | `fuse_features()` | Concat [image(2048) + bert(768) + ehr(13)] → 2829-dim |
| 7 | `train_xgboost_per_class()` | Train XGBoost cho mỗi trong 14 bệnh (OvR strategy) |
| 8 | `train_mlp()` | Train MLP 4 lớp (2829→512→256→128→14, Dropout 0.3) |
| 9 | `calibrate_ensemble()` | Isotonic Calibration trên validation predictions |
| 10 | `evaluate_clinical()` | Tính AUC, F1, Recall, Precision, lưu CSV |

**14 bệnh được phân loại**:

| # | Tên Bệnh | ICD-10 |
|---|---------|--------|
| 1 | Atelectasis | J98.11 |
| 2 | Cardiomegaly | I51.7 |
| 3 | Effusion | J90 |
| 4 | Infiltration | J18.9 |
| 5 | Mass | R91.8 |
| 6 | Nodule | R91.1 |
| 7 | Pneumonia | J18.9 |
| 8 | Pneumothorax | J93.9 |
| 9 | Consolidation | J18.1 |
| 10 | Edema | J81.0 |
| 11 | Emphysema | J43.9 |
| 12 | Fibrosis | J84.10 |
| 13 | Pleural_Thickening | J92.9 |
| 14 | Hernia | K46.9 |

---

### AI_Server/

---

#### `requirements.txt`

Thư viện runtime cho FastAPI server:

| Thư Viện | Vai Trò |
|----------|---------|
| fastapi, uvicorn | Web framework + ASGI server |
| tensorflow, keras | Load ResNet50 + MLP model |
| transformers, torch | ClinicalBERT tokenizer/model |
| xgboost | Load XGBoost classifiers |
| scikit-learn | Load calibrators + scaler |
| opencv-python | Xử lý ảnh |
| pydicom | Đọc DICOM |
| shap | SHAP explainability |
| pillow, numpy | Tiện ích ảnh và mảng |

---

#### `main.py` ⭐ (Entry point server)

**Mục đích**: FastAPI application với 5 HTTP endpoints phục vụ inference và giải thích AI.

**5 Endpoints**:

| Method | Path | Chức Năng |
|--------|------|-----------|
| GET | `/health` | Kiểm tra trạng thái server + tất cả models đã load |
| POST | `/predict` | Chẩn đoán đầy đủ: ảnh + EHR + ghi chú → 14 nhãn + ICD-10 + giải thích |
| POST | `/segment` | Phân đoạn phổi: ảnh → mask U-Net + bounding box YOLO |
| POST | `/explain` | Grad-CAM: ảnh + tên bệnh → heatmap overlay |
| POST | `/shap` | SHAP: fusion vector → SHAP values 14 bệnh |

**7 hàm nội bộ chính**:

| Hàm | Mô Tả |
|-----|-------|
| `build_ehr_raw(request)` | Tạo numpy array 13 features EHR từ request JSON |
| `encode_clinical_note(note, scaler)` | Tokenize + BERT encode → 768-dim vector |
| `build_fusion_vector(img, ehr, note)` | Concat ResNet50 + BERT + EHR → 2829-dim |
| `run_ensemble_predict(fusion_vec)` | XGBoost × 14 + MLP → weight average → Isotonic Calibration |
| `build_textual_rationale(probs, feats)` | Tạo văn bản giải thích dựa trên top features |
| `apply_lung_mask(img, mask)` | Áp mask phổi lên ảnh gốc |
| `load_all_models()` | Load 7 file mô hình khi startup |

**Request JSON (POST /predict)**:

```json
{
  "image_base64": "...",
  "age": 45, "gender": "M", "view_pa": true,
  "smoking_history": false, "heart_rate": 82,
  "spo2": 97.5, "temperature": 37.2,
  "systolic_bp": 120, "respiratory_rate": 16,
  "wbc": 8.5, "crp": 12.0, "lactate": 1.2,
  "clinical_note": "Patient presents with cough..."
}
```

---

#### `utils/preprocess.py`

**Mục đích**: Tất cả logic tiền xử lý ảnh và dữ liệu trước khi đưa vào mô hình.

| Hàm | Input | Output | Dùng Khi |
|-----|-------|--------|---------|
| `load_image_from_base64(b64)` | Base64 string | numpy array (H,W,3) | Nhận ảnh từ API |
| `load_dicom(path)` | Đường dẫn file .dcm | numpy array (H,W,3) | Mở file DICOM |
| `preprocess_for_resnet(img)` | numpy array | tensor (1,224,224,3) | Feature extraction |
| `preprocess_for_unet(img)` | numpy array | tensor (1,512,512,1) | Phân đoạn phổi |
| `normalize_ehr(raw_ehr, scaler)` | dict EHR + scaler | numpy (1,13) | Chuẩn hóa EHR |
| `apply_clahe(img)` | Ảnh xám | Ảnh tăng contrast | Tiền xử lý DICOM |

---

#### `utils/gradcam.py`

**Mục đích**: Tính Grad-CAM (Gradient-weighted Class Activation Mapping) để trực quan hóa vùng ảnh mà mô hình chú ý.

**Cơ chế**:
1. Forward pass qua ResNet50
2. Hook lấy gradient tại layer `conv5_block3_out`
3. Global Average Pooling các gradient → weights
4. Weighted sum activation maps → heatmap
5. ReLU + normalize → overlay màu lên ảnh gốc

**Lưu ý**: Grad-CAM chỉ hoạt động với ResNet50 feature extractor, không trực tiếp với XGBoost. Heatmap phản ánh vùng ResNet50 chú ý, không phải quyết định cuối cùng của XGBoost.

---

#### `utils/shap_explain.py`

**Mục đích**: Giải thích quyết định của XGBoost và MLP bằng SHAP values.

| Hàm | Mô Tả |
|-----|-------|
| `compute_shap_xgb(fusion_vec, xgb_models)` | SHAP TreeExplainer cho 14 XGBoost classifiers |
| `compute_shap_mlp(fusion_vec, mlp_model)` | SHAP DeepExplainer cho MLP |
| `aggregate_shap(xgb_shap, mlp_shap)` | Kết hợp SHAP XGBoost và MLP theo trọng số |
| `top_features(shap_vals, feature_names, n)` | Trả về top-N features quan trọng nhất |
| `shap_to_dict(shap_vals, names)` | Serialize SHAP values thành dict JSON |

**Feature groups** (2829 features được chia):
- [0:2048] – features hình ảnh từ ResNet50
- [2048:2816] – features văn bản từ ClinicalBERT  
- [2816:2829] – 13 EHR features (có tên đặt)

---

#### `models/README.md`

**Mục đích**: Hướng dẫn đặt 7 file mô hình cần thiết vào thư mục này.

**7 files cần có**:

| File | Nguồn | Kích Thước Ước Tính |
|------|-------|---------------------|
| `best.pt` | Output của `train_yolo.py` | ~6 MB |
| `unet_model.h5` | Output của `train_unet.py` | ~120 MB |
| `resnet50_extractor.keras` | Output của `train_multimodal_fusion.py` (bước 5) | ~90 MB |
| `mlp_model.keras` | Output của `train_multimodal_fusion.py` (bước 8) | ~5 MB |
| `xgb_models.pkl` | Output của `train_multimodal_fusion.py` (bước 7) | ~50 MB |
| `calibrators.pkl` | Output của `train_multimodal_fusion.py` (bước 9) | ~1 MB |
| `ehr_scaler.pkl` | Output của `train_multimodal_fusion.py` (bước 2) | ~1 KB |

---

### WinForms_App/

---

#### `Program.cs`

**Mục đích**: Entry point của ứng dụng WinForms.

```csharp
Application.SetHighDpiMode(HighDpiMode.SystemAware);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.Run(new LoginForm());
```

Khởi động ứng dụng ở `LoginForm`. Toàn bộ điều hướng sau đó do các Form tự quản lý.

---

#### `ChestAI.csproj`

**Mục đích**: Định nghĩa project .NET 8 WinForms.

**Cấu hình**:
- Target Framework: `net8.0-windows`
- UseWindowsForms: `true`

**4 NuGet packages**:

| Package | Phiên Bản | Dùng Cho |
|---------|-----------|---------|
| Microsoft.Data.SqlClient | 5.2.0 | Kết nối SQL Server |
| Newtonsoft.Json | 13.0.3 | Deserialize JSON từ API |
| BCrypt.Net-Next | 4.0.3 | Hash + verify mật khẩu |
| LiveChartsCore.SkiaSharpView.WinForms | 2.0.0-rc2 | Biểu đồ kết quả AI |

---

#### `Forms/LoginForm.cs`

**Mục đích**: Form đăng nhập với xác thực BCrypt.

**Luồng xác thực**:
1. User nhập username + password
2. Gọi `DatabaseService.ValidateLogin(username, password)`
3. DB trả về `(id, bcrypt_hash, name, role)`
4. `BCrypt.Net.BCrypt.Verify(plainPassword, hash)` kiểm tra
5. Nếu đúng → lưu vào `Session.CurrentUser` → mở `MainForm`

**Key controls**: `txtUsername`, `txtPassword`, `btnLogin`, `lblError`

> ⚠️ **Bug đã biết**: Trong `DatabaseService.ValidateLogin()`, có lỗi đọc SqlDataReader sau khi đã `rdr.Close()`. Cần đọc `id, name, role` TRƯỚC khi gọi `rdr.Close()`.

---

#### `Forms/MainForm.cs`

**Mục đích**: Dashboard chính sau đăng nhập – điều hướng toàn bộ ứng dụng.

**Chức năng**:
- Hiển thị thông tin user đăng nhập (`Session.CurrentUser.Name`, `Role`)
- Menu điều hướng: Bệnh Nhân, Chẩn Đoán, Lịch Sử, Cài Đặt
- Sidebar với LiveCharts hiển thị thống kê nhanh (số ca, tỷ lệ bệnh)
- Quản lý phiên đăng nhập + logout

---

#### `Forms/PatientForm.cs`

**Mục đích**: Quản lý hồ sơ bệnh nhân (CRUD).

**Chức năng**:
- Tìm kiếm bệnh nhân theo tên / MRN
- Thêm mới hồ sơ bệnh nhân
- Cập nhật thông tin: tên, ngày sinh, giới tính, liên lạc
- Xem lịch sử chẩn đoán của bệnh nhân
- Gọi `DatabaseService` cho tất cả CRUD operations

---

#### `Forms/DiagnosisForm.cs` ⭐

**Mục đích**: Form chính của ứng dụng – toàn bộ workflow chẩn đoán AI.

**8 Tabs giao diện**:

| Tab | Chức Năng |
|-----|-----------|
| Thông Tin Bệnh Nhân | Chọn/tìm bệnh nhân, nhập EHR vitals |
| Upload Ảnh | Kéo thả hoặc browse file .jpg/.png/.dcm |
| Phân Đoạn Phổi | Hiển thị YOLO bounding box + U-Net mask |
| Kết Quả AI | Bảng 14 bệnh + xác suất + ICD-10 + threshold bars |
| Grad-CAM | Overlay heatmap vùng bệnh lý trên ảnh |
| SHAP | Biểu đồ top features ảnh hưởng đến chẩn đoán |
| Giải Thích | Văn bản tổng hợp giải thích kết quả |
| Lưu / In | Lưu DB + Export PDF/Print report |

**Luồng chẩn đoán**:
```
Upload ảnh → [Segment] → [Predict] → Hiển thị kết quả
     ↓              ↓           ↓
  Preview       mask+bbox    14 labels +
  thumbnail     overlay      probabilities +
                             ICD-10 codes
```

**8 hàm chính**:

| Hàm | Mô Tả |
|-----|-------|
| `btnUpload_Click` | Mở file dialog, load ảnh, hiển thị preview |
| `btnSegment_Click` | Gọi `ApiService.SegmentAsync()`, hiển thị mask |
| `btnDiagnose_Click` | Thu thập EHR + gọi `ApiService.PredictAsync()` |
| `DisplayResults(result)` | Render bảng + LiveCharts bar chart |
| `btnGradCam_Click` | Gọi `/explain`, hiển thị heatmap |
| `btnShap_Click` | Gọi `/shap`, render SHAP waterfall chart |
| `btnSave_Click` | Gọi `DatabaseService.SaveDiagnosis()` |
| `btnExportPdf_Click` | Tạo PDF report bằng iTextSharp |

---

#### `Models/Patient.cs`

**Mục đích**: DTO (Data Transfer Object) cho hồ sơ bệnh nhân.

```csharp
public class Patient {
    public int    PatientId   { get; set; }
    public string MRN         { get; set; }  // Medical Record Number
    public string FullName    { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string Gender      { get; set; }
    public string PhoneNumber { get; set; }
    public string Address     { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

---

#### `Models/Diagnosis.cs`

**Mục đích**: DTO cho kết quả một lần chẩn đoán hoàn chỉnh.

```csharp
public class Diagnosis {
    public int    DiagnosisId  { get; set; }
    public int    PatientId    { get; set; }
    public string ImagePath    { get; set; }
    public string AIResult     { get; set; }  // JSON string 14 labels
    public string TopIcdCode   { get; set; }  // ICD-10 cao nhất
    public float  Confidence   { get; set; }
    public string DoctorNote   { get; set; }
    public DateTime CreatedAt  { get; set; }
    public List<DiagnosisDetail> Details { get; set; }
}
```

---

#### `Services/ApiService.cs`

**Mục đích**: HTTP client wrapper – gọi tất cả endpoints của FastAPI server.

**5 hàm HTTP**:

| Hàm | Endpoint | Trả Về |
|-----|---------|--------|
| `PredictAsync(request)` | POST /predict | `PredictionResult` (14 nhãn + ICD) |
| `SegmentAsync(imageB64)` | POST /segment | `SegmentResult` (mask base64 + bbox) |
| `ExplainAsync(imageB64, diseaseLabel)` | POST /explain | `ExplainResult` (heatmap base64) |
| `ShapAsync(fusionVec)` | POST /shap | `ShapResult` (dict feature → value) |
| `HealthCheckAsync()` | GET /health | `bool` (server sẵn sàng không) |

**Cấu hình**:
- Base URL: đọc từ `appsettings.json` hoặc biến môi trường `AI_SERVER_URL`
- Timeout: 60 giây (inference có thể mất thời gian)
- Serialization: `Newtonsoft.Json`

**Class `EhrVitals`** – DTO gửi kèm request predict:

```csharp
public class EhrVitals {
    public int    Age, public string Gender, public bool ViewPA
    public bool   SmokingHistory, public float HeartRate
    public float  SpO2, public float Temperature
    public float  SystolicBP, public float RespiratoryRate  
    public float  WBC, public float CRP, public float Lactate
    public string ClinicalNote
}
```

---

#### `Services/DatabaseService.cs`

**Mục đích**: Tất cả tương tác với SQL Server qua ADO.NET (không dùng ORM).

**Connection string**: đọc từ `appsettings.json` → key `ChestAIDb`

**Nhóm hàm**:

| Nhóm | Hàm | Mô Tả |
|------|-----|-------|
| **Auth** | `ValidateLogin(user, pass)` | Lấy hash từ DB, verify BCrypt |
| **Patients** | `GetAllPatients()` | Lấy danh sách bệnh nhân |
| | `GetPatientById(id)` | Lấy 1 bệnh nhân |
| | `SearchPatients(keyword)` | Tìm theo tên/MRN |
| | `AddPatient(p)` | Thêm mới |
| | `UpdatePatient(p)` | Cập nhật |
| **XRay** | `SaveXRayImage(path, patientId)` | Lưu đường dẫn ảnh |
| | `GetImagesByPatient(patientId)` | Lấy ảnh của bệnh nhân |
| **Diagnoses** | `SaveDiagnosis(diag)` | Lưu kết quả chẩn đoán |
| | `GetDiagnosesByPatient(id)` | Lấy lịch sử chẩn đoán |
| | `GetDiagnosisDetails(diagId)` | Lấy chi tiết 14 nhãn |

> ⚠️ **Bug cần sửa** trong `ValidateLogin()`:
> ```csharp
> // SAI: đọc sau khi đã Close
> rdr.Close();
> int id = rdr.GetInt32(0);  // ← Lỗi InvalidOperationException!
>
> // ĐÚNG: đọc TRƯỚC khi Close
> int    id   = rdr.GetInt32(0);
> string hash = rdr.GetString(1);
> string name = rdr.GetString(2);
> string role = rdr.GetString(3);
> if (!BCrypt.Net.BCrypt.Verify(password, hash)) return null;
> rdr.Close();
> ```

---

### Database/

---

#### `schema.sql`

**Mục đích**: Tạo toàn bộ database ChestAI từ đầu (chạy một lần khi setup).

**6 Bảng**:

| Bảng | Cột Chính | Mô Tả |
|------|-----------|-------|
| `Users` | UserId, Username, PasswordHash, FullName, Role | Tài khoản đăng nhập |
| `Patients` | PatientId, MRN, FullName, DateOfBirth, Gender | Hồ sơ bệnh nhân |
| `XRayImages` | ImageId, PatientId, FilePath, UploadedAt | Ảnh X-Quang |
| `Diagnoses` | DiagnosisId, PatientId, ImageId, AIResult, TopIcdCode, Confidence | Kết quả chẩn đoán |
| `DiagnosisDetails` | DetailId, DiagnosisId, DiseaseLabel, Probability, IsPositive | Chi tiết 14 nhãn |
| `AuditLog` | LogId, UserId, Action, Timestamp | Nhật ký thao tác |

**3 Stored Procedures**:

| SP | Chức Năng |
|----|-----------|
| `sp_GetPatientSummary` | Thống kê tổng hợp cho 1 bệnh nhân |
| `sp_GetDiagnosisReport` | Báo cáo chi tiết 1 lần chẩn đoán |
| `sp_AuditLog` | Ghi nhật ký thao tác |

> ⚠️ **Lưu ý**: INSERT admin mặc định dùng placeholder hash `'$2a$12$PLACEHOLDER_HASH_REPLACE_THIS'`.  
> Tạo hash thực bằng: `BCrypt.Net.BCrypt.HashPassword("Admin@123")`  
> rồi UPDATE thủ công: `UPDATE Users SET PasswordHash='...' WHERE Username='admin'`

---

#### `migrations/001_add_TopIcdCode.sql`

**Mục đích**: Migration thêm cột `TopIcdCode VARCHAR(20)` vào bảng `Diagnoses`.

**Khi nào chạy**: Sau khi đã chạy `schema.sql` gốc (nếu DB đã tồn tại từ phiên bản cũ chưa có cột này).

```sql
ALTER TABLE Diagnoses
ADD TopIcdCode VARCHAR(20) NULL;
```

**Thứ tự chạy SQL**:
1. `schema.sql` – tạo DB đầy đủ (đã bao gồm `TopIcdCode`)
2. `001_add_TopIcdCode.sql` – **chỉ chạy** nếu DB cũ chưa có cột này

---

## 4. Luồng Dữ Liệu

### Luồng Đăng Nhập

```
LoginForm
    │ nhập username + password
    ▼
DatabaseService.ValidateLogin()
    │ SELECT PasswordHash FROM Users WHERE Username = ?
    ▼
BCrypt.Verify(plain, hash)
    │ true/false
    ▼
Session.CurrentUser = { Id, Name, Role }
    │
    ▼
MainForm.Show()
```

### Luồng Chẩn Đoán AI

```
DiagnosisForm
    ├─[1] Upload ảnh (jpg/png/dcm)
    ├─[2] Nhập EHR vitals (13 fields)
    ├─[3] Nhập Clinical Note (text)
    │
    ▼
ApiService.SegmentAsync(imageBase64)
    │ POST /segment
    ▼
AI_Server/main.py::segment()
    ├── YOLO v8 → bounding boxes
    └── U-Net → lung mask
    │ trả về mask base64 + bbox JSON
    ▼
DiagnosisForm → hiển thị overlay
    │
    ▼
ApiService.PredictAsync(image + EHR + note)
    │ POST /predict
    ▼
AI_Server/main.py::predict()
    ├── preprocess_for_resnet → ResNet50 → 2048-dim
    ├── encode_clinical_note → BERT → 768-dim
    ├── normalize_ehr → 13-dim
    ├── concat → 2829-dim fusion vector
    ├── XGBoost×14 + MLP → weighted avg
    └── Isotonic Calibration → probabilities
    │ trả về 14 labels + ICD-10 + rationale
    ▼
DiagnosisForm.DisplayResults()
    ├── TableView: bệnh + xác suất + ICD-10
    ├── LiveCharts: bar chart probabilities
    └── Text: textual rationale
    │
    ▼
DatabaseService.SaveDiagnosis()
    ├── INSERT INTO Diagnoses (AIResult, TopIcdCode, Confidence...)
    └── INSERT INTO DiagnosisDetails × 14 rows
```

---

## 5. Cài Đặt & Chạy

### Bước 1: Cài Đặt Database

```sql
-- Chạy trong SQL Server Management Studio
-- 1. Tạo DB đầy đủ
:r "d:\DO_AN\Do_An_Cs\Database\schema.sql"

-- 2. Cập nhật hash admin (thay YOUR_BCRYPT_HASH bằng hash thực)
UPDATE Users SET PasswordHash = '$2a$12$YOUR_BCRYPT_HASH' WHERE Username = 'admin';
```

### Bước 2: Cài Đặt AI Server

```powershell
cd "d:\DO_AN\Do_An_Cs\AI_Server"
python -m venv venv
.\venv\Scripts\Activate.ps1
pip install -r requirements.txt

# Đặt 7 file mô hình vào models/
# Xem models/README.md để biết danh sách file cần thiết

# Chạy server
uvicorn main:app --host 0.0.0.0 --port 8000 --reload
```

### Bước 3: Chạy WinForms App

```powershell
cd "d:\DO_AN\Do_An_Cs\WinForms_App\ChestAI"
dotnet restore
dotnet run
```

### Bước 4: Cấu Hình Connection

Tạo `appsettings.json` trong thư mục WinForms_App/ChestAI/:

```json
{
  "ConnectionStrings": {
    "ChestAIDb": "Server=localhost\\SQLEXPRESS;Database=ChestAI;Trusted_Connection=True;"
  },
  "ApiSettings": {
    "BaseUrl": "http://localhost:8000"
  }
}
```

---

## 6. Huấn Luyện Mô Hình trên Kaggle

### Dataset Cần Thiết

```
# Notebook 1 & 2: NIH ChestX-ray14
kaggle datasets download -d nih-chest-xrays/data

# Notebook 1: YOLO annotations (VinBigData)
kaggle competitions download -c vinbigdata-chest-xray-abnormalities-detection

# Notebook 2: Lung segmentation masks
kaggle datasets download -d nikhilpandey360/chest-xray-masks-and-labels
```

### Notebook 1 – YOLO & U-Net (GPU T4 × 1, ~4-6 giờ)

```python
# Tạo Kaggle Notebook, bật GPU
# Upload: train_yolo.py, train_unet.py, requirements_training.txt

!pip install -r requirements_training.txt
!python train_yolo.py     # Output: runs/detect/exp/weights/best.pt
!python train_unet.py     # Output: unet_model.h5
```

**Download outputs**:
```python
from IPython.display import FileLink
FileLink('runs/detect/exp/weights/best.pt')
FileLink('unet_model.h5')
```

### Notebook 2 – Multimodal Fusion (GPU P100, ~8-12 giờ)

```python
# Upload: train_multimodal_fusion.py, requirements_training.txt

!pip install -r requirements_training.txt
# Tải BERT model (lần đầu ~1.5GB)
!python train_multimodal_fusion.py

# 5 output files:
# resnet50_extractor.keras, mlp_model.keras,
# xgb_models.pkl, calibrators.pkl, ehr_scaler.pkl
```

### Notebook 3 – Kiểm Tra Nhanh (CPU, ~5 phút)

```python
import pickle, numpy as np
from tensorflow import keras

# Load tất cả models
resnet = keras.models.load_model('resnet50_extractor.keras')
mlp    = keras.models.load_model('mlp_model.keras')
with open('xgb_models.pkl', 'rb') as f:
    xgb_models = pickle.load(f)
with open('calibrators.pkl', 'rb') as f:
    calibrators = pickle.load(f)
with open('ehr_scaler.pkl', 'rb') as f:
    ehr_scaler = pickle.load(f)

print("ResNet50:", resnet.output_shape)   # (None, 2048)
print("MLP:", mlp.output_shape)           # (None, 14)
print("XGB classifiers:", len(xgb_models))  # 14
print("Calibrators:", len(calibrators))     # 14
print("EHR features:", ehr_scaler.n_features_in_)  # 13
print("All models loaded successfully!")
```

---

## 7. Biến Môi Trường

| Biến | Giá Trị Mặc Định | Mô Tả |
|------|-----------------|-------|
| `AI_SERVER_URL` | `http://localhost:8000` | URL của FastAPI server |
| `DB_CONNECTION` | *(xem appsettings.json)* | SQL Server connection string |
| `MODEL_DIR` | `./models` | Thư mục chứa file mô hình |
| `MAX_IMAGE_SIZE` | `1024` | Kích thước ảnh tối đa (pixels) |
| `INFERENCE_TIMEOUT` | `60` | Timeout cho inference (giây) |

**Thiết lập trong PowerShell**:

```powershell
$env:AI_SERVER_URL = "http://localhost:8000"
$env:MODEL_DIR = "d:\DO_AN\Do_An_Cs\AI_Server\models"
```

---

## 8. Đánh Giá Mô Hình

Kết quả sau huấn luyện được lưu vào `clinical_evaluation.csv`. Các metrics mục tiêu:

| Bệnh | AUC (mục tiêu) | Threshold |
|------|---------------|-----------|
| Atelectasis | ≥ 0.75 | 0.3 |
| Cardiomegaly | ≥ 0.85 | 0.4 |
| Effusion | ≥ 0.80 | 0.35 |
| Infiltration | ≥ 0.70 | 0.3 |
| Mass | ≥ 0.80 | 0.4 |
| Nodule | ≥ 0.72 | 0.35 |
| Pneumonia | ≥ 0.76 | 0.35 |
| Pneumothorax | ≥ 0.85 | 0.45 |
| Consolidation | ≥ 0.78 | 0.35 |
| Edema | ≥ 0.82 | 0.4 |
| Emphysema | ≥ 0.88 | 0.5 |
| Fibrosis | ≥ 0.80 | 0.45 |
| Pleural_Thickening | ≥ 0.75 | 0.35 |
| Hernia | ≥ 0.90 | 0.5 |

**Công thức Ensemble**:

$$P_{final}(c) = \text{Isotonic}\left(0.5 \cdot P_{XGB}(c) + 0.5 \cdot P_{MLP}(c)\right)$$

**Threshold mặc định**: 0.35 (có thể điều chỉnh theo sensitivity/specificity yêu cầu lâm sàng).

---

*Tài liệu này được tạo tự động từ toàn bộ source code của dự án ChestAI.*#   D o _ A N _ C s  
 #   D o _ A N _ C s  
 