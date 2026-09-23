SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 54320, N'Permission verification refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
    THROW 54321, N'Permission verification failed: principal is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_principals
    WHERE principal_id = @PrincipalId
      AND type_desc = N'EXTERNAL_USER'
)
    THROW 54322, N'Permission verification failed: principal is not EXTERNAL_USER.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    JOIN sys.database_principals r
      ON r.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = @PrincipalId
      AND r.name NOT IN (N'db_datareader', N'db_ddladmin')
)
    THROW 54323, N'Permission verification failed: unexpected role membership exists.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
   OR ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) <> 1
   OR ISNULL(IS_ROLEMEMBER(N'db_owner', N'gh-fieldvisit-uat-migrate'), 0) <> 0
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin', N'gh-fieldvisit-uat-migrate'), 0) <> 0
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter', N'gh-fieldvisit-uat-migrate'), 0) <> 0
    THROW 54324, N'Permission verification failed: role gate is not satisfied.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_permissions p
    WHERE p.grantee_principal_id = @PrincipalId
      AND NOT
      (
          (p.class = 0
           AND p.permission_name = N'CONNECT'
           AND p.state = N'G')
          OR
          (p.class = 3
           AND SCHEMA_NAME(p.major_id) = N'dbo'
           AND p.permission_name = N'INSERT'
           AND p.state = N'G')
          OR
          (p.class = 1
           AND OBJECT_SCHEMA_NAME(p.major_id) = N'dbo'
           AND OBJECT_NAME(p.major_id) = N'Employments'
           AND p.permission_name = N'UPDATE'
           AND p.state = N'G')
      )
)
    THROW 54325, N'Permission verification failed: unexpected explicit permission exists.', 1;

DECLARE
    @CanCreateTable INT,
    @CanInsertDboSchema INT,
    @CanInsertSchemaVersions INT,
    @CanAlterEmployments INT,
    @CanReferenceUsers INT,
    @CanReferenceEmployments INT,
    @CanUpdateEmployments INT,
    @CanUpdateProjects INT,
    @CanUpdateVisitTypes INT,
    @CanUpdateMileageRateRules INT,
    @CanUpdateOrganizations INT,
    @CanUpdateTeams INT,
    @CanUpdateUserIdentityProfiles INT,
    @CanUpdateLocations INT,
    @CanUpdateDeploymentAssignments INT,
    @CanSelectRequired INT;

BEGIN TRY
    EXECUTE AS USER = N'gh-fieldvisit-uat-migrate';

    SELECT
        @CanCreateTable = ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CREATE TABLE'), 0),
        @CanInsertDboSchema = ISNULL(HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'INSERT'), 0),
        @CanInsertSchemaVersions = ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions', N'OBJECT', N'INSERT'), 0),
        @CanAlterEmployments = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Employments', N'OBJECT', N'ALTER'), 0),
        @CanReferenceUsers = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Users', N'OBJECT', N'REFERENCES'), 0),
        @CanReferenceEmployments = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Employments', N'OBJECT', N'REFERENCES'), 0),
        @CanUpdateEmployments = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Employments', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateProjects = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Projects', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateVisitTypes = ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTypes', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateMileageRateRules = ISNULL(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateOrganizations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Organizations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateTeams = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Teams', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateUserIdentityProfiles = ISNULL(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateLocations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Locations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateDeploymentAssignments = ISNULL(HAS_PERMS_BY_NAME(N'dbo.DeploymentSiteLocationAssignments', N'OBJECT', N'UPDATE'), 0),
        @CanSelectRequired =
            CASE
                WHEN ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions', N'OBJECT', N'SELECT'), 0) = 1
                 AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.Users', N'OBJECT', N'SELECT'), 0) = 1
                 AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.Employments', N'OBJECT', N'SELECT'), 0) = 1
                 AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTrips', N'OBJECT', N'SELECT'), 0) = 1
                 AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTripSnapshots', N'OBJECT', N'SELECT'), 0) = 1
                 AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTripSnapshotStops', N'OBJECT', N'SELECT'), 0) = 1
                THEN 1
                ELSE 0
            END;

    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME() = N'gh-fieldvisit-uat-migrate' REVERT;
    THROW;
END CATCH;

IF @CanCreateTable <> 1
   OR @CanInsertDboSchema <> 1
   OR @CanInsertSchemaVersions <> 1
   OR @CanAlterEmployments <> 1
   OR @CanReferenceUsers <> 1
   OR @CanReferenceEmployments <> 1
   OR @CanUpdateEmployments <> 1
   OR @CanSelectRequired <> 1
    THROW 54326, N'Permission verification failed: required 1800_006 capability is missing.', 1;

IF @CanUpdateProjects <> 0
   OR @CanUpdateVisitTypes <> 0
   OR @CanUpdateMileageRateRules <> 0
   OR @CanUpdateOrganizations <> 0
   OR @CanUpdateTeams <> 0
   OR @CanUpdateUserIdentityProfiles <> 0
   OR @CanUpdateLocations <> 0
   OR @CanUpdateDeploymentAssignments <> 0
    THROW 54327, N'Permission verification failed: forbidden prior-stage UPDATE capability remains.', 1;

SELECT N'PASS' AS VerifyStatus,
       N'1800_006_PERMISSION_GATE' AS GateName;
