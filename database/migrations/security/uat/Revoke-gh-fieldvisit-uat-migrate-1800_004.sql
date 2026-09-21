SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 54130, N'Permission revoke refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
    THROW 54131, N'Permission revoke refused: principal is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_principals
    WHERE principal_id = @PrincipalId
      AND type_desc = N'EXTERNAL_USER'
)
    THROW 54132, N'Permission revoke refused: principal is not EXTERNAL_USER.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    REVOKE INSERT ON SCHEMA::dbo FROM [gh-fieldvisit-uat-migrate];

    IF ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
        ALTER ROLE db_ddladmin DROP MEMBER [gh-fieldvisit-uat-migrate];

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
    THROW 54133, N'Permission revoke post-check failed: db_datareader baseline is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) <> 0
    THROW 54134, N'Permission revoke post-check failed: db_ddladmin remains.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_owner', N'gh-fieldvisit-uat-migrate'), 0) <> 0
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin', N'gh-fieldvisit-uat-migrate'), 0) <> 0
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter', N'gh-fieldvisit-uat-migrate'), 0) <> 0
    THROW 54135, N'Permission revoke post-check failed: forbidden broad role exists.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    JOIN sys.database_principals r
      ON r.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = @PrincipalId
      AND r.name <> N'db_datareader'
)
    THROW 54136, N'Permission revoke post-check failed: unexpected role membership remains.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_permissions p
    WHERE p.grantee_principal_id = @PrincipalId
      AND NOT
      (
          p.class = 0
          AND p.permission_name = N'CONNECT'
          AND p.state = N'G'
      )
)
    THROW 54137, N'Permission revoke post-check failed: unexpected explicit permission remains.', 1;

DECLARE
    @CanUpdateLocations INT,
    @CanUpdateOrganizations INT,
    @CanUpdateTeams INT,
    @CanUpdateStage002Profile INT;

BEGIN TRY
    EXECUTE AS USER = N'gh-fieldvisit-uat-migrate';

    SELECT
        @CanUpdateLocations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Locations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateOrganizations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Organizations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateTeams = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Teams', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateStage002Profile = ISNULL(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles', N'OBJECT', N'UPDATE'), 0);

    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME() = N'gh-fieldvisit-uat-migrate' REVERT;
    THROW;
END CATCH;

IF @CanUpdateLocations <> 0
   OR @CanUpdateOrganizations <> 0
   OR @CanUpdateTeams <> 0
   OR @CanUpdateStage002Profile <> 0
    THROW 54138, N'Permission revoke post-check failed: Stage 004 or cross-stage UPDATE capability remains.', 1;

SELECT N'REVOKED_1800_004_ELEVATION' AS PermissionState,
       DB_NAME() AS DatabaseName,
       N'gh-fieldvisit-uat-migrate' AS DatabasePrincipal;
