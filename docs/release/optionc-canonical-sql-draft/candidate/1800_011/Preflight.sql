/*
  1800_011 B3 schema candidate -- READ-ONLY PREFLIGHT.
  This candidate is executable only after a separately approved UAT change
  window. It is NOT discovered by the migration runner and must never be
  copied into database/migrations without a new Owner authorization.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 51100, N'1800_011 must target the named Field Visit UAT database, never a system database.', 1;

IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Organizations', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Teams', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Users', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Locations', N'U') IS NULL
    THROW 51101, N'1800_011 prerequisite catalog is incomplete.', 1;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-011')
    THROW 51102, N'1800_011 is already recorded; do not rerun.', 1;

IF (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions
    ORDER BY AppliedAt DESC, VersionNumber DESC) <> N'1.8.0-010'
    THROW 51103, N'Latest SchemaVersion must be exactly 1.8.0-010.', 1;

IF OBJECT_ID(N'dbo.ChangeRequests', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.ChangeRequestEvents', N'U') IS NOT NULL
    THROW 51104, N'B3 tables already exist; stop for DBA drift review.', 1;

/*
  DBA evidence to capture outside this script: exact server/database identity,
  SQL login/Grant-Revoke evidence, current Locations catalog/relations/triggers,
  backup/restore point, and this file SHA256. This preflight does not grant
  permissions or modify data.
*/
SELECT CAST(1 AS bit) AS PreflightPassed,
       DB_NAME() AS DatabaseName,
       @@SERVERNAME AS ServerName,
       N'1.8.0-010' AS ExpectedLatestVersion;
