/* 1800_012 B3 atomic-apply candidate -- READ-ONLY PREFLIGHT. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() IN(N'master',N'model',N'msdb',N'tempdb') THROW 51200,N'Invalid target database.',1;
IF (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC)<>N'1.8.0-011'
    THROW 51201,N'Latest SchemaVersion must be exactly 1.8.0-011.',1;
IF OBJECT_ID(N'dbo.ChangeRequests',N'U') IS NULL OR OBJECT_ID(N'dbo.ChangeRequestEvents',N'U') IS NULL OR OBJECT_ID(N'dbo.Locations',N'U') IS NULL
    THROW 51202,N'1800_012 B3/Locations prerequisites are incomplete.',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-012')
    THROW 51203,N'1800_012 is already recorded; do not rerun.',1;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE is_disabled=0 AND parent_id IN(OBJECT_ID(N'dbo.ChangeRequests'),OBJECT_ID(N'dbo.ChangeRequestEvents')))
    THROW 51204,N'Enabled B3 table trigger is prohibited.',1;
SELECT CAST(1 AS bit) AS PreflightPassed,DB_NAME() AS DatabaseName,N'1.8.0-011' AS ExpectedLatestVersion;
