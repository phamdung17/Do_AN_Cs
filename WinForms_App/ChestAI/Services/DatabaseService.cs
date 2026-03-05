using Microsoft.Data.SqlClient;
using ChestAI.Models;
using System.Data;

namespace ChestAI;

/// <summary>
/// Tất cả tương tác với SQL Server đều đi qua lớp này.
/// Chuỗi kết nối đọc từ biến môi trường CHESTAI_CONNSTR hoặc fallback hardcode.
/// </summary>
public static class DatabaseService
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("CHESTAI_CONNSTR")
        ?? @"Server=localhost\SQLEXPRESS;Database=ChestAI;Integrated Security=True;TrustServerCertificate=True;";

    // ─── KIỂM TRA KẾT NỐI ────────────────────────────────────────────────────
    public static bool TestConnection()
    {
        try
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            return true;
        }
        catch { return false; }
    }

    // ─── USERS ───────────────────────────────────────────────────────────────
    public static (int userId, string fullName, string role)? ValidateLogin(
        string username, string plainPassword)
    {
        const string sql = """
            SELECT UserID, PasswordHash, FullName, Role
            FROM Users
            WHERE Username = @u AND IsActive = 1
            """;
        using var conn = new SqlConnection(ConnectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@u", username);

        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;

        // Đọc tất cả giá trị TRƯỚC khi đóng reader
        int    id   = rdr.GetInt32(0);
        string hash = rdr.GetString(1);
        string name = rdr.GetString(2);
        string role = rdr.GetString(3);
        rdr.Close();

        if (!BCrypt.Net.BCrypt.Verify(plainPassword, hash)) return null;

        // Cập nhật LastLogin
        using var upd = new SqlCommand(
            "UPDATE Users SET LastLogin = GETDATE() WHERE UserID = @id", conn);
        upd.Parameters.AddWithValue("@id", id);
        upd.ExecuteNonQuery();

        return (id, name, role);
    }

    // ─── PATIENTS ─────────────────────────────────────────────────────────────
    public static List<Patient> GetAllPatients()
    {
        const string sql = "SELECT * FROM Patients ORDER BY CreatedAt DESC";
        var list = new List<Patient>();
        using var conn = new SqlConnection(ConnectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read()) list.Add(MapPatient(rdr));
        return list;
    }

    public static List<Patient> SearchPatients(string keyword)
    {
        var list = new List<Patient>();
        using var conn = new SqlConnection(ConnectionString);
        conn.Open();
        using var cmd = new SqlCommand("sp_SearchPatients", conn)
        { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Keyword", keyword);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read()) list.Add(MapPatient(rdr));
        return list;
    }

    public static int InsertPatient(Patient p, int createdByUserId)
    {
        const string sql = """
            INSERT INTO Patients (FullName, DateOfBirth, Gender, Phone, Address,
                                  IsSmoker, MedicalHistory, CreatedBy)
            OUTPUT INSERTED.PatientID
            VALUES (@fn, @dob, @g, @ph, @addr, @sm, @mh, @cb)
            """;
        using var conn = new SqlConnection(ConnectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@fn",   p.FullName);
        cmd.Parameters.AddWithValue("@dob",  p.DateOfBirth);
        cmd.Parameters.AddWithValue("@g",    p.Gender.ToString());
        cmd.Parameters.AddWithValue("@ph",   (object?)p.Phone    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@addr", (object?)p.Address  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sm",   p.IsSmoker ? 1 : 0);
        cmd.Parameters.AddWithValue("@mh",   (object?)p.MedicalHistory ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cb",   createdByUserId);
        return (int)cmd.ExecuteScalar()!;
    }

    public static void UpdatePatient(Patient p)
    {
        const string sql = """
            UPDATE Patients SET FullName=@fn, DateOfBirth=@dob, Gender=@g, Phone=@ph,
            Address=@addr, IsSmoker=@sm, MedicalHistory=@mh
            WHERE PatientID=@id
            """;
        using var conn = new SqlConnection(ConnectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@fn",   p.FullName);
        cmd.Parameters.AddWithValue("@dob",  p.DateOfBirth);
        cmd.Parameters.AddWithValue("@g",    p.Gender.ToString());
        cmd.Parameters.AddWithValue("@ph",   (object?)p.Phone    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@addr", (object?)p.Address  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sm",   p.IsSmoker ? 1 : 0);
        cmd.Parameters.AddWithValue("@mh",   (object?)p.MedicalHistory ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id",   p.PatientID);
        cmd.ExecuteNonQuery();
    }

    // ─── XRAY IMAGES ─────────────────────────────────────────────────────────
    public static int InsertXRayImage(int patientId, string filePath,
                                      string viewPos, int uploadedBy)
    {
        const string sql = """
            INSERT INTO XRayImages (PatientID, FilePath, ViewPosition, UploadedBy)
            OUTPUT INSERTED.ImageID
            VALUES (@p, @fp, @vp, @ub)
            """;
        using var conn = new SqlConnection(ConnectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@p",  patientId);
        cmd.Parameters.AddWithValue("@fp", filePath);
        cmd.Parameters.AddWithValue("@vp", viewPos);
        cmd.Parameters.AddWithValue("@ub", uploadedBy);
        return (int)cmd.ExecuteScalar()!;
    }

    // ─── DIAGNOSES ────────────────────────────────────────────────────────────
    public static int InsertDiagnosis(Diagnosis d, int diagnosedBy)
    {
        using var conn = new SqlConnection(ConnectionString);
        conn.Open();
        using var tx  = conn.BeginTransaction();

        try
        {
            const string sqlD = """
                INSERT INTO Diagnoses (PatientID, ImageID, DiagnosedBy, TopDisease,
                    TopIcdCode, TopProbability, IsNormal, LesionAreaPercent,
                    HeatmapPath, SegMaskPath, RawJsonResult)
                OUTPUT INSERTED.DiagnosisID
                VALUES (@pid, @iid, @db, @td, @tic, @tp, @in, @lap, @hp, @sp, @rj)
                """;
            using var cmd = new SqlCommand(sqlD, conn, tx);
            cmd.Parameters.AddWithValue("@pid", d.PatientID);
            cmd.Parameters.AddWithValue("@iid", d.ImageID);
            cmd.Parameters.AddWithValue("@db",  diagnosedBy);
            cmd.Parameters.AddWithValue("@td",  d.TopDisease);
            cmd.Parameters.AddWithValue("@tic", string.IsNullOrEmpty(d.TopIcdCode) ? DBNull.Value : (object)d.TopIcdCode);
            cmd.Parameters.AddWithValue("@tp",  d.TopProbability);
            cmd.Parameters.AddWithValue("@in",  d.IsNormal ? 1 : 0);
            cmd.Parameters.AddWithValue("@lap", (object?)d.LesionAreaPercent ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@hp",  (object?)d.HeatmapPath      ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sp",  (object?)d.SegMaskPath      ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rj",  DBNull.Value);
            int diagId = (int)cmd.ExecuteScalar()!;

            // Chèn chi tiết từng bệnh
            foreach (var det in d.Details)
            {
                using var cmdDet = new SqlCommand(
                    "INSERT INTO DiagnosisDetails (DiagnosisID, DiseaseName, Probability) " +
                    "VALUES (@did, @dn, @p)", conn, tx);
                cmdDet.Parameters.AddWithValue("@did", diagId);
                cmdDet.Parameters.AddWithValue("@dn",  det.Disease);
                cmdDet.Parameters.AddWithValue("@p",   det.Probability);
                cmdDet.ExecuteNonQuery();
            }

            tx.Commit();
            return diagId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public static List<Diagnosis> GetPatientDiagnoses(int patientId)
    {
        var list = new List<Diagnosis>();
        using var conn = new SqlConnection(ConnectionString);
        conn.Open();
        using var cmd = new SqlCommand("sp_GetPatientDiagnoses", conn)
        { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PatientID", patientId);
        using var rdr = cmd.ExecuteReader();
        // Kiểm tra xem cột TopIcdCode có trong kết quả không
        int icdOrdinal = -1;
        try { icdOrdinal = rdr.GetOrdinal("TopIcdCode"); } catch { /* cột chưa tồn tại */ }

        while (rdr.Read())
        {
            list.Add(new Diagnosis
            {
                DiagnosisID       = rdr.GetInt32(0),
                DiagnosedAt       = rdr.GetDateTime(1),
                TopDisease        = rdr.GetString(2),
                TopProbability    = rdr.GetDouble(3),
                IsNormal          = rdr.GetBoolean(4),
                LesionAreaPercent = rdr.IsDBNull(5) ? null : rdr.GetDouble(5),
                DoctorConclusion  = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                DoctorConfirmed   = rdr.GetBoolean(7),
                DoctorName        = rdr.GetString(8),
                ImagePath         = rdr.IsDBNull(9)  ? null : rdr.GetString(9),
                HeatmapPath       = rdr.IsDBNull(10) ? null : rdr.GetString(10),
                SegMaskPath       = rdr.IsDBNull(11) ? null : rdr.GetString(11),
                TopIcdCode        = (icdOrdinal >= 0 && !rdr.IsDBNull(icdOrdinal))
                                    ? rdr.GetString(icdOrdinal) : string.Empty,
            });
        }
        return list;
    }

    public static void ConfirmDiagnosis(int diagnosisId, string doctorConclusion)
    {
        const string sql = """
            UPDATE Diagnoses SET DoctorConclusion=@dc, DoctorConfirmed=1, ConfirmedAt=GETDATE()
            WHERE DiagnosisID=@id
            """;
        using var conn = new SqlConnection(ConnectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@dc", doctorConclusion);
        cmd.Parameters.AddWithValue("@id", diagnosisId);
        cmd.ExecuteNonQuery();
    }

    // ─── HELPER ──────────────────────────────────────────────────────────────
    private static Patient MapPatient(SqlDataReader r) => new()
    {
        PatientID      = r.GetInt32(r.GetOrdinal("PatientID")),
        FullName       = r.GetString(r.GetOrdinal("FullName")),
        DateOfBirth    = r.GetDateTime(r.GetOrdinal("DateOfBirth")),
        Gender         = r.GetString(r.GetOrdinal("Gender"))[0],
        Phone          = r.IsDBNull(r.GetOrdinal("Phone"))    ? null : r.GetString(r.GetOrdinal("Phone")),
        Address        = r.IsDBNull(r.GetOrdinal("Address"))  ? null : r.GetString(r.GetOrdinal("Address")),
        IsSmoker       = r.GetBoolean(r.GetOrdinal("IsSmoker")),
        MedicalHistory = r.IsDBNull(r.GetOrdinal("MedicalHistory")) ? null : r.GetString(r.GetOrdinal("MedicalHistory")),
        CreatedAt      = r.GetDateTime(r.GetOrdinal("CreatedAt")),
    };
}
