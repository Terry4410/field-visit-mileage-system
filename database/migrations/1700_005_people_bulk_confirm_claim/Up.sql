SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    /* ================================================================
       v1.7.0-005 People Bulk Atomic Confirm Claim

       Purpose:
       Allow the transient ImportBatches status "Confirming" so the API
       can atomically claim a Previewed batch before applying writes.

       This prevents two concurrent Confirm requests from both applying
       the same authorization batch.

       IMPORTANT:
       - No business data is changed by this migration.
       - 1700_001 through 1700_004 remain immutable.
       ================================================================ */

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
        THROW 52000,
              N'找不到 dbo.SchemaVersions。',
              1;

    IF NOT EXISTS(
        SELECT 1
        FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-004'
    )
        THROW 52001,
              N'尚未套用 prerequisite Migration 1.7.0-004。',
              1;

    IF EXISTS(
        SELECT 1
        FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-005'
    )
        THROW 52002,
              N'Migration 1.7.0-005 已套用，不得重複執行。',
              1;

    IF OBJECT_ID(N'dbo.ImportBatches', N'U') IS NULL
        THROW 52003,
              N'找不到 dbo.ImportBatches。',
              1;

    IF NOT EXISTS(
        SELECT 1
        FROM sys.check_constraints
        WHERE
            parent_object_id =
                OBJECT_ID(N'dbo.ImportBatches')
            AND name =
                N'CK_ImportBatches_Status'
    )
        THROW 52004,
              N'找不到 CK_ImportBatches_Status，請由 IT Review。',
              1;

    IF EXISTS(
        SELECT 1
        FROM dbo.ImportBatches
        WHERE Status NOT IN(
            N'Previewed',
            N'Confirmed',
            N'PartiallyFailed'
        )
    )
        THROW 52005,
              N'ImportBatches 存在非預期 Status，請由 IT Review。',
              1;

    ALTER TABLE dbo.ImportBatches
        DROP CONSTRAINT CK_ImportBatches_Status;

    ALTER TABLE dbo.ImportBatches
        WITH CHECK
        ADD CONSTRAINT CK_ImportBatches_Status
        CHECK(
            Status IN(
                N'Previewed',
                N'Confirming',
                N'Confirmed',
                N'PartiallyFailed'
            )
        );

    ALTER TABLE dbo.ImportBatches
        CHECK CONSTRAINT CK_ImportBatches_Status;

    INSERT dbo.SchemaVersions
    (
        VersionNumber,
        Description,
        AppliedAt,
        AppliedBy
    )
    VALUES
    (
        N'1.7.0-005',
        N'Allow Confirming status for atomic People Bulk confirmation claim',
        SYSUTCDATETIME(),
        N'v1.7.0 People Bulk concurrency hardening'
    );

    COMMIT TRANSACTION;

    PRINT N'v1.7.0-005 People Bulk Atomic Confirm Claim completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
