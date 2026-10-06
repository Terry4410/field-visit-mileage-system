SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Reviewed preparation script only. Run separately in an approved UAT migration window.
IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 54840, N'Permission grant refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
   OR NOT EXISTS
   (
       SELECT 1 FROM sys.database_principals
       WHERE principal_id = @PrincipalId AND type_desc = N'EXTERNAL_USER'
   )
    THROW 54841, N'Permission grant refused: expected EXTERNAL_USER is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
    THROW 54842, N'Permission grant refused: retained db_datareader baseline is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_owner', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter', N'gh-fieldvisit-uat-migrate'), 0) = 1
    THROW 54843, N'Permission grant refused: forbidden elevated or broad role exists.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.database_role_members drm
    JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = @PrincipalId AND r.name <> N'db_datareader'
)
    THROW 54844, N'Permission grant refused: unexpected role membership exists.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.database_permissions p
    WHERE p.grantee_principal_id = @PrincipalId
      AND NOT (p.class = 0 AND p.permission_name = N'CONNECT' AND p.state = N'G')
)
    THROW 54845, N'Permission grant refused: explicit baseline is not approved CONNECT only.', 1;

IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.RouteCalculationAttempts', N'U') IS NULL
   OR OBJECT_ID(N'dbo.CK_RouteCalculationAttempts_StopCount', N'C') IS NULL
    THROW 54846, N'Permission grant refused: required 1.8.0-009 predecessor objects are missing.', 1;

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-008')
   OR (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC, VersionNumber DESC) <> N'1.8.0-008'
    THROW 54847, N'Permission grant refused: latest SchemaVersion is not exact predecessor 1.8.0-008.', 1;

IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-009')
    THROW 54848, N'Permission grant refused: target 1.8.0-009 already exists.', 1;

DECLARE @OldDefinition NVARCHAR(MAX) =
    (SELECT definition FROM sys.check_constraints WHERE object_id = OBJECT_ID(N'dbo.CK_RouteCalculationAttempts_StopCount', N'C'));
DECLARE @OldNormalized NVARCHAR(MAX) =
    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @OldDefinition, N' ', N''), N'[', N''), N']', N''), N'(', N''), N')', N''));

IF @OldDefinition IS NULL OR @OldNormalized <> N'stopcount>=2'
    THROW 54849, N'Permission grant refused: predecessor StopCount constraint drifted.', 1;

DECLARE
    @CanUpdateRouteCalculationAttempts INT,
    @CanDeleteDatabase INT,
    @CanDeleteDboSchema INT;

BEGIN TRY
    EXECUTE AS USER = N'gh-fieldvisit-uat-migrate';
    SELECT
        @CanUpdateRouteCalculationAttempts = ISNULL(HAS_PERMS_BY_NAME(N'dbo.RouteCalculationAttempts', N'OBJECT', N'UPDATE'), 0),
        @CanDeleteDatabase = ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'DELETE'), 0),
        @CanDeleteDboSchema = ISNULL(HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'DELETE'), 0);
    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME() = N'gh-fieldvisit-uat-migrate' REVERT;
    THROW;
END CATCH;

IF @CanUpdateRouteCalculationAttempts <> 0 OR @CanDeleteDatabase <> 0 OR @CanDeleteDboSchema <> 0
    THROW 54850, N'Permission grant refused: forbidden UPDATE or DELETE capability residue exists.', 1;

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

SELECT N'GRANT_PREPARED_FOR_1800_009' AS PermissionState,
       DB_NAME() AS DatabaseName,
       N'gh-fieldvisit-uat-migrate' AS DatabasePrincipal;
