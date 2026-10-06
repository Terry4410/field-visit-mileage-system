SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @LockResult INT;
    EXEC @LockResult = sys.sp_getapplock
        @Resource = N'FieldVisit.SchemaMigration',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 0;

    IF @LockResult < 0
        THROW 55000, N'無法取得 FieldVisit Migration lock。', 1;

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
       OR NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-009')
        THROW 55001, N'尚未套用 prerequisite Migration 1.8.0-009。', 1;

    IF (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC, VersionNumber DESC) <> N'1.8.0-009'
        THROW 55002, N'latest SchemaVersion 不是 exact predecessor 1.8.0-009。', 1;

    IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-010')
        THROW 55003, N'Migration 1.8.0-010 已套用，不得重複執行。', 1;

    IF OBJECT_ID(N'dbo.RouteCalculationAttempts', N'U') IS NULL
        THROW 55004, N'找不到 dbo.RouteCalculationAttempts；停止 Migration。', 1;

    DECLARE @ConstraintObjectId INT =
        OBJECT_ID(N'dbo.CK_RouteCalculationAttempts_Reason', N'C');

    IF @ConstraintObjectId IS NULL
        THROW 55005, N'找不到既有 CK_RouteCalculationAttempts_Reason；停止 Migration。', 1;

    DECLARE @OldDefinition NVARCHAR(MAX) =
        (SELECT definition FROM sys.check_constraints WHERE object_id = @ConstraintObjectId);
    DECLARE @OldCompact NVARCHAR(MAX) =
        LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            @OldDefinition, N' ', N''), N'[', N''), N']', N''), N'(', N''), N')', N''));

    IF @OldDefinition IS NULL
       OR @OldCompact NOT LIKE N'%visitorcalculate%'
       OR @OldCompact NOT LIKE N'%leaderretry%'
       OR @OldCompact NOT LIKE N'%correctionrecalculate%'
       OR @OldCompact LIKE N'%backgroundmileagejob%'
        THROW 55006, N'既有 CalculationReason constraint 與 1.8.0-009 predecessor contract 不一致；請由 IT Review。', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE object_id = @ConstraintObjectId
          AND (is_disabled = 1 OR is_not_trusted = 1)
    )
        THROW 55007, N'既有 CalculationReason constraint 不是 enabled + trusted；停止 Migration。', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.RouteCalculationAttempts
        WHERE CalculationReason NOT IN
        (
            N'VisitorCalculate',
            N'LeaderRetry',
            N'CorrectionRecalculate'
        )
    )
        THROW 55008, N'既有 RouteCalculationAttempts 含 predecessor contract 以外的 CalculationReason；停止 Migration。', 1;

    ALTER TABLE dbo.RouteCalculationAttempts
        DROP CONSTRAINT CK_RouteCalculationAttempts_Reason;

    ALTER TABLE dbo.RouteCalculationAttempts WITH CHECK ADD
        CONSTRAINT CK_RouteCalculationAttempts_Reason
        CHECK
        (
            CalculationReason = N'VisitorCalculate'
            OR CalculationReason = N'LeaderRetry'
            OR CalculationReason = N'CorrectionRecalculate'
            OR CalculationReason = N'BackgroundMileageJob'
        );

    ALTER TABLE dbo.RouteCalculationAttempts
        CHECK CONSTRAINT CK_RouteCalculationAttempts_Reason;

    INSERT dbo.SchemaVersions(VersionNumber, Description, AppliedAt, AppliedBy)
    VALUES
    (
        N'1.8.0-010',
        N'Owner Pre-UAT: align route calculation reason constraint with background mileage job',
        SYSUTCDATETIME(),
        N'v1.8.0 Owner Pre-UAT'
    );

    COMMIT TRANSACTION;
    PRINT N'1.8.0-010 background mileage reason constraint correction completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
