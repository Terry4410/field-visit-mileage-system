SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 55170, N'Permission revoke refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
   OR NOT EXISTS(SELECT 1 FROM sys.database_principals WHERE principal_id=@PrincipalId AND type_desc=N'EXTERNAL_USER')
    THROW 55171, N'Permission revoke refused: expected EXTERNAL_USER is missing.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    REVOKE ALTER ON OBJECT::dbo.RouteCalculationAttempts FROM [gh-fieldvisit-uat-migrate];
    REVOKE INSERT ON OBJECT::dbo.SchemaVersions FROM [gh-fieldvisit-uat-migrate];
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader',N'gh-fieldvisit-uat-migrate'),0)<>1
   OR ISNULL(IS_ROLEMEMBER(N'db_ddladmin',N'gh-fieldvisit-uat-migrate'),0)<>0
   OR ISNULL(IS_ROLEMEMBER(N'db_owner',N'gh-fieldvisit-uat-migrate'),0)<>0
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin',N'gh-fieldvisit-uat-migrate'),0)<>0
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter',N'gh-fieldvisit-uat-migrate'),0)<>0
    THROW 55172, N'Permission revoke post-check failed: role baseline is not restored.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.database_role_members drm
    JOIN sys.database_principals r ON r.principal_id=drm.role_principal_id
    WHERE drm.member_principal_id=@PrincipalId AND r.name<>N'db_datareader'
)
    THROW 55173, N'Permission revoke post-check failed: unexpected role membership remains.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.database_permissions p
    WHERE p.grantee_principal_id=@PrincipalId
      AND NOT(p.class=0 AND p.permission_name=N'CONNECT' AND p.state=N'G')
)
    THROW 55174, N'Permission revoke post-check failed: explicit baseline is not CONNECT only.', 1;

DECLARE
    @CanAlterRouteCalculationAttempts INT,
    @CanInsertSchemaVersions INT,
    @CanUpdateRouteCalculationAttempts INT,
    @CanDeleteDatabase INT,
    @CanDeleteDboSchema INT;

BEGIN TRY
    EXECUTE AS USER=N'gh-fieldvisit-uat-migrate';
    SELECT
        @CanAlterRouteCalculationAttempts=ISNULL(HAS_PERMS_BY_NAME(N'dbo.RouteCalculationAttempts',N'OBJECT',N'ALTER'),0),
        @CanInsertSchemaVersions=ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'INSERT'),0),
        @CanUpdateRouteCalculationAttempts=ISNULL(HAS_PERMS_BY_NAME(N'dbo.RouteCalculationAttempts',N'OBJECT',N'UPDATE'),0),
        @CanDeleteDatabase=ISNULL(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'DELETE'),0),
        @CanDeleteDboSchema=ISNULL(HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'DELETE'),0);
    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME()=N'gh-fieldvisit-uat-migrate' REVERT;
    THROW;
END CATCH;

IF @CanAlterRouteCalculationAttempts<>0
   OR @CanInsertSchemaVersions<>0
   OR @CanUpdateRouteCalculationAttempts<>0
   OR @CanDeleteDatabase<>0
   OR @CanDeleteDboSchema<>0
    THROW 55175, N'Permission revoke post-check failed: Stage 010 elevation residue remains.', 1;

SELECT N'REVOKED_1800_010_ELEVATION' AS PermissionState,
       DB_NAME() AS DatabaseName,
       N'gh-fieldvisit-uat-migrate' AS DatabasePrincipal;
