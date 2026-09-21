SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Reviewed preparation script only. Run separately in an approved window.
IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 54100, N'Permission grant refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.database_principals
       WHERE principal_id = @PrincipalId
         AND type_desc = N'EXTERNAL_USER'
   )
    THROW 54101, N'Permission grant refused: expected EXTERNAL_USER is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
    THROW 54102, N'Permission grant refused: retained db_datareader baseline is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_owner', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter', N'gh-fieldvisit-uat-migrate'), 0) = 1
    THROW 54103, N'Permission grant refused: forbidden elevated or broad role exists.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    JOIN sys.database_principals r
      ON r.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = @PrincipalId
      AND r.name <> N'db_datareader'
)
    THROW 54104, N'Permission grant refused: unexpected role membership exists.', 1;

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
    THROW 54105, N'Permission grant refused: unexpected explicit permission state exists.', 1;

IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Users', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Locations', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Teams', N'U') IS NULL
   OR OBJECT_ID(N'dbo.DeploymentSiteLocationAssignments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTrips', N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTripSnapshots', N'U') IS NULL
    THROW 54106, N'Permission grant refused: required 1.8.0-004 predecessor objects are missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.SchemaVersions
    WHERE VersionNumber = N'1.8.0-003'
)
    THROW 54107, N'Permission grant refused: exact predecessor 1.8.0-003 is missing.', 1;

IF
(
    SELECT TOP (1) VersionNumber
    FROM dbo.SchemaVersions
    ORDER BY AppliedAt DESC, VersionNumber DESC
) <> N'1.8.0-003'
    THROW 54108,
        N'Permission grant refused: latest SchemaVersion is not exact predecessor 1.8.0-003.',
        1;

IF EXISTS
(
    SELECT 1
    FROM dbo.SchemaVersions
    WHERE VersionNumber = N'1.8.0-004'
)
    THROW 54109, N'Permission grant refused: target 1.8.0-004 already exists.', 1;

IF OBJECT_ID(N'dbo.TeamLocationNotes', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.TeamLocationNoteHistory', N'U') IS NOT NULL
   OR COL_LENGTH(N'dbo.Locations', N'TaxId') IS NOT NULL
   OR COL_LENGTH(N'dbo.Locations', N'MasterNote') IS NOT NULL
   OR COL_LENGTH(N'dbo.Locations', N'InactivatedAt') IS NOT NULL
   OR COL_LENGTH(N'dbo.Locations', N'InactivatedByUserId') IS NOT NULL
   OR COL_LENGTH(N'dbo.Locations', N'DuplicateOfLocationId') IS NOT NULL
   OR COL_LENGTH(N'dbo.Locations', N'DuplicateReason') IS NOT NULL
   OR COL_LENGTH(N'dbo.Locations', N'NormalizedLocationName') IS NOT NULL
   OR COL_LENGTH(N'dbo.Locations', N'NormalizedAddress') IS NOT NULL
   OR OBJECT_ID(N'dbo.TR_Locations_ProtectCurrentDeploymentSiteLocations', N'TR') IS NOT NULL
   OR OBJECT_ID(N'dbo.TR_DeploymentSiteLocationAssignments_ProtectActiveLocation', N'TR') IS NOT NULL
   OR EXISTS
      (
          SELECT 1 FROM sys.indexes
          WHERE object_id = OBJECT_ID(N'dbo.Locations', N'U')
            AND name = N'IX_Locations_Organization_TaxId'
      )
   OR EXISTS
      (
          SELECT 1 FROM sys.indexes
          WHERE object_id = OBJECT_ID(N'dbo.Locations', N'U')
            AND name = N'IX_Locations_NormalizedNameAddress'
      )
   OR EXISTS
      (
          SELECT 1 FROM sys.indexes
          WHERE object_id = OBJECT_ID(N'dbo.Locations', N'U')
            AND name = N'IX_Locations_DuplicateOf'
      )
   OR EXISTS
      (
          SELECT 1 FROM sys.indexes
          WHERE object_id = OBJECT_ID(N'dbo.TeamLocationNotes', N'U')
            AND name = N'IX_TeamLocationNotes_Location_Team'
      )
   OR EXISTS
      (
          SELECT 1 FROM sys.indexes
          WHERE object_id = OBJECT_ID(N'dbo.TeamLocationNoteHistory', N'U')
            AND name = N'IX_TeamLocationNoteHistory_Note_Changed'
      )
    THROW 54110, N'Permission grant refused: one or more of 17 Stage 004 partial-state markers exist.', 1;

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

SELECT N'GRANT_PREPARED_FOR_1800_004' AS PermissionState,
       DB_NAME() AS DatabaseName,
       N'gh-fieldvisit-uat-migrate' AS DatabasePrincipal;
