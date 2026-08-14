SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    /* ================================================================
       v1.7.0-003 Authentication / People Administration Foundation

       Add Microsoft Entra ID stable identity binding to the existing
       v1.7 UserIdentityProfiles layer.

       IMPORTANT:
       - This migration does NOT change legacy Users/UserRoles.
       - It does NOT enable Entra authentication by itself.
       - Demo authentication remains available at application level.
       ================================================================ */

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
        THROW 51800,
              N'找不到 dbo.SchemaVersions，無法確認 Migration 順序。',
              1;

    IF NOT EXISTS(
        SELECT 1
        FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-002'
    )
        THROW 51801,
              N'尚未套用 prerequisite Migration 1.7.0-002。',
              1;

    IF EXISTS(
        SELECT 1
        FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-003'
    )
        THROW 51802,
              N'Migration 1.7.0-003 已套用，不得重複執行。',
              1;

    IF OBJECT_ID(N'dbo.UserIdentityProfiles', N'U') IS NULL
        THROW 51803,
              N'找不到 dbo.UserIdentityProfiles。',
              1;

    /* A partially applied/manual schema is intentionally blocked. */
    IF COL_LENGTH(
           N'dbo.UserIdentityProfiles',
           N'EntraTenantId'
       ) IS NOT NULL
       OR
       COL_LENGTH(
           N'dbo.UserIdentityProfiles',
           N'EntraObjectId'
       ) IS NOT NULL
        THROW 51804,
              N'偵測到 Entra identity 欄位已存在，請由 IT Review，不得直接覆蓋。',
              1;

    ALTER TABLE dbo.UserIdentityProfiles
        ADD
            EntraTenantId UNIQUEIDENTIFIER NULL,
            EntraObjectId UNIQUEIDENTIFIER NULL;

    ALTER TABLE dbo.UserIdentityProfiles
        ADD CONSTRAINT CK_UserIdentityProfiles_EntraBindingPair
        CHECK(
            (
                EntraTenantId IS NULL
                AND EntraObjectId IS NULL
            )
            OR
            (
                EntraTenantId IS NOT NULL
                AND EntraObjectId IS NOT NULL
            )
        );

    CREATE UNIQUE INDEX
        UX_UserIdentityProfiles_EntraIdentity
        ON dbo.UserIdentityProfiles(
            EntraTenantId,
            EntraObjectId
        )
        WHERE
            EntraTenantId IS NOT NULL
            AND EntraObjectId IS NOT NULL;

    INSERT dbo.SchemaVersions
    (
        VersionNumber,
        Description,
        AppliedAt,
        AppliedBy
    )
    VALUES
    (
        N'1.7.0-003',
        N'Authentication and people administration foundation: Microsoft Entra tenant/object identity binding',
        SYSUTCDATETIME(),
        N'v1.7.0 Auth People B1'
    );

    COMMIT TRANSACTION;

    PRINT N'v1.7.0-003 Authentication / People Foundation Migration completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
