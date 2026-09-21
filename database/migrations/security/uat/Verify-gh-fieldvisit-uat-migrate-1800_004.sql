SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 54120, N'Permission verification refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
    THROW 54121, N'Permission verification failed: principal is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_principals
    WHERE principal_id = @PrincipalId
      AND type_desc = N'EXTERNAL_USER'
)
    THROW 54122, N'Permission verification failed: principal is not EXTERNAL_USER.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    JOIN sys.database_principals r
      ON r.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = @PrincipalId
      AND r.name NOT IN (N'db_datareader', N'db_ddladmin')
)
    THROW 54123, N'Permission verification failed: unexpected role membership exists.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
   OR ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) <> 1
   OR ISNULL(IS_ROLEMEMBER(N'db_owner', N'gh-fieldvisit-uat-migrate'), 0) <> 0
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin', N'gh-fieldvisit-uat-migrate'), 0) <> 0
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter', N'gh-fieldvisit-uat-migrate'), 0) <> 0
    THROW 54124, N'Permission verification failed: role gate is not satisfied.', 1;

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
      )
)
    THROW 54125, N'Permission verification failed: unexpected explicit permission exists.', 1;

DECLARE
    @CanCreateTable INT,
    @CanInsertDboSchema INT,
    @CanInsertSchemaVersions INT,
    @CanAlterLocations INT,
    @CanAlterDeploymentAssignments INT,
    @CanReferenceUsers INT,
    @CanReferenceLocations INT,
    @CanReferenceTeams INT,
    @CanSelectRequired INT,
    @CanUpdateLocations INT,
    @CanUpdateOrganizations INT,
    @CanUpdateTeams INT,
    @CanUpdateStage002Profile INT;

BEGIN TRY
    EXECUTE AS USER = N'gh-fieldvisit-uat-migrate';

    SELECT
        @CanCreateTable = ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CREATE TABLE'), 0),
        @CanInsertDboSchema = ISNULL(HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'INSERT'), 0),
        @CanInsertSchemaVersions = ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions', N'OBJECT', N'INSERT'), 0),
        @CanAlterLocations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Locations', N'OBJECT', N'ALTER'), 0),
        @CanAlterDeploymentAssignments = ISNULL(HAS_PERMS_BY_NAME(N'dbo.DeploymentSiteLocationAssignments', N'OBJECT', N'ALTER'), 0),
        @CanReferenceUsers = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Users', N'OBJECT', N'REFERENCES'), 0),
        @CanReferenceLocations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Locations', N'OBJECT', N'REFERENCES'), 0),
        @CanReferenceTeams = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Teams', N'OBJECT', N'REFERENCES'), 0),
        @CanUpdateLocations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Locations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateOrganizations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Organizations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateTeams = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Teams', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateStage002Profile = ISNULL(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles', N'OBJECT', N'UPDATE'), 0),
        @CanSelectRequired =
            CASE
                WHEN ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions', N'OBJECT', N'SELECT'), 0) = 1
                 AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.Users', N'OBJECT', N'SELECT'), 0) = 1
                 AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.Locations', N'OBJECT', N'SELECT'), 0) = 1
                 AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.Teams', N'OBJECT', N'SELECT'), 0) = 1
                 AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.DeploymentSiteLocationAssignments', N'OBJECT', N'SELECT'), 0) = 1
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
   OR @CanAlterLocations <> 1
   OR @CanAlterDeploymentAssignments <> 1
   OR @CanReferenceUsers <> 1
   OR @CanReferenceLocations <> 1
   OR @CanReferenceTeams <> 1
   OR @CanSelectRequired <> 1
    THROW 54126, N'Permission verification failed: required 1800_004 capability is missing.', 1;

IF @CanUpdateLocations <> 0
   OR @CanUpdateOrganizations <> 0
   OR @CanUpdateTeams <> 0
   OR @CanUpdateStage002Profile <> 0
    THROW 54127, N'Permission verification failed: Stage 004 or cross-stage UPDATE capability remains effective.', 1;

SELECT N'PASS' AS VerifyStatus,
       N'1800_004_PERMISSION_GATE' AS GateName;
