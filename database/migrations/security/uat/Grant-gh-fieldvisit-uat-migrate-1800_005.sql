SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Reviewed preparation script only. Run separately in an approved window.
IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 54200, N'Permission grant refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.database_principals
       WHERE principal_id = @PrincipalId
         AND type_desc = N'EXTERNAL_USER'
   )
    THROW 54201, N'Permission grant refused: expected EXTERNAL_USER is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
    THROW 54202, N'Permission grant refused: retained db_datareader baseline is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_owner', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter', N'gh-fieldvisit-uat-migrate'), 0) = 1
    THROW 54203, N'Permission grant refused: forbidden elevated or broad role exists.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    JOIN sys.database_principals r
      ON r.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = @PrincipalId
      AND r.name <> N'db_datareader'
)
    THROW 54204, N'Permission grant refused: unexpected role membership exists.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_permissions p
    WHERE p.grantee_principal_id = @PrincipalId
      AND NOT
      (
          p.class = 0
          AND p.permission_name = N'CONNECT'
          AND p.state = N'G'
      )
)
    THROW 54205, N'Permission grant refused: explicit baseline is not approved CONNECT only.', 1;

IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Users', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Organizations', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Teams', N'U') IS NULL
   OR OBJECT_ID(N'dbo.UserIdentityProfiles', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Locations', N'U') IS NULL
   OR OBJECT_ID(N'dbo.DeploymentSiteLocationAssignments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Projects', N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTypes', N'U') IS NULL
   OR OBJECT_ID(N'dbo.MileageRateRules', N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTrips', N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTripSnapshots', N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTripSnapshotStops', N'U') IS NULL
    THROW 54206, N'Permission grant refused: required 1.8.0-005 predecessor objects are missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.SchemaVersions
    WHERE VersionNumber = N'1.8.0-004'
)
    THROW 54207, N'Permission grant refused: exact predecessor 1.8.0-004 is missing.', 1;

IF
(
    SELECT TOP (1) VersionNumber
    FROM dbo.SchemaVersions
    ORDER BY AppliedAt DESC, VersionNumber DESC
) <> N'1.8.0-004'
    THROW 54208,
        N'Permission grant refused: latest SchemaVersion is not exact predecessor 1.8.0-004.',
        1;

IF EXISTS
(
    SELECT 1
    FROM dbo.SchemaVersions
    WHERE VersionNumber = N'1.8.0-005'
)
    THROW 54209, N'Permission grant refused: target 1.8.0-005 already exists.', 1;

IF COL_LENGTH(N'dbo.Projects', N'InactivatedAt') IS NOT NULL
   OR COL_LENGTH(N'dbo.Projects', N'InactivatedByUserId') IS NOT NULL
   OR COL_LENGTH(N'dbo.Projects', N'RowVersion') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTypes', N'InactivatedAt') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTypes', N'InactivatedByUserId') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTypes', N'RowVersion') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageRateRules', N'CreatedByUserId') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageRateRules', N'UpdatedByUserId') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageRateRules', N'InactivatedAt') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageRateRules', N'InactivatedByUserId') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageRateRules', N'RowVersion') IS NOT NULL
   OR OBJECT_ID(N'dbo.TR_MileageRateRules_ProtectSeries', N'TR') IS NOT NULL
   OR EXISTS
      (
          SELECT 1 FROM sys.indexes
          WHERE object_id = OBJECT_ID(N'dbo.Projects', N'U')
            AND name = N'IX_Projects_Search'
      )
   OR EXISTS
      (
          SELECT 1 FROM sys.indexes
          WHERE object_id = OBJECT_ID(N'dbo.VisitTypes', N'U')
            AND name = N'UX_VisitTypes_VisitTypeCode'
      )
   OR EXISTS
      (
          SELECT 1 FROM sys.indexes
          WHERE object_id = OBJECT_ID(N'dbo.VisitTypes', N'U')
            AND name = N'IX_VisitTypes_Active_Sort'
      )
   OR EXISTS
      (
          SELECT 1 FROM sys.indexes
          WHERE object_id = OBJECT_ID(N'dbo.MileageRateRules', N'U')
            AND name = N'UX_MileageRateRules_Scope_Vehicle_Start'
      )
   OR EXISTS
      (
          SELECT 1 FROM sys.indexes
          WHERE object_id = OBJECT_ID(N'dbo.MileageRateRules', N'U')
            AND name = N'IX_MileageRateRules_AsOf'
      )
    THROW 54210, N'Permission grant refused: one or more of 17 Stage 005 partial-state markers exist.', 1;

DECLARE
    @CanUpdateMileageRateRules INT,
    @CanUpdateProjects INT,
    @CanUpdateVisitTypes INT,
    @CanUpdateOrganizations INT,
    @CanUpdateTeams INT,
    @CanUpdateUserIdentityProfiles INT,
    @CanUpdateLocations INT,
    @CanUpdateDeploymentAssignments INT;

BEGIN TRY
    EXECUTE AS USER = N'gh-fieldvisit-uat-migrate';

    SELECT
        @CanUpdateMileageRateRules = ISNULL(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateProjects = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Projects', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateVisitTypes = ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTypes', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateOrganizations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Organizations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateTeams = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Teams', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateUserIdentityProfiles = ISNULL(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateLocations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Locations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateDeploymentAssignments = ISNULL(HAS_PERMS_BY_NAME(N'dbo.DeploymentSiteLocationAssignments', N'OBJECT', N'UPDATE'), 0);

    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME() = N'gh-fieldvisit-uat-migrate' REVERT;
    THROW;
END CATCH;

IF @CanUpdateMileageRateRules <> 0
   OR @CanUpdateProjects <> 0
   OR @CanUpdateVisitTypes <> 0
   OR @CanUpdateOrganizations <> 0
   OR @CanUpdateTeams <> 0
   OR @CanUpdateUserIdentityProfiles <> 0
   OR @CanUpdateLocations <> 0
   OR @CanUpdateDeploymentAssignments <> 0
    THROW 54211, N'Permission grant refused: pre-stage UPDATE capability residue exists.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    ALTER ROLE db_ddladmin ADD MEMBER [gh-fieldvisit-uat-migrate];
    GRANT INSERT ON SCHEMA::dbo TO [gh-fieldvisit-uat-migrate];
    GRANT UPDATE ON OBJECT::dbo.Projects TO [gh-fieldvisit-uat-migrate];
    GRANT UPDATE ON OBJECT::dbo.VisitTypes TO [gh-fieldvisit-uat-migrate];
    GRANT UPDATE ON OBJECT::dbo.MileageRateRules TO [gh-fieldvisit-uat-migrate];

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT N'GRANT_PREPARED_FOR_1800_005' AS PermissionState,
       DB_NAME() AS DatabaseName,
       N'gh-fieldvisit-uat-migrate' AS DatabasePrincipal;
