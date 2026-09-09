SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-007')
    THROW 55020, N'Development harness verify failed: schema version metadata missing.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_principals
    WHERE name NOT IN (N'dbo', N'guest', N'INFORMATION_SCHEMA', N'sys', N'public')
      AND type IN ('S','U','G','E','X')
)
    THROW 55021, N'Development harness verify failed: environment principal found.', 1;

IF EXISTS (SELECT 1 FROM dbo.Users)
    THROW 55022, N'Development harness verify failed: user/business data found.', 1;

IF EXISTS (SELECT 1 FROM dbo.Organizations)
    THROW 55023, N'Development harness verify failed: organization data found.', 1;

IF EXISTS (SELECT 1 FROM dbo.VisitTrips)
    THROW 55024, N'Development harness verify failed: transaction data found.', 1;
