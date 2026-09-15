SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 53920, N'Permission verification refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
    THROW 53921, N'Permission verification failed: principal is missing.', 1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.database_principals
    WHERE principal_id = @PrincipalId AND type_desc = N'EXTERNAL_USER'
)
    THROW 53922, N'Permission verification failed: principal is not EXTERNAL_USER.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = @PrincipalId
      AND r.name NOT IN (N'db_datareader', N'db_ddladmin')
)
    THROW 53923, N'Permission verification failed: unexpected role membership exists.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
   OR ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) <> 1
   OR ISNULL(IS_ROLEMEMBER(N'db_owner', N'gh-fieldvisit-uat-migrate'), 0) <> 0
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin', N'gh-fieldvisit-uat-migrate'), 0) <> 0
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter', N'gh-fieldvisit-uat-migrate'), 0) <> 0
    THROW 53924, N'Permission verification failed: role gate is not satisfied.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_permissions p
    WHERE p.grantee_principal_id = @PrincipalId
      AND NOT
      (
          (p.class = 0 AND p.permission_name = N'CONNECT' AND p.state = N'G')
          OR (p.class = 3 AND SCHEMA_NAME(p.major_id) = N'dbo'
              AND p.permission_name = N'INSERT' AND p.state = N'G')
          OR (p.class = 1 AND OBJECT_SCHEMA_NAME(p.major_id) = N'dbo'
              AND OBJECT_NAME(p.major_id) = N'UserIdentityProfiles'
              AND p.permission_name = N'UPDATE' AND p.state = N'G')
      )
)
    THROW 53925, N'Permission verification failed: unexpected explicit permission exists.', 1;

DECLARE
    @CanCreateTable INT,
    @CanInsertDboSchema INT,
    @CanInsertSchemaVersions INT,
    @CanUpdateProfile INT,
    @CanAlterProfile INT,
    @CanAlterTrips INT,
    @CanAlterSnapshots INT,
    @CanReferenceUsers INT,
    @CanReferenceOrganizations INT,
    @CanReferenceRoles INT,
    @CanReferenceTeams INT,
    @CanSelectTrips INT,
    @CanSelectSnapshots INT,
    @CanSelectStops INT;

BEGIN TRY
    EXECUTE AS USER = N'gh-fieldvisit-uat-migrate';
    SELECT
        @CanCreateTable = ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CREATE TABLE'), 0),
        @CanInsertDboSchema = ISNULL(HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'INSERT'), 0),
        @CanInsertSchemaVersions = ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions', N'OBJECT', N'INSERT'), 0),
        @CanUpdateProfile = ISNULL(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles', N'OBJECT', N'UPDATE'), 0),
        @CanAlterProfile = ISNULL(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles', N'OBJECT', N'ALTER'), 0),
        @CanAlterTrips = ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTrips', N'OBJECT', N'ALTER'), 0),
        @CanAlterSnapshots = ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTripSnapshots', N'OBJECT', N'ALTER'), 0),
        @CanReferenceUsers = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Users', N'OBJECT', N'REFERENCES'), 0),
        @CanReferenceOrganizations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Organizations', N'OBJECT', N'REFERENCES'), 0),
        @CanReferenceRoles = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Roles', N'OBJECT', N'REFERENCES'), 0),
        @CanReferenceTeams = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Teams', N'OBJECT', N'REFERENCES'), 0),
        @CanSelectTrips = ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTrips', N'OBJECT', N'SELECT'), 0),
        @CanSelectSnapshots = ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTripSnapshots', N'OBJECT', N'SELECT'), 0),
        @CanSelectStops = ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTripSnapshotStops', N'OBJECT', N'SELECT'), 0);
    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME() = N'gh-fieldvisit-uat-migrate' REVERT;
    THROW;
END CATCH;

IF @CanCreateTable <> 1 OR @CanInsertDboSchema <> 1 OR @CanInsertSchemaVersions <> 1
   OR @CanUpdateProfile <> 1 OR @CanAlterProfile <> 1 OR @CanAlterTrips <> 1
   OR @CanAlterSnapshots <> 1 OR @CanReferenceUsers <> 1
   OR @CanReferenceOrganizations <> 1 OR @CanReferenceRoles <> 1
   OR @CanReferenceTeams <> 1 OR @CanSelectTrips <> 1
   OR @CanSelectSnapshots <> 1 OR @CanSelectStops <> 1
    THROW 53926, N'Permission verification failed: required 1800_002 capability is missing.', 1;

SELECT N'PASS' AS VerifyStatus, N'1800_002_PERMISSION_GATE' AS GateName;
