using System.Net.Http.Headers;
using Newtonsoft.Json;
using ChestAI.Models;
using System.Drawing;

namespace ChestAI;

/// <summary>
/// Giao tiếp với FastAPI Server qua HTTP.
/// Hỗ trợ: /predict (3 modalities), /segment, /explain (Grad-CAM), /shap (SHAP XAI)
/// </summary>
public static class ApiService
{
    private static readonly HttpClient _http = new()
    {
        BaseAddress = new Uri(
            Environment.GetEnvironmentVariable("CHESTAI_API_URL")
            ?? "http://localhost:8000/"),
        Timeout = TimeSpan.FromSeconds(180)
    };

    // ─── HEALTH CHECK ─────────────────────────────────────────────────────────
    public static async Task<bool> IsServerAliveAsync()
    {
        try
        {
            var resp = await _http.GetAsync("health");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ─── PREDICT (Multimodal: Image + EHR + ClinicalBERT text) ───────────────
    /// <summary>
    /// Gọi POST /predict – trả về ranked diagnoses với ICD codes + textual rationale.
    /// </summary>
    public static async Task<PredictApiResponse> PredictAsync(
        string imagePath, Models.Patient patient,
        EhrVitals? vitals = null,
        string clinicalNotes = "")
    {
        using var form = BuildImageForm(imagePath);
        AddEhrFields(form, patient, vitals);
        if (!string.IsNullOrWhiteSpace(clinicalNotes))
            form.Add(new StringContent(clinicalNotes), "clinical_notes");

        var resp = await _http.PostAsync("predict", form);
        return await DeserializeAsync<PredictApiResponse>(resp);
    }

    // ─── SEGMENT ─────────────────────────────────────────────────────────────
    public static async Task<SegmentApiResponse> SegmentAsync(string imagePath)
    {
        using var form = BuildImageForm(imagePath);
        var resp = await _http.PostAsync("segment", form);
        return await DeserializeAsync<SegmentApiResponse>(resp);
    }

    // ─── EXPLAIN (Grad-CAM) ──────────────────────────────────────────────────
    public static async Task<ExplainApiResponse> ExplainAsync(
        string imagePath, Models.Patient patient, int classIndex = 6,
        EhrVitals? vitals = null)
    {
        using var form = BuildImageForm(imagePath);
        AddEhrFields(form, patient, vitals);
        form.Add(new StringContent(classIndex.ToString()), "class_index");

        var resp = await _http.PostAsync("explain", form);
        return await DeserializeAsync<ExplainApiResponse>(resp);
    }

    // ─── SHAP (EHR + XGBoost TreeExplainer) ──────────────────────────────────
    /// <summary>
    /// Gọi POST /shap – trả về SHAP feature contributions + textual explanation.
    /// </summary>
    public static async Task<ShapApiResponse> ShapAsync(
        string imagePath, Models.Patient patient, int classIndex = 6,
        EhrVitals? vitals = null, string clinicalNotes = "")
    {
        using var form = BuildImageForm(imagePath);
        AddEhrFields(form, patient, vitals);
        form.Add(new StringContent(classIndex.ToString()), "class_index");
        if (!string.IsNullOrWhiteSpace(clinicalNotes))
            form.Add(new StringContent(clinicalNotes), "clinical_notes");

        var resp = await _http.PostAsync("shap", form);
        return await DeserializeAsync<ShapApiResponse>(resp);
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────────
    private static MultipartFormDataContent BuildImageForm(string imagePath)
    {
        var form = new MultipartFormDataContent();
        var fileBytes = File.ReadAllBytes(imagePath);
        var content   = new ByteArrayContent(fileBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "file", Path.GetFileName(imagePath));
        return form;
    }

    private static void AddEhrFields(MultipartFormDataContent form, Models.Patient p,
                                     EhrVitals? vitals)
    {
        form.Add(new StringContent(p.Age.ToString()),        "age");
        form.Add(new StringContent(p.GenderInt.ToString()),  "gender");
        form.Add(new StringContent(p.IsSmoker ? "1" : "0"), "smoking");
        form.Add(new StringContent("1"),                     "view_pa");
        form.Add(new StringContent("0"),                     "history");

        // Vitals & Lab tests (từ EhrVitals nếu có)
        var v = vitals ?? new EhrVitals();
        form.Add(new StringContent(v.HeartRate.ToString("F0")), "hr");
        form.Add(new StringContent(v.SpO2.ToString("F0")),      "spo2");
        form.Add(new StringContent(v.Temperature.ToString("F1")), "temp");
        form.Add(new StringContent(v.SBP.ToString("F0")),       "sbp");
        form.Add(new StringContent(v.RespiratoryRate.ToString("F0")), "rr");
        form.Add(new StringContent(v.WBC.ToString("F1")),       "wbc");
        form.Add(new StringContent(v.CRP.ToString("F1")),       "crp");
        form.Add(new StringContent(v.Lactate.ToString("F2")),   "lactate");
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage resp)
    {
        string body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"API Error {(int)resp.StatusCode}: {body}");
        return JsonConvert.DeserializeObject<T>(body)
               ?? throw new Exception("API trả về dữ liệu rỗng.");
    }

    /// <summary>Chuyển chuỗi base64 từ API thành đối tượng Bitmap.</summary>
    public static Bitmap Base64ToBitmap(string base64)
    {
        byte[] bytes = Convert.FromBase64String(base64);
        using var ms = new MemoryStream(bytes);
        return new Bitmap(ms);
    }
}

/// <summary>
/// Thông số sinh tồn và xét nghiệm của bệnh nhân (EHR Vitals  Labs).
/// Giá trị mặc định = bình thường.
/// </summary>
public class EhrVitals
{
    public float HeartRate        { get; set; } = 75f;   // bpm
    public float SpO2             { get; set; } = 97f;   // %
    public float Temperature      { get; set; } = 37.0f; // °C
    public float SBP              { get; set; } = 120f;  // mmHg
    public float RespiratoryRate  { get; set; } = 18f;   // /phút
    public float WBC              { get; set; } = 7.0f;  // K/μL
    public float CRP              { get; set; } = 5.0f;  // mg/L
    public float Lactate          { get; set; } = 1.0f;  // mmol/L
}
