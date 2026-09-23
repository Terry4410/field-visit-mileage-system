SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Reviewed preparation script only. Run separately in an approved window.
IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 54400, N'Permission grant refused: target is not db-fieldvisit-uat.', 1;

DECLARE @PrincipalId INT = USER_ID(N'gh-fieldvisit-uat-migrate');
IF @PrincipalId IS NULL
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.database_principals
       WHERE principal_id = @PrincipalId
         AND type_desc = N'EXTERNAL_USER'
   )
    THROW 54401, N'Permission grant refused: expected EXTERNAL_USER is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_datareader', N'gh-fieldvisit-uat-migrate'), 0) <> 1
    THROW 54402, N'Permission grant refused: retained db_datareader baseline is missing.', 1;

IF ISNULL(IS_ROLEMEMBER(N'db_ddladmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_owner', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_securityadmin', N'gh-fieldvisit-uat-migrate'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER(N'db_datawriter', N'gh-fieldvisit-uat-migrate'), 0) = 1
    THROW 54403, N'Permission grant refused: forbidden elevated or broad role exists.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    JOIN sys.database_principals r
      ON r.principal_id = drm.role_principal_id
    WHERE drm.member_principal_id = @PrincipalId
      AND r.name <> N'db_datareader'
)
    THROW 54404, N'Permission grant refused: unexpected role membership exists.', 1;

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
    THROW 54405, N'Permission grant refused: explicit baseline is not approved CONNECT only.', 1;

IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Users', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Organizations', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Teams', N'U') IS NULL
   OR OBJECT_ID(N'dbo.UserIdentityProfiles', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Employments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Locations', N'U') IS NULL
   OR OBJECT_ID(N'dbo.DeploymentSiteLocationAssignments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Projects', N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTypes', N'U') IS NULL
   OR OBJECT_ID(N'dbo.MileageRateRules', N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTrips', N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTripSnapshots', N'U') IS NULL
   OR OBJECT_ID(N'dbo.VisitTripSnapshotStops', N'U') IS NULL
   OR OBJECT_ID(N'dbo.MileageCalculations', N'U') IS NULL
    THROW 54406, N'Permission grant refused: required 1.8.0-007 predecessor objects are missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.SchemaVersions
    WHERE VersionNumber = N'1.8.0-006'
)
    THROW 54407, N'Permission grant refused: exact predecessor 1.8.0-006 is missing.', 1;

IF
(
    SELECT TOP (1) VersionNumber
    FROM dbo.SchemaVersions
    ORDER BY AppliedAt DESC, VersionNumber DESC
) <> N'1.8.0-006'
    THROW 54408,
        N'Permission grant refused: latest SchemaVersion is not exact predecessor 1.8.0-006.',
        1;

IF EXISTS
(
    SELECT 1
    FROM dbo.SchemaVersions
    WHERE VersionNumber = N'1.8.0-007'
)
    THROW 54409, N'Permission grant refused: target 1.8.0-007 already exists.', 1;

IF OBJECT_ID(N'dbo.GeocodingAttempts', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.RouteCalculationAttempts', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.MileageGovernanceEvents', N'U') IS NOT NULL
   OR COL_LENGTH(N'dbo.Locations', N'SelectedGeocodingAttemptId') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'SelectedRouteCalculationAttemptId') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'ManualFallbackUsed') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'DistanceDecisionGovernanceVersion') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'ApprovedDistanceSource') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'ApprovalBasisCode') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'ApprovalBasisHash') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'DistanceApprovedAt') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'DistanceApprovedByUserId') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'InvalidatedAt') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'InvalidatedByUserId') IS NOT NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'InvalidationReason') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'MileageRouteAttemptIdSnapshot') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'RouteTravelModeSnapshot') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'RouteCalculatedAtSnapshot') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'RouteCalculationStatusSnapshot') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'RouteErrorCodeSnapshot') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'RouteCorrelationIdSnapshot') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'ApprovedDistanceSourceSnapshot') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'ApprovalBasisCodeSnapshot') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'ApprovalBasisHashSnapshot') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'DistanceApprovedAtSnapshot') IS NOT NULL
   OR OBJECT_ID(N'dbo.TR_Locations_SelectedGeocodingAttempt', N'TR') IS NOT NULL
   OR OBJECT_ID(N'dbo.TR_MileageCalculations_SelectedRouteTrip', N'TR') IS NOT NULL
   OR OBJECT_ID(N'dbo.TR_MileageCalculations_DecisionEvidence', N'TR') IS NOT NULL
   OR OBJECT_ID(N'dbo.TR_VisitTripSnapshots_RouteAttemptTrip', N'TR') IS NOT NULL
    THROW 54410, N'Permission grant refused: one or more of 29 Stage 007 partial-state markers exist.', 1;

DECLARE
    @CanUpdateMileageCalculations INT,
    @CanUpdateOrganizations INT,
    @CanUpdateTeams INT,
    @CanUpdateUserIdentityProfiles INT,
    @CanUpdateLocations INT,
    @CanUpdateDeploymentAssignments INT,
    @CanUpdateProjects INT,
    @CanUpdateVisitTypes INT,
    @CanUpdateMileageRateRules INT,
    @CanUpdateEmployments INT,
    @CanDeleteDatabase INT,
    @CanDeleteDboSchema INT;

BEGIN TRY
    EXECUTE AS USER = N'gh-fieldvisit-uat-migrate';

    SELECT
        @CanUpdateMileageCalculations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.MileageCalculations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateOrganizations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Organizations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateTeams = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Teams', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateUserIdentityProfiles = ISNULL(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateLocations = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Locations', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateDeploymentAssignments = ISNULL(HAS_PERMS_BY_NAME(N'dbo.DeploymentSiteLocationAssignments', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateProjects = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Projects', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateVisitTypes = ISNULL(HAS_PERMS_BY_NAME(N'dbo.VisitTypes', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateMileageRateRules = ISNULL(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules', N'OBJECT', N'UPDATE'), 0),
        @CanUpdateEmployments = ISNULL(HAS_PERMS_BY_NAME(N'dbo.Employments', N'OBJECT', N'UPDATE'), 0),
        @CanDeleteDatabase = ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'DELETE'), 0),
        @CanDeleteDboSchema = ISNULL(HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'DELETE'), 0);

    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME() = N'gh-fieldvisit-uat-migrate' REVERT;
    THROW;
END CATCH;

IF @CanUpdateMileageCalculations <> 0
   OR @CanUpdateOrganizations <> 0
   OR @CanUpdateTeams <> 0
   OR @CanUpdateUserIdentityProfiles <> 0
   OR @CanUpdateLocations <> 0
   OR @CanUpdateDeploymentAssignments <> 0
   OR @CanUpdateProjects <> 0
   OR @CanUpdateVisitTypes <> 0
   OR @CanUpdateMileageRateRules <> 0
   OR @CanUpdateEmployments <> 0
   OR @CanDeleteDatabase <> 0
   OR @CanDeleteDboSchema <> 0
    THROW 54411, N'Permission grant refused: pre-stage UPDATE or DELETE capability residue exists.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    ALTER ROLE db_ddladmin ADD MEMBER [gh-fieldvisit-uat-migrate];
    GRANT INSERT ON SCHEMA::dbo TO [gh-fieldvisit-uat-migrate];
    GRANT UPDATE ON OBJECT::dbo.MileageCalculations TO [gh-fieldvisit-uat-migrate];

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT N'GRANT_PREPARED_FOR_1800_007' AS PermissionState,
       DB_NAME() AS DatabaseName,
       N'gh-fieldvisit-uat-migrate' AS DatabasePrincipal;
