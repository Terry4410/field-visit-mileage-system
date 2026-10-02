SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 54660, N'Permission verification refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
   OR NOT EXISTS(SELECT 1 FROM sys.database_principals WHERE principal_id=@PrincipalId AND type_desc=N'EXTERNAL_USER')
    THROW 54661, N'Permission verification failed: expected EXTERNAL_USER is missing.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.database_role_members drm
    JOIN sys.database_principals r ON r.principal_id=drm.role_principal_id
    WHERE drm.member_principal_id=@PrincipalId AND r.name NOT IN(N'db_datareader',N'db_ddladmin')
)
    THROW 54662, N'Permission verification failed: unexpected role membership exists.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader',N'gh-fieldvisit-uat-migrate'),0)<>1
   OR ISNULL(IS_ROLEMEMBER(N'db_ddladmin',N'gh-fieldvisit-uat-migrate'),0)<>1
   OR ISNULL(IS_ROLEMEMBER(N'db_owner',N'gh-fieldvisit-uat-migrate'),0)<>0
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin',N'gh-fieldvisit-uat-migrate'),0)<>0
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter',N'gh-fieldvisit-uat-migrate'),0)<>0
    THROW 54663, N'Permission verification failed: role gate is not satisfied.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.database_permissions p
    WHERE p.grantee_principal_id=@PrincipalId
      AND NOT(
        (p.class=0 AND p.permission_name=N'CONNECT' AND p.state=N'G')
        OR (p.class=3 AND SCHEMA_NAME(p.major_id)=N'dbo' AND p.permission_name=N'INSERT' AND p.state=N'G')
      )
)
    THROW 54664, N'Permission verification failed: unexpected explicit permission exists.', 1;

DECLARE
    @CanCreateTable INT,
    @CanInsertDboSchema INT,
    @CanInsertSchemaVersions INT,
    @CanAlterMileageRateRules INT,
    @CanSelectRequired INT,
    @CanUpdateMileageRateRules INT,
    @CanDeleteDatabase INT,
    @CanDeleteDboSchema INT;

BEGIN TRY
    EXECUTE AS USER = N'gh-fieldvisit-uat-migrate';
    SELECT
        @CanCreateTable = ISNULL(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CREATE TABLE'),0),
        @CanInsertDboSchema = ISNULL(HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'INSERT'),0),
        @CanInsertSchemaVersions = ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'INSERT'),0),
        @CanAlterMileageRateRules = ISNULL(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'ALTER'),0),
        @CanSelectRequired = CASE WHEN
            ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'SELECT'),0)=1
            AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'SELECT'),0)=1
            AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTrips',N'OBJECT',N'SELECT'),0)=1
            AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTripSnapshots',N'OBJECT',N'SELECT'),0)=1
            THEN 1 ELSE 0 END,
        @CanUpdateMileageRateRules = ISNULL(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'UPDATE'),0),
        @CanDeleteDatabase = ISNULL(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'DELETE'),0),
        @CanDeleteDboSchema = ISNULL(HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'DELETE'),0);
    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME()=N'gh-fieldvisit-uat-migrate' REVERT;
    THROW;
END CATCH;

IF @CanCreateTable<>1
   OR @CanInsertDboSchema<>1
   OR @CanInsertSchemaVersions<>1
   OR @CanAlterMileageRateRules<>1
   OR @CanSelectRequired<>1
    THROW 54665, N'Permission verification failed: required 1800_008 capability is missing.', 1;

IF @CanUpdateMileageRateRules<>0 OR @CanDeleteDatabase<>0 OR @CanDeleteDboSchema<>0
    THROW 54666, N'Permission verification failed: forbidden UPDATE or DELETE capability exists.', 1;

SELECT N'PASS' AS VerifyStatus, N'1800_008_PERMISSION_GATE' AS GateName;
