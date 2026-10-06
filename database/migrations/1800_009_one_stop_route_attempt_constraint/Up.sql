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
        THROW 54700, N'無法取得 FieldVisit Migration lock。', 1;

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
       OR NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-008')
        THROW 54701, N'尚未套用 prerequisite Migration 1.8.0-008。', 1;

    IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-009')
        THROW 54702, N'Migration 1.8.0-009 已套用，不得重複執行。', 1;

    IF OBJECT_ID(N'dbo.RouteCalculationAttempts', N'U') IS NULL
        THROW 54703, N'找不到 dbo.RouteCalculationAttempts；停止 Migration。', 1;

    DECLARE @ConstraintObjectId INT =
        OBJECT_ID(N'dbo.CK_RouteCalculationAttempts_StopCount', N'C');

    IF @ConstraintObjectId IS NULL
        THROW 54704, N'找不到既有 CK_RouteCalculationAttempts_StopCount；停止 Migration。', 1;

    DECLARE @OldDefinition NVARCHAR(MAX) =
        (SELECT definition FROM sys.check_constraints WHERE object_id = @ConstraintObjectId);
    DECLARE @OldNormalized NVARCHAR(MAX) =
        LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            @OldDefinition, N' ', N''), N'[', N''), N']', N''), N'(', N''), N')', N''));

    IF @OldDefinition IS NULL OR @OldNormalized <> N'stopcount>=2'
        THROW 54705, N'既有 StopCount constraint 與 1.8.0-007 baseline 不一致；請由 IT Review。', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE object_id = @ConstraintObjectId
          AND (is_disabled = 1 OR is_not_trusted = 1)
    )
        THROW 54706, N'既有 StopCount constraint 不是 enabled + trusted；停止 Migration。', 1;

    IF EXISTS(SELECT 1 FROM dbo.RouteCalculationAttempts WHERE StopCount < 1)
        THROW 54707, N'既有 RouteCalculationAttempts 含 StopCount < 1；停止 Migration。', 1;

    ALTER TABLE dbo.RouteCalculationAttempts
        DROP CONSTRAINT CK_RouteCalculationAttempts_StopCount;

    ALTER TABLE dbo.RouteCalculationAttempts WITH CHECK ADD
        CONSTRAINT CK_RouteCalculationAttempts_StopCount
        CHECK (StopCount >= 1);

    ALTER TABLE dbo.RouteCalculationAttempts
        CHECK CONSTRAINT CK_RouteCalculationAttempts_StopCount;

    INSERT dbo.SchemaVersions(VersionNumber, Description, AppliedAt, AppliedBy)
    VALUES
    (
        N'1.8.0-009',
        N'Owner Pre-UAT: align route calculation attempt StopCount with one-stop trip rule',
        SYSUTCDATETIME(),
        N'v1.8.0 Owner Pre-UAT'
    );

    COMMIT TRANSACTION;
    PRINT N'1.8.0-009 one-stop route-attempt constraint correction completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
