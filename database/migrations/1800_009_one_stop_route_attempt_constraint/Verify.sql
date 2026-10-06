SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-009')
    THROW 54800, N'Verify failed: 找不到 SchemaVersion 1.8.0-009。', 1;

IF (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC, VersionNumber DESC) <> N'1.8.0-009'
    THROW 54801, N'Verify failed: latest SchemaVersion 不是 1.8.0-009。', 1;

IF OBJECT_ID(N'dbo.RouteCalculationAttempts', N'U') IS NULL
    THROW 54802, N'Verify failed: RouteCalculationAttempts 不存在。', 1;

DECLARE @ConstraintObjectId INT =
    OBJECT_ID(N'dbo.CK_RouteCalculationAttempts_StopCount', N'C');

IF @ConstraintObjectId IS NULL
    THROW 54803, N'Verify failed: CK_RouteCalculationAttempts_StopCount 不存在。', 1;

DECLARE @Definition NVARCHAR(MAX) =
    (SELECT definition FROM sys.check_constraints WHERE object_id = @ConstraintObjectId);
DECLARE @Normalized NVARCHAR(MAX) =
    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @Definition, N' ', N''), N'[', N''), N']', N''), N'(', N''), N')', N''));

IF @Definition IS NULL OR @Normalized <> N'stopcount>=1'
    THROW 54804, N'Verify failed: StopCount constraint 不是 >= 1。', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE object_id = @ConstraintObjectId
      AND (is_disabled = 1 OR is_not_trusted = 1)
)
    THROW 54805, N'Verify failed: StopCount constraint 不是 enabled + trusted。', 1;

IF EXISTS(SELECT 1 FROM dbo.RouteCalculationAttempts WHERE StopCount < 1)
    THROW 54806, N'Verify failed: RouteCalculationAttempts 存在 StopCount < 1。', 1;

SELECT
    N'PASS' AS VerifyStatus,
    DB_NAME() AS DatabaseName,
    N'1.8.0-009' AS MigrationVersion,
    @Definition AS StopCountConstraintDefinition,
    COUNT_BIG(*) AS RouteCalculationAttemptCount,
    SUM(CASE WHEN StopCount = 1 THEN 1 ELSE 0 END) AS OneStopAttemptCount
FROM dbo.RouteCalculationAttempts;
