SET NOCOUNT ON;
SET XACT_ABORT ON;

-- REVIEWED PREPARATION SCRIPT ONLY. A DBA must run this separately in an
-- approved maintenance window. The migration workflow must never run it.

IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 53800, N'Permission grant refused: target is not db-fieldvisit-uat.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_principals
    WHERE name = N'gh-fieldvisit-uat-migrate'
      AND type_desc = N'EXTERNAL_USER'
)
    THROW 53801, N'Permission grant refused: expected EXTERNAL_USER gh-fieldvisit-uat-migrate is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
    THROW 53802, N'Permission grant refused: existing db_datareader baseline is missing.', 1;

IF IS_ROLEMEMBER(N'db_owner', N'gh-fieldvisit-uat-migrate') = 1
   OR IS_ROLEMEMBER(N'db_securityadmin', N'gh-fieldvisit-uat-migrate') = 1
   OR IS_ROLEMEMBER(N'db_datawriter', N'gh-fieldvisit-uat-migrate') = 1
    THROW 53803, N'Permission grant refused: migration principal already has a forbidden broad role.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members AS drm
    JOIN sys.database_principals AS role_principal
      ON role_principal.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = USER_ID(N'gh-fieldvisit-uat-migrate')
      AND role_principal.name NOT IN (N'db_datareader', N'db_ddladmin')
)
    THROW 53805, N'Permission grant refused: migration principal has an unexpected database role.', 1;

IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Organizations', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Teams', N'U') IS NULL
    THROW 53804, N'Permission grant refused: required v1.7.2 tables are missing.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) <> 1
        ALTER ROLE db_ddladmin ADD MEMBER [gh-fieldvisit-uat-migrate];

    -- Required because SchemaMigrationDataBaselines is created and populated
    -- atomically by Up.sql, so it cannot receive an object grant in advance.
    GRANT INSERT ON SCHEMA::dbo TO [gh-fieldvisit-uat-migrate];

    -- Adding ROWVERSION NOT NULL materializes values for existing rows.
    GRANT UPDATE ON OBJECT::dbo.Organizations TO [gh-fieldvisit-uat-migrate];
    GRANT UPDATE ON OBJECT::dbo.Teams TO [gh-fieldvisit-uat-migrate];

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT
    N'GRANT_PREPARED_FOR_1800_001' AS PermissionState,
    DB_NAME() AS DatabaseName,
    N'gh-fieldvisit-uat-migrate' AS DatabasePrincipal,
    N'Run Verify-gh-fieldvisit-uat-migrate-1800_001.sql before any migration dispatch.' AS NextGate;
