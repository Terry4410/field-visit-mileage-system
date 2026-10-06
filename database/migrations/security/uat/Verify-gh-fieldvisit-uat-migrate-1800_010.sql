SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 55160, N'Permission verification refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
   OR NOT EXISTS(SELECT 1 FROM sys.database_principals WHERE principal_id=@PrincipalId AND type_desc=N'EXTERNAL_USER')
    THROW 55161, N'Permission verification failed: expected EXTERNAL_USER is missing.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.database_role_members drm
    JOIN sys.database_principals r ON r.principal_id=drm.role_principal_id
    WHERE drm.member_principal_id=@PrincipalId AND r.name<>N'db_datareader'
)
    THROW 55162, N'Permission verification failed: unexpected role membership exists.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader',N'gh-fieldvisit-uat-migrate'),0)<>1
   OR ISNULL(IS_ROLEMEMBER(N'db_ddladmin',N'gh-fieldvisit-uat-migrate'),0)<>0
   OR ISNULL(IS_ROLEMEMBER(N'db_owner',N'gh-fieldvisit-uat-migrate'),0)<>0
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin',N'gh-fieldvisit-uat-migrate'),0)<>0
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter',N'gh-fieldvisit-uat-migrate'),0)<>0
    THROW 55163, N'Permission verification failed: role gate is not satisfied.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.database_permissions p
    WHERE p.grantee_principal_id=@PrincipalId
      AND NOT
      (
        (p.class=0 AND p.permission_name=N'CONNECT' AND p.state=N'G')
        OR
        (p.class=1 AND p.major_id=OBJECT_ID(N'dbo.RouteCalculationAttempts')
         AND p.permission_name=N'ALTER' AND p.state=N'G')
        OR
        (p.class=1 AND p.major_id=OBJECT_ID(N'dbo.SchemaVersions')
         AND p.permission_name=N'INSERT' AND p.state=N'G')
      )
)
    THROW 55164, N'Permission verification failed: unexpected explicit permission exists.', 1;

DECLARE
    @CanAlterRouteCalculationAttempts INT,
    @CanInsertSchemaVersions INT,
    @CanSelectRequired INT,
    @CanUpdateRouteCalculationAttempts INT,
    @CanDeleteDatabase INT,
    @CanDeleteDboSchema INT;

BEGIN TRY
    EXECUTE AS USER = N'gh-fieldvisit-uat-migrate';
    SELECT
        @CanAlterRouteCalculationAttempts = ISNULL(HAS_PERMS_BY_NAME(N'dbo.RouteCalculationAttempts',N'OBJECT',N'ALTER'),0),
        @CanInsertSchemaVersions = ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'INSERT'),0),
        @CanSelectRequired = CASE WHEN
            ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'SELECT'),0)=1
            AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.RouteCalculationAttempts',N'OBJECT',N'SELECT'),0)=1
            AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTrips',N'OBJECT',N'SELECT'),0)=1
            AND ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTripSnapshots',N'OBJECT',N'SELECT'),0)=1
            THEN 1 ELSE 0 END,
        @CanUpdateRouteCalculationAttempts = ISNULL(HAS_PERMS_BY_NAME(N'dbo.RouteCalculationAttempts',N'OBJECT',N'UPDATE'),0),
        @CanDeleteDatabase = ISNULL(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'DELETE'),0),
        @CanDeleteDboSchema = ISNULL(HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'DELETE'),0);
    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME()=N'gh-fieldvisit-uat-migrate' REVERT;
    THROW;
END CATCH;

IF @CanAlterRouteCalculationAttempts<>1
   OR @CanInsertSchemaVersions<>1
   OR @CanSelectRequired<>1
    THROW 55165, N'Permission verification failed: required 1800_010 capability is missing.', 1;

IF @CanUpdateRouteCalculationAttempts<>0 OR @CanDeleteDatabase<>0 OR @CanDeleteDboSchema<>0
    THROW 55166, N'Permission verification failed: forbidden UPDATE or DELETE capability exists.', 1;

SELECT N'PASS' AS VerifyStatus, N'1800_010_PERMISSION_GATE' AS GateName;
