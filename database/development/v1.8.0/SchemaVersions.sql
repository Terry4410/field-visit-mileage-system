SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
    THROW 55010, N'Development harness requires dbo.SchemaVersions.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-007')
    INSERT dbo.SchemaVersions (VersionNumber, Description, AppliedAt, AppliedBy)
    VALUES (N'1.8.0-007', N'Development harness final structural state', SYSUTCDATETIME(), N'development-harness');
