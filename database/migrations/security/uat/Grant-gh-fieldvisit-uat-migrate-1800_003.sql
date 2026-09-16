SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Reviewed preparation script only. Run separately in an approved window.
IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 54000, N'Permission grant refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.database_principals
       WHERE principal_id = @PrincipalId
         AND type_desc = N'EXTERNAL_USER'
   )
    THROW 54001, N'Permission grant refused: expected EXTERNAL_USER is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
    THROW 54002, N'Permission grant refused: retained db_datareader baseline is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_owner', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter', N'gh-fieldvisit-uat-migrate'), 0) = 1
    THROW 54003, N'Permission grant refused: forbidden elevated or broad role exists.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    JOIN sys.database_principals r
      ON r.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = @PrincipalId
      AND r.name <> N'db_datareader'
)
    THROW 54004, N'Permission grant refused: unexpected role membership exists.', 1;

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
    THROW 54005, N'Permission grant refused: unexpected explicit permission state exists.', 1;

IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Users', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Centers', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Locations', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Teams', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Employments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.TeamCenterAssignments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTrips', N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTripSnapshots', N'U') IS NULL
    THROW 54006, N'Permission grant refused: required 1.8.0-003 predecessor objects are missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.SchemaVersions
    WHERE VersionNumber = N'1.8.0-002'
)
    THROW 54007, N'Permission grant refused: exact predecessor 1.8.0-002 is missing.', 1;

IF
(
    SELECT TOP (1) VersionNumber
    FROM dbo.SchemaVersions
    ORDER BY AppliedAt DESC, VersionNumber DESC
) <> N'1.8.0-002'
    THROW 54008,
        N'Permission grant refused: latest SchemaVersion is not exact predecessor 1.8.0-002.',
        1;

IF EXISTS
(
    SELECT 1
    FROM dbo.SchemaVersions
    WHERE VersionNumber = N'1.8.0-003'
)
    THROW 54009, N'Permission grant refused: target 1.8.0-003 already exists.', 1;

IF OBJECT_ID(N'dbo.DeploymentSites', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.DeploymentSiteLocationAssignments', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.TeamDeploymentSiteAssignments', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.EmploymentDeploymentSiteAssignments', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.TR_TeamCenterAssignments_ProtectTeamSites', N'TR') IS NOT NULL
    THROW 54010, N'Permission grant refused: 1.8.0-003 partial object state exists.', 1;

IF COL_LENGTH(N'dbo.VisitTrips', N'StartDeploymentSiteId') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTrips', N'EndDeploymentSiteId') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'StartDeploymentSiteIdSnapshot') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'EndDeploymentSiteIdSnapshot') IS NOT NULL
    THROW 54011, N'Permission grant refused: 1.8.0-003 partial column state exists.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    ALTER ROLE db_ddladmin ADD MEMBER [gh-fieldvisit-uat-migrate];
    GRANT INSERT ON SCHEMA::dbo TO [gh-fieldvisit-uat-migrate];
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT N'GRANT_PREPARED_FOR_1800_003' AS PermissionState,
       DB_NAME() AS DatabaseName,
       N'gh-fieldvisit-uat-migrate' AS DatabasePrincipal;
