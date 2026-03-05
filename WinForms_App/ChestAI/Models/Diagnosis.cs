using Newtonsoft.Json;

namespace ChestAI.Models;

/// <summary>Kết quả chẩn đoán AI – ánh xạ bảng Diagnoses + DiagnosisDetails.</summary>
public class Diagnosis
{
    public int       DiagnosisID         { get; set; }
    public int       PatientID           { get; set; }
    public int       ImageID             { get; set; }
    public DateTime  DiagnosedAt         { get; set; }
    public string    TopDisease          { get; set; } = string.Empty;
    public string    TopIcdCode          { get; set; } = string.Empty;
    public double    TopProbability      { get; set; }
    public bool      IsNormal            { get; set; }
    public double?   LesionAreaPercent   { get; set; }
    public string?   HeatmapPath         { get; set; }
    public string?   SegMaskPath         { get; set; }
    public string?   DoctorConclusion    { get; set; }
    public bool      DoctorConfirmed     { get; set; }
    public string?   DoctorName          { get; set; }
    public string?   ImagePath           { get; set; }
    public string?   TextualRationale    { get; set; }    // Giải thích ngắn từ AI
    public List<string> ModalitiesUsed   { get; set; } = []; // ["image", "text", "ehr"]

    /// <summary>Danh sách xác suất từng bệnh (từ /predict).</summary>
    public List<DiseaseResult> Details { get; set; } = [];
}

public class DiseaseResult
{
    [JsonProperty("disease")]               public string Disease               { get; set; } = string.Empty;
    [JsonProperty("icd_code")]              public string IcdCode               { get; set; } = string.Empty;
    [JsonProperty("probability")]           public double Probability           { get; set; }
    [JsonProperty("calibrated_probability")] public double CalibratedProbability { get; set; }
    [JsonProperty("rank")]                  public int    Rank                  { get; set; }
}

/// <summary>Response từ endpoint POST /predict của FastAPI.</summary>
public class PredictApiResponse
{
    public List<DiseaseResult> diagnoses         { get; set; } = [];
    public List<DetectionBox>  detections        { get; set; } = [];
    public string              top_disease       { get; set; } = string.Empty;
    public string              top_icd_code      { get; set; } = string.Empty;
    public double              top_probability   { get; set; }
    public bool                normal            { get; set; }
    public string              textual_rationale { get; set; } = string.Empty;  // Mới
    public List<string>        modalities_used   { get; set; } = [];             // Mới
}

public class DetectionBox
{
    public double x1         { get; set; }
    public double y1         { get; set; }
    public double x2         { get; set; }
    public double y2         { get; set; }
    public double confidence { get; set; }
    public string label      { get; set; } = string.Empty;
}

/// <summary>Response từ endpoint POST /segment.</summary>
public class SegmentApiResponse
{
    public string mask_image_base64   { get; set; } = string.Empty;
    public bool   has_lesion          { get; set; }
    public double lesion_area_percent { get; set; }
}

/// <summary>Response từ endpoint POST /explain.</summary>
public class ExplainApiResponse
{
    public string heatmap_image_base64 { get; set; } = string.Empty;
    public string disease_explained    { get; set; } = string.Empty;
    public string icd_code             { get; set; } = string.Empty;   // Mới
    public double confidence           { get; set; }
}

/// <summary>Một đặc trưng trong SHAP explanation.</summary>
public class ShapFeatureContribution
{
    public string  name        { get; set; } = string.Empty;
    public string? value       { get; set; }       // Giá trị thực tế (ví dụ: "72 bpm")
    public double  shap_value  { get; set; }       // Đóng góp SHAP
    public string  direction   { get; set; } = string.Empty;  // "positive" | "negative"
}

/// <summary>Response từ endpoint POST /shap.</summary>
public class ShapApiResponse
{
    public string                       disease               { get; set; } = string.Empty;
    public string                       icd_code              { get; set; } = string.Empty;
    public double                       probability           { get; set; }
    public List<ShapFeatureContribution> feature_contributions { get; set; } = [];
    public string                       textual_explanation   { get; set; } = string.Empty;
    public List<string>                 top_positive_features { get; set; } = [];
    public List<string>                 top_negative_features { get; set; } = [];
}
