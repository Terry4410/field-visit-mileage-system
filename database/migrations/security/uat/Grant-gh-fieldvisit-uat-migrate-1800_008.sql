SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Reviewed preparation script only. Run separately in an approved UAT migration window.
IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 54640, N'Permission grant refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
   OR NOT EXISTS
   (
       SELECT 1 FROM sys.database_principals
       WHERE principal_id = @PrincipalId AND type_desc = N'EXTERNAL_USER'
   )
    THROW 54641, N'Permission grant refused: expected EXTERNAL_USER is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
    THROW 54642, N'Permission grant refused: retained db_datareader baseline is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_owner', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter', N'gh-fieldvisit-uat-migrate'), 0) = 1
    THROW 54643, N'Permission grant refused: forbidden elevated or broad role exists.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.database_role_members drm
    JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = @PrincipalId AND r.name <> N'db_datareader'
)
    THROW 54644, N'Permission grant refused: unexpected role membership exists.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.database_permissions p
    WHERE p.grantee_principal_id = @PrincipalId
      AND NOT (p.class = 0 AND p.permission_name = N'CONNECT' AND p.state = N'G')
)
    THROW 54645, N'Permission grant refused: explicit baseline is not approved CONNECT only.', 1;

IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.MileageRateRules', N'U') IS NULL
   OR OBJECT_ID(N'dbo.TR_MileageRateRules_ProtectSeries', N'TR') IS NULL
    THROW 54646, N'Permission grant refused: required 1.8.0-008 predecessor objects are missing.', 1;

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-007')
   OR (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC, VersionNumber DESC) <> N'1.8.0-007'
    THROW 54647, N'Permission grant refused: latest SchemaVersion is not exact predecessor 1.8.0-007.', 1;

IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-008')
    THROW 54648, N'Permission grant refused: target 1.8.0-008 already exists.', 1;

DECLARE @OldDefinition NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.TR_MileageRateRules_ProtectSeries'));
DECLARE @OldNormalized NVARCHAR(MAX) =
    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(@OldDefinition, N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N''));

IF @OldDefinition IS NULL
   OR CHARINDEX(N'lead(r.effectivefrom)', @OldNormalized) = 0
   OR CHARINDEX(N'throw53839', @OldNormalized) = 0
   OR CHARINDEX(N'fieldvisit.mileagerateseries', @OldNormalized) = 0
    THROW 54649, N'Permission grant refused: predecessor MileageRate trigger drifted.', 1;

DECLARE
    @CanUpdateMileageRateRules INT,
    @CanDeleteDatabase INT,
    @CanDeleteDboSchema INT;

BEGIN TRY
    EXECUTE AS USER = N'gh-fieldvisit-uat-migrate';
    SELECT
        @CanUpdateMileageRateRules = ISNULL(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules', N'OBJECT', N'UPDATE'), 0),
        @CanDeleteDatabase = ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'DELETE'), 0),
        @CanDeleteDboSchema = ISNULL(HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'DELETE'), 0);
    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME() = N'gh-fieldvisit-uat-migrate' REVERT;
    THROW;
END CATCH;

IF @CanUpdateMileageRateRules <> 0 OR @CanDeleteDatabase <> 0 OR @CanDeleteDboSchema <> 0
    THROW 54650, N'Permission grant refused: forbidden UPDATE or DELETE capability residue exists.', 1;

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

SELECT N'GRANT_PREPARED_FOR_1800_008' AS PermissionState,
       DB_NAME() AS DatabaseName,
       N'gh-fieldvisit-uat-migrate' AS DatabasePrincipal;
