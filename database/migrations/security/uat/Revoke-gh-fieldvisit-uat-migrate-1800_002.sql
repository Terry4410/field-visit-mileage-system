SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 53930, N'Permission revoke refused: target is not db-fieldvisit-uat.', 1;
IF USER_ID(N'gh-fieldvisit-uat-migrate') IS NULL
    THROW 53931, N'Permission revoke refused: principal is missing.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    REVOKE INSERT ON SCHEMA::dbo FROM [gh-fieldvisit-uat-migrate];
    REVOKE UPDATE ON OBJECT::dbo.UserIdentityProfiles FROM [gh-fieldvisit-uat-migrate];
    IF ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
        ALTER ROLE db_ddladmin DROP MEMBER [gh-fieldvisit-uat-migrate];
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
    THROW 53932, N'Permission revoke post-check failed: db_datareader baseline is missing.', 1;
IF ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) <> 0
    THROW 53933, N'Permission revoke post-check failed: db_ddladmin remains.', 1;
IF EXISTS
(
    SELECT 1
    FROM sys.database_permissions p
    WHERE p.grantee_principal_id = USER_ID(N'gh-fieldvisit-uat-migrate')
      AND ((p.class = 3 AND SCHEMA_NAME(p.major_id) = N'dbo'
            AND p.permission_name = N'INSERT')
        OR (p.class = 1 AND OBJECT_SCHEMA_NAME(p.major_id) = N'dbo'
            AND OBJECT_NAME(p.major_id) = N'UserIdentityProfiles'
            AND p.permission_name = N'UPDATE'))
)
    THROW 53934, N'Permission revoke post-check failed: Stage 002 explicit permission remains.', 1;

SELECT N'REVOKED_1800_002_ELEVATION' AS PermissionState,
       DB_NAME() AS DatabaseName,
       N'gh-fieldvisit-uat-migrate' AS DatabasePrincipal;
