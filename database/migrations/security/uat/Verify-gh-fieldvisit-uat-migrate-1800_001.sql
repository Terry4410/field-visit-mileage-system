SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 53810, N'Permission verification refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
    THROW 53811, N'Permission verification failed: gh-fieldvisit-uat-migrate is missing.', 1;

SELECT
    name AS PrincipalName,
    type_desc AS PrincipalType,
    authentication_type_desc AS AuthenticationType
FROM sys.database_principals
WHERE principal_id = @PrincipalId;

SELECT
    role_principal.name AS RoleName,
    member_principal.name AS MemberName
FROM sys.database_role_members AS drm
JOIN sys.database_principals AS role_principal
  ON role_principal.principal_id = drm.role_principal_id
JOIN sys.database_principals AS member_principal
  ON member_principal.principal_id = drm.member_principal_id
WHERE member_principal.principal_id = @PrincipalId
ORDER BY role_principal.name;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members AS drm
    JOIN sys.database_principals AS role_principal
      ON role_principal.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = @PrincipalId
      AND role_principal.name NOT IN (N'db_datareader', N'db_ddladmin')
)
    THROW 53813, N'Permission verification failed: unexpected database role membership exists.', 1;

SELECT
    permission.state_desc AS PermissionState,
    permission.permission_name AS PermissionName,
    permission.class_desc AS PermissionClass,
    CASE permission.class
        WHEN 0 THEN DB_NAME()
        WHEN 1 THEN QUOTENAME(OBJECT_SCHEMA_NAME(permission.major_id)) + N'.' + QUOTENAME(OBJECT_NAME(permission.major_id))
        WHEN 3 THEN QUOTENAME(SCHEMA_NAME(permission.major_id))
        ELSE CONVERT(NVARCHAR(128), permission.major_id)
    END AS SecurableName
FROM sys.database_permissions AS permission
WHERE permission.grantee_principal_id = @PrincipalId
ORDER BY permission.class_desc, SecurableName, permission.permission_name;

IF EXISTS
(
    SELECT 1
    FROM sys.database_permissions AS permission
    WHERE permission.grantee_principal_id = @PrincipalId
      AND NOT
      (
          (permission.class = 0 AND permission.permission_name = N'CONNECT' AND permission.state = N'G')
          OR (permission.class = 3
              AND SCHEMA_NAME(permission.major_id) = N'dbo'
              AND permission.permission_name = N'INSERT'
              AND permission.state = N'G')
          OR (permission.class = 1
              AND OBJECT_SCHEMA_NAME(permission.major_id) = N'dbo'
              AND OBJECT_NAME(permission.major_id) IN (N'Organizations', N'Teams')
              AND permission.permission_name = N'UPDATE'
              AND permission.state = N'G')
      )
)
    THROW 53814, N'Permission verification failed: unexpected explicit permission exists.', 1;

DECLARE
    @IsExternalUser INT,
    @IsDataReader INT,
    @IsDdlAdmin INT,
    @IsOwner INT,
    @IsSecurityAdmin INT,
    @IsDataWriter INT,
    @CanCreateTable INT,
    @CanInsertDboSchema INT,
    @CanInsertSchemaVersions INT,
    @CanUpdateOrganizations INT,
    @CanUpdateTeams INT,
    @CanAlterOrganizations INT,
    @CanAlterTeams INT,
    @CanAlterSnapshots INT,
    @CanReferenceUsers INT,
    @CanSelectTrips INT,
    @IsPublicMember INT;

SELECT @IsExternalUser = CASE WHEN type_desc = N'EXTERNAL_USER' THEN 1 ELSE 0 END
FROM sys.database_principals
WHERE principal_id = @PrincipalId;

BEGIN TRY
    EXECUTE AS USER = N'gh-fieldvisit-uat-migrate';

    SELECT
        @IsDataReader = ISNULL(IS_ROLEMEMBER(N'db_datareader'), 0),
        @IsDdlAdmin = ISNULL(IS_ROLEMEMBER(N'db_ddladmin'), 0),
        @IsOwner = ISNULL(IS_ROLEMEMBER(N'db_owner'), 0),
        @IsSecurityAdmin = ISNULL(IS_ROLEMEMBER(N'db_securityadmin'), 0),
        @IsDataWriter = ISNULL(IS_ROLEMEMBER(N'db_datawriter'), 0),
        @CanCreateTable = ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CREATE TABLE'), 0),
        @CanInsertDboSchema = ISNULL(HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'INSERT'), 0),
        @CanInsertSchemaVersions = ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions', N'OBJECT', N'INSERT'), 0),
        @CanUpdateOrganizations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Organizations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateTeams = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Teams', N'OBJECT', N'UPDATE'), 0),
        @CanAlterOrganizations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Organizations', N'OBJECT', N'ALTER'), 0),
        @CanAlterTeams = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Teams', N'OBJECT', N'ALTER'), 0),
        @CanAlterSnapshots = ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTripSnapshots', N'OBJECT', N'ALTER'), 0),
        @CanReferenceUsers = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Users', N'OBJECT', N'REFERENCES'), 0),
        @CanSelectTrips = ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTrips', N'OBJECT', N'SELECT'), 0),
        @IsPublicMember = ISNULL(IS_ROLEMEMBER(N'public'), 0);

    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME() = N'gh-fieldvisit-uat-migrate' REVERT;
    THROW;
END CATCH;

SELECT
    @IsExternalUser AS IsExternalUser,
    @IsDataReader AS IsDataReader,
    @IsDdlAdmin AS IsDdlAdmin,
    @IsOwner AS IsOwner,
    @IsSecurityAdmin AS IsSecurityAdmin,
    @IsDataWriter AS IsDataWriter,
    @CanCreateTable AS CanCreateTable,
    @CanInsertDboSchema AS CanInsertDboSchema,
    @CanInsertSchemaVersions AS CanInsertSchemaVersions,
    @CanUpdateOrganizations AS CanUpdateOrganizations,
    @CanUpdateTeams AS CanUpdateTeams,
    @CanAlterOrganizations AS CanAlterOrganizations,
    @CanAlterTeams AS CanAlterTeams,
    @CanAlterSnapshots AS CanAlterVisitTripSnapshots,
    @CanReferenceUsers AS CanReferenceUsers,
    @CanSelectTrips AS CanSelectHistoricalTrips,
    @IsPublicMember AS CanUseSpGetAppLockDefaultPublicPrincipal;

IF @IsExternalUser <> 1
   OR @IsDataReader <> 1
   OR @IsDdlAdmin <> 1
   OR @IsOwner <> 0
   OR @IsSecurityAdmin <> 0
   OR @IsDataWriter <> 0
   OR @CanCreateTable <> 1
   OR @CanInsertDboSchema <> 1
   OR @CanInsertSchemaVersions <> 1
   OR @CanUpdateOrganizations <> 1
   OR @CanUpdateTeams <> 1
   OR @CanAlterOrganizations <> 1
   OR @CanAlterTeams <> 1
   OR @CanAlterSnapshots <> 1
   OR @CanReferenceUsers <> 1
   OR @CanSelectTrips <> 1
   OR @IsPublicMember <> 1
    THROW 53812, N'Permission verification failed: required least-privilege state is not satisfied.', 1;

SELECT N'PASS' AS VerifyStatus, N'1800_001_PERMISSION_GATE' AS GateName;
