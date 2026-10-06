SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Reviewed preparation script only. Run separately in an approved UAT migration window.
IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 55140, N'Permission grant refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
   OR NOT EXISTS
   (
       SELECT 1 FROM sys.database_principals
       WHERE principal_id = @PrincipalId AND type_desc = N'EXTERNAL_USER'
   )
    THROW 55141, N'Permission grant refused: expected EXTERNAL_USER is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
    THROW 55142, N'Permission grant refused: retained db_datareader baseline is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_owner', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter', N'gh-fieldvisit-uat-migrate'), 0) = 1
    THROW 55143, N'Permission grant refused: forbidden elevated or broad role exists.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.database_role_members drm
    JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = @PrincipalId AND r.name <> N'db_datareader'
)
    THROW 55144, N'Permission grant refused: unexpected role membership exists.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.database_permissions p
    WHERE p.grantee_principal_id = @PrincipalId
      AND NOT (p.class = 0 AND p.permission_name = N'CONNECT' AND p.state = N'G')
)
    THROW 55145, N'Permission grant refused: explicit baseline is not approved CONNECT only.', 1;

IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.RouteCalculationAttempts', N'U') IS NULL
   OR OBJECT_ID(N'dbo.CK_RouteCalculationAttempts_Reason', N'C') IS NULL
    THROW 55146, N'Permission grant refused: required 1.8.0-010 predecessor objects are missing.', 1;

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-009')
   OR (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC, VersionNumber DESC) <> N'1.8.0-009'
    THROW 55147, N'Permission grant refused: latest SchemaVersion is not exact predecessor 1.8.0-009.', 1;

IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-010')
    THROW 55148, N'Permission grant refused: target 1.8.0-010 already exists.', 1;

DECLARE @OldDefinition NVARCHAR(MAX) =
    (SELECT definition FROM sys.check_constraints WHERE object_id = OBJECT_ID(N'dbo.CK_RouteCalculationAttempts_Reason', N'C'));
DECLARE @OldCompact NVARCHAR(MAX) =
    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @OldDefinition, N' ', N''), N'[', N''), N']', N''), N'(', N''), N')', N''));

IF @OldDefinition IS NULL
   OR @OldCompact NOT LIKE N'%visitorcalculate%'
   OR @OldCompact NOT LIKE N'%leaderretry%'
   OR @OldCompact NOT LIKE N'%correctionrecalculate%'
   OR @OldCompact LIKE N'%backgroundmileagejob%'
    THROW 55149, N'Permission grant refused: predecessor CalculationReason constraint drifted.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE object_id = OBJECT_ID(N'dbo.CK_RouteCalculationAttempts_Reason', N'C')
      AND (is_disabled = 1 OR is_not_trusted = 1)
)
    THROW 55150, N'Permission grant refused: predecessor CalculationReason constraint is disabled or untrusted.', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.RouteCalculationAttempts
    WHERE CalculationReason NOT IN
    (
        N'VisitorCalculate',
        N'LeaderRetry',
        N'CorrectionRecalculate'
    )
)
    THROW 55151, N'Permission grant refused: predecessor reason data drifted.', 1;

DECLARE
    @CanAlterRouteCalculationAttempts INT,
    @CanInsertSchemaVersions INT,
    @CanUpdateRouteCalculationAttempts INT,
    @CanDeleteDatabase INT,
    @CanDeleteDboSchema INT;

BEGIN TRY
    EXECUTE AS USER = N'gh-fieldvisit-uat-migrate';
    SELECT
        @CanAlterRouteCalculationAttempts = ISNULL(HAS_PERMS_BY_NAME(N'dbo.RouteCalculationAttempts', N'OBJECT', N'ALTER'), 0),
        @CanInsertSchemaVersions = ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions', N'OBJECT', N'INSERT'), 0),
        @CanUpdateRouteCalculationAttempts = ISNULL(HAS_PERMS_BY_NAME(N'dbo.RouteCalculationAttempts', N'OBJECT', N'UPDATE'), 0),
        @CanDeleteDatabase = ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'DELETE'), 0),
        @CanDeleteDboSchema = ISNULL(HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'DELETE'), 0);
    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME() = N'gh-fieldvisit-uat-migrate' REVERT;
    THROW;
END CATCH;

IF @CanAlterRouteCalculationAttempts <> 0
   OR @CanInsertSchemaVersions <> 0
   OR @CanUpdateRouteCalculationAttempts <> 0
   OR @CanDeleteDatabase <> 0
   OR @CanDeleteDboSchema <> 0
    THROW 55152, N'Permission grant refused: Stage 010 capability residue exists before grant.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    GRANT ALTER ON OBJECT::dbo.RouteCalculationAttempts TO [gh-fieldvisit-uat-migrate];
    GRANT INSERT ON OBJECT::dbo.SchemaVersions TO [gh-fieldvisit-uat-migrate];
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT N'GRANT_PREPARED_FOR_1800_010' AS PermissionState,
       DB_NAME() AS DatabaseName,
       N'gh-fieldvisit-uat-migrate' AS DatabasePrincipal;
