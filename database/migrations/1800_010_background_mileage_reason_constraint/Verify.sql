SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-010')
    THROW 55100, N'Verify failed: 找不到 SchemaVersion 1.8.0-010。', 1;

IF (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC, VersionNumber DESC) <> N'1.8.0-010'
    THROW 55101, N'Verify failed: latest SchemaVersion 不是 1.8.0-010。', 1;

IF OBJECT_ID(N'dbo.RouteCalculationAttempts', N'U') IS NULL
    THROW 55102, N'Verify failed: RouteCalculationAttempts 不存在。', 1;

DECLARE @ConstraintObjectId INT =
    OBJECT_ID(N'dbo.CK_RouteCalculationAttempts_Reason', N'C');

IF @ConstraintObjectId IS NULL
    THROW 55103, N'Verify failed: CK_RouteCalculationAttempts_Reason 不存在。', 1;

DECLARE @Definition NVARCHAR(MAX) =
    (SELECT definition FROM sys.check_constraints WHERE object_id = @ConstraintObjectId);
DECLARE @Compact NVARCHAR(MAX) =
    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @Definition, N' ', N''), N'[', N''), N']', N''), N'(', N''), N')', N''));

IF @Definition IS NULL
   OR @Compact NOT LIKE N'%visitorcalculate%'
   OR @Compact NOT LIKE N'%leaderretry%'
   OR @Compact NOT LIKE N'%correctionrecalculate%'
   OR @Compact NOT LIKE N'%backgroundmileagejob%'
    THROW 55104, N'Verify failed: CalculationReason constraint 未包含完整 1.8.0-010 reason contract。', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE object_id = @ConstraintObjectId
      AND (is_disabled = 1 OR is_not_trusted = 1)
)
    THROW 55105, N'Verify failed: CalculationReason constraint 不是 enabled + trusted。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.RouteCalculationAttempts
    WHERE CalculationReason NOT IN
    (
        N'VisitorCalculate',
        N'LeaderRetry',
        N'CorrectionRecalculate',
        N'BackgroundMileageJob'
    )
)
    THROW 55106, N'Verify failed: RouteCalculationAttempts 存在不允許的 CalculationReason。', 1;

SELECT
    N'PASS' AS VerifyStatus,
    DB_NAME() AS DatabaseName,
    N'1.8.0-010' AS MigrationVersion,
    @Definition AS CalculationReasonConstraintDefinition,
    COUNT_BIG(*) AS RouteCalculationAttemptCount,
    SUM(CASE WHEN CalculationReason = N'BackgroundMileageJob' THEN 1 ELSE 0 END) AS BackgroundMileageAttemptCount
FROM dbo.RouteCalculationAttempts;
