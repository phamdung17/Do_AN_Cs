-- Migration 001: Add TopIcdCode to Diagnoses table
-- Run this script once against the ChestAI database

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Diagnoses' AND COLUMN_NAME = 'TopIcdCode'
)
BEGIN
    ALTER TABLE Diagnoses
        ADD TopIcdCode NVARCHAR(20) NULL;
    PRINT 'Column TopIcdCode added to Diagnoses.';
END
ELSE
    PRINT 'Column TopIcdCode already exists – skipped.';
GO

-- Also ensure sp_GetPatientDiagnoses returns TopIcdCode.
-- Recreate or alter the stored procedure to include it.
-- The procedure below is a safe DROP-and-CREATE; update the SELECT list as needed.
IF OBJECT_ID('dbo.sp_GetPatientDiagnoses', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_GetPatientDiagnoses;
GO

CREATE PROCEDURE dbo.sp_GetPatientDiagnoses
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
        COALESCE(u.FullName, '') AS DoctorName,
        xi.ImagePath,
        d.HeatmapPath,
        d.SegMaskPath,
        d.TopIcdCode
    FROM Diagnoses d
    LEFT JOIN Users    u  ON u.UserID  = d.ConfirmedBy
    LEFT JOIN XRayImages xi ON xi.ImageID = d.ImageID
    WHERE d.PatientID = @PatientID
    ORDER BY d.DiagnosedAt DESC;
END
GO
