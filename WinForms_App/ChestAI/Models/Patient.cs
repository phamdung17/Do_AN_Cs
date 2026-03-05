namespace ChestAI.Models;

/// <summary>Hồ sơ bệnh nhân – ánh xạ bảng Patients.</summary>
public class Patient
{
    public int       PatientID      { get; set; }
    public string    FullName       { get; set; } = string.Empty;
    public DateTime  DateOfBirth    { get; set; }
    public char      Gender         { get; set; }   // 'M' hoặc 'F'
    public string?   Phone          { get; set; }
    public string?   Address        { get; set; }
    public bool      IsSmoker       { get; set; }
    public string?   MedicalHistory { get; set; }
    public DateTime  CreatedAt      { get; set; }

    /// <summary>Tuổi tính từ ngày sinh (dùng cho API EHR).</summary>
    public int Age => (int)((DateTime.Today - DateOfBirth).TotalDays / 365.25);

    /// <summary>Giới tính dạng int (1=Nam, 0=Nữ) gửi lên API.</summary>
    public int GenderInt => Gender == 'M' ? 1 : 0;

    public override string ToString() => $"[{PatientID}] {FullName}";
}
