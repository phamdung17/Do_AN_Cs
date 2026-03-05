-- ============================================================
--  ChestAI – SQL Server Schema
--  Cơ sở dữ liệu cho hệ thống chẩn đoán X-quang đa phương thức
-- ============================================================

USE master;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'ChestAI')
    CREATE DATABASE ChestAI;
GO

USE ChestAI;
GO

-- ============================================================
-- BẢNG 1: Users – Tài khoản bác sĩ / quản trị viên
-- ============================================================
CREATE TABLE Users (
    UserID       INT IDENTITY(1,1) PRIMARY KEY,
    Username     NVARCHAR(50)  NOT NULL UNIQUE,
    PasswordHash NVARCHAR(256) NOT NULL,           -- Lưu hash bcrypt, không lưu plaintext
    FullName     NVARCHAR(100) NOT NULL,
    Role         NVARCHAR(20)  NOT NULL DEFAULT 'Doctor', -- 'Doctor' | 'Admin'
    IsActive     BIT           NOT NULL DEFAULT 1,
    CreatedAt    DATETIME2     NOT NULL DEFAULT GETDATE(),
    LastLogin    DATETIME2     NULL
);
GO

-- ============================================================
-- BẢNG 2: Patients – Hồ sơ bệnh nhân
-- ============================================================
CREATE TABLE Patients (
    PatientID       INT IDENTITY(1,1) PRIMARY KEY,
    FullName        NVARCHAR(100) NOT NULL,
    DateOfBirth     DATE          NOT NULL,
    Gender          NCHAR(1)      NOT NULL CHECK (Gender IN (N'M', N'F')), -- M=Nam F=Nữ
    Phone           NVARCHAR(20)  NULL,
    Address         NVARCHAR(255) NULL,
    -- Dữ liệu lâm sàng (EHR) gửi lên API
    IsSmoker        BIT           NOT NULL DEFAULT 0,
    MedicalHistory  NVARCHAR(500) NULL,    -- Tiền sử bệnh (text tự do)
    CreatedAt       DATETIME2     NOT NULL DEFAULT GETDATE(),
    CreatedBy       INT           NOT NULL REFERENCES Users(UserID)
);
GO

-- ============================================================
-- BẢNG 3: XRayImages – Quản lý file ảnh X-quang đã upload
-- ============================================================
CREATE TABLE XRayImages (
    ImageID      INT IDENTITY(1,1) PRIMARY KEY,
    PatientID    INT           NOT NULL REFERENCES Patients(PatientID),
    FilePath     NVARCHAR(500) NOT NULL,      -- Đường dẫn file trên server
    ViewPosition NVARCHAR(5)   NOT NULL DEFAULT 'PA', -- 'PA' | 'AP'
    UploadedAt   DATETIME2     NOT NULL DEFAULT GETDATE(),
    UploadedBy   INT           NOT NULL REFERENCES Users(UserID)
);
GO

-- ============================================================
-- BẢNG 4: Diagnoses – Kết quả chẩn đoán AI
-- ============================================================
CREATE TABLE Diagnoses (
    DiagnosisID         INT IDENTITY(1,1) PRIMARY KEY,
    PatientID           INT           NOT NULL REFERENCES Patients(PatientID),
    ImageID             INT           NOT NULL REFERENCES XRayImages(ImageID),
    DiagnosedBy         INT           NOT NULL REFERENCES Users(UserID),
    DiagnosedAt         DATETIME2     NOT NULL DEFAULT GETDATE(),

    -- Kết quả AI tổng hợp
    TopDisease          NVARCHAR(50)  NOT NULL,
    TopProbability      FLOAT         NOT NULL,
    IsNormal            BIT           NOT NULL DEFAULT 0,
    RawJsonResult       NVARCHAR(MAX) NULL,     -- Toàn bộ JSON từ /predict

    -- Segmentation
    SegMaskPath         NVARCHAR(500) NULL,     -- Đường dẫn ảnh mask
    LesionAreaPercent   FLOAT         NULL,

    -- Grad-CAM
    HeatmapPath         NVARCHAR(500) NULL,     -- Đường dẫn ảnh heatmap

    -- Xác nhận của bác sĩ
    DoctorConclusion    NVARCHAR(500) NULL,
    DoctorConfirmed     BIT           NOT NULL DEFAULT 0,
    ConfirmedAt         DATETIME2     NULL
);
GO

-- ============================================================
-- BẢNG 5: DiagnosisDetails – Chi tiết xác suất từng bệnh
-- ============================================================
CREATE TABLE DiagnosisDetails (
    DetailID     INT IDENTITY(1,1) PRIMARY KEY,
    DiagnosisID  INT          NOT NULL REFERENCES Diagnoses(DiagnosisID),
    DiseaseName  NVARCHAR(50) NOT NULL,
    Probability  FLOAT        NOT NULL
);
GO

-- ============================================================
-- BẢNG 6: AuditLog – Ghi lại mọi hành động quan trọng
-- ============================================================
CREATE TABLE AuditLog (
    LogID      INT IDENTITY(1,1) PRIMARY KEY,
    UserID     INT           NULL REFERENCES Users(UserID),
    Action     NVARCHAR(100) NOT NULL,
    Detail     NVARCHAR(500) NULL,
    IPAddress  NVARCHAR(45)  NULL,
    LoggedAt   DATETIME2     NOT NULL DEFAULT GETDATE()
);
GO

-- ============================================================
-- INDEXES – Tăng tốc truy vấn phổ biến
-- ============================================================
CREATE INDEX IX_Diagnoses_PatientID  ON Diagnoses(PatientID);
CREATE INDEX IX_Diagnoses_DiagnosedAt ON Diagnoses(DiagnosedAt DESC);
CREATE INDEX IX_XRayImages_PatientID ON XRayImages(PatientID);
CREATE INDEX IX_DiagnosisDetails_DiagnosisID ON DiagnosisDetails(DiagnosisID);
GO

-- ============================================================
-- DỮ LIỆU MẪU – Tài khoản Admin mặc định
-- (Password: Admin@123 – hashed bằng bcrypt, thay bằng hash thật khi deploy)
-- ============================================================
INSERT INTO Users (Username, PasswordHash, FullName, Role)
VALUES ('admin', '$2a$12$PLACEHOLDER_HASH_REPLACE_THIS', N'Quản trị viên', 'Admin');
GO

-- ============================================================
-- STORED PROCEDURES
-- ============================================================

-- Lấy lịch sử chẩn đoán của 1 bệnh nhân (kèm chi tiết)
CREATE OR ALTER PROCEDURE sp_GetPatientDiagnoses
    @PatientID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        d.DiagnosisID,
        d.DiagnosedAt,
        d.TopDisease,
        d.TopProbability,
        d.IsNormal,
        d.LesionAreaPercent,
        d.DoctorConclusion,
        d.DoctorConfirmed,
        u.FullName AS DoctorName,
        xi.FilePath AS ImagePath,
        d.HeatmapPath,
        d.SegMaskPath
    FROM Diagnoses d
    JOIN Users u    ON d.DiagnosedBy = u.UserID
    JOIN XRayImages xi ON d.ImageID  = xi.ImageID
    WHERE d.PatientID = @PatientID
    ORDER BY d.DiagnosedAt DESC;
END;
GO

-- Tìm kiếm bệnh nhân theo tên
CREATE OR ALTER PROCEDURE sp_SearchPatients
    @Keyword NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT PatientID, FullName, DateOfBirth, Gender, Phone, CreatedAt
    FROM Patients
    WHERE FullName LIKE N'%' + @Keyword + N'%'
    ORDER BY FullName;
END;
GO

-- Thống kê tổng quan (dashboard)
CREATE OR ALTER PROCEDURE sp_GetDashboardStats
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        (SELECT COUNT(*) FROM Patients)                                AS TotalPatients,
        (SELECT COUNT(*) FROM Diagnoses)                               AS TotalDiagnoses,
        (SELECT COUNT(*) FROM Diagnoses WHERE CAST(DiagnosedAt AS DATE) = CAST(GETDATE() AS DATE)) AS TodayDiagnoses,
        (SELECT COUNT(*) FROM Diagnoses WHERE IsNormal = 0)            AS AbnormalCases,
        (SELECT TOP 1 TopDisease FROM Diagnoses
         GROUP BY TopDisease ORDER BY COUNT(*) DESC)                   AS MostCommonDisease;
END;
GO
