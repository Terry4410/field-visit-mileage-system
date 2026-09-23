SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 54430, N'Permission revoke refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
    THROW 54431, N'Permission revoke refused: principal is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_principals
    WHERE principal_id = @PrincipalId
      AND type_desc = N'EXTERNAL_USER'
)
    THROW 54432, N'Permission revoke refused: principal is not EXTERNAL_USER.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    REVOKE UPDATE ON OBJECT::dbo.MileageCalculations FROM [gh-fieldvisit-uat-migrate];
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
    THROW 54433, N'Permission revoke post-check failed: db_datareader baseline is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) <> 0
    THROW 54434, N'Permission revoke post-check failed: db_ddladmin remains.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_owner', N'gh-fieldvisit-uat-migrate'), 0) <> 0
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin', N'gh-fieldvisit-uat-migrate'), 0) <> 0
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter', N'gh-fieldvisit-uat-migrate'), 0) <> 0
    THROW 54435, N'Permission revoke post-check failed: forbidden broad role exists.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    JOIN sys.database_principals r
      ON r.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = @PrincipalId
      AND r.name <> N'db_datareader'
)
    THROW 54436, N'Permission revoke post-check failed: unexpected role membership remains.', 1;

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
    THROW 54437, N'Permission revoke post-check failed: explicit baseline is not CONNECT only.', 1;

DECLARE
    @CanInsertDboSchema INT,
    @CanUpdateMileageCalculations INT,
    @CanUpdateOrganizations INT,
    @CanUpdateTeams INT,
    @CanUpdateUserIdentityProfiles INT,
    @CanUpdateLocations INT,
    @CanUpdateDeploymentAssignments INT,
    @CanUpdateProjects INT,
    @CanUpdateVisitTypes INT,
    @CanUpdateMileageRateRules INT,
    @CanUpdateEmployments INT,
    @CanDeleteDatabase INT,
    @CanDeleteDboSchema INT;

BEGIN TRY
    EXECUTE AS USER = N'gh-fieldvisit-uat-migrate';

    SELECT
        @CanInsertDboSchema = ISNULL(HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'INSERT'), 0),
        @CanUpdateMileageCalculations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.MileageCalculations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateOrganizations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Organizations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateTeams = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Teams', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateUserIdentityProfiles = ISNULL(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateLocations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Locations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateDeploymentAssignments = ISNULL(HAS_PERMS_BY_NAME(N'dbo.DeploymentSiteLocationAssignments', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateProjects = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Projects', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateVisitTypes = ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTypes', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateMileageRateRules = ISNULL(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateEmployments = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Employments', N'OBJECT', N'UPDATE'), 0),
        @CanDeleteDatabase = ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'DELETE'), 0),
        @CanDeleteDboSchema = ISNULL(HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'DELETE'), 0);

    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME() = N'gh-fieldvisit-uat-migrate' REVERT;
    THROW;
END CATCH;

IF @CanInsertDboSchema <> 0
   OR @CanUpdateMileageCalculations <> 0
   OR @CanUpdateOrganizations <> 0
   OR @CanUpdateTeams <> 0
   OR @CanUpdateUserIdentityProfiles <> 0
   OR @CanUpdateLocations <> 0
   OR @CanUpdateDeploymentAssignments <> 0
   OR @CanUpdateProjects <> 0
   OR @CanUpdateVisitTypes <> 0
   OR @CanUpdateMileageRateRules <> 0
   OR @CanUpdateEmployments <> 0
   OR @CanDeleteDatabase <> 0
   OR @CanDeleteDboSchema <> 0
    THROW 54438, N'Permission revoke post-check failed: Stage 007, prior-stage UPDATE, or DELETE capability remains.', 1;

SELECT N'REVOKED_1800_007_ELEVATION' AS PermissionState,
       DB_NAME() AS DatabaseName,
       N'gh-fieldvisit-uat-migrate' AS DatabasePrincipal;
