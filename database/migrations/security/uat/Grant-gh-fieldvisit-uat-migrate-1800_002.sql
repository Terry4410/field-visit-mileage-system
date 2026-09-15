SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Reviewed preparation script only. Run separately in an approved window.
IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 53900, N'Permission grant refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
   OR NOT EXISTS
   (
       SELECT 1 FROM sys.database_principals
       WHERE principal_id = @PrincipalId AND type_desc = N'EXTERNAL_USER'
   )
    THROW 53901, N'Permission grant refused: expected EXTERNAL_USER is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
    THROW 53902, N'Permission grant refused: retained db_datareader baseline is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_owner', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter', N'gh-fieldvisit-uat-migrate'), 0) = 1
    THROW 53903, N'Permission grant refused: forbidden broad role exists.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = @PrincipalId
      AND r.name <> N'db_datareader'
)
    THROW 53904, N'Permission grant refused: unexpected role membership exists.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_permissions p
    WHERE p.grantee_principal_id = @PrincipalId
      AND NOT (p.class = 0 AND p.permission_name = N'CONNECT' AND p.state = N'G')
)
    THROW 53905, N'Permission grant refused: unexpected explicit permission state exists.', 1;

IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Users', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Organizations', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Teams', N'U') IS NULL
   OR OBJECT_ID(N'dbo.UserIdentityProfiles', N'U') IS NULL
   OR OBJECT_ID(N'dbo.UserEmploymentPeriods', N'U') IS NULL
   OR OBJECT_ID(N'dbo.UserRoleAssignments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.UserTeamAssignments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Roles', N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTrips', N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTripSnapshots', N'U') IS NULL
    THROW 53906, N'Permission grant refused: required predecessor objects are missing.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-001')
    THROW 53907, N'Permission grant refused: exact predecessor 1.8.0-001 is missing.', 1;
IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-002')
    THROW 53908, N'Permission grant refused: target 1.8.0-002 already exists.', 1;

IF OBJECT_ID(N'dbo.Persons', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.Employments', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.EmploymentStatusPeriods', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.EmploymentRoleAssignments', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.TeamMemberships', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.TeamLeaderAssignments', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.TeamLeaderDelegations', N'U') IS NOT NULL
    THROW 53909, N'Permission grant refused: 1800_002 partial tables exist.', 1;

IF COL_LENGTH(N'dbo.UserIdentityProfiles', N'EmploymentId') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTrips', N'EmploymentId') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'PersonIdSnapshot') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'EmploymentIdSnapshot') IS NOT NULL
    THROW 53910, N'Permission grant refused: 1800_002 partial columns exist.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    ALTER ROLE db_ddladmin ADD MEMBER [gh-fieldvisit-uat-migrate];
    GRANT INSERT ON SCHEMA::dbo TO [gh-fieldvisit-uat-migrate];
    GRANT UPDATE ON OBJECT::dbo.UserIdentityProfiles TO [gh-fieldvisit-uat-migrate];
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT N'GRANT_PREPARED_FOR_1800_002' AS PermissionState,
       DB_NAME() AS DatabaseName,
       N'gh-fieldvisit-uat-migrate' AS DatabasePrincipal;
