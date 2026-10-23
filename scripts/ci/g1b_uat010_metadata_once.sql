-- G1-B: one SELECT-only snapshot, metadata / boolean checks only. No business rows exported.
-- The SQL executor verifies target and emits allowlisted PASS / FAIL / UNKNOWN flags.
SET NOCOUNT ON;
WITH ExpectedTriggers AS (
    SELECT name, table_name FROM (VALUES
        (N'TR_Locations_ProtectCurrentDeploymentSiteLocations',N'Locations'),
        (N'TR_DeploymentSiteLocationAssignments_ProtectActiveLocation',N'DeploymentSiteLocationAssignments'),
        (N'TR_RouteCalculationAttempts_BasisTrip',N'RouteCalculationAttempts'),
        (N'TR_Locations_SelectedGeocodingAttempt',N'Locations'),
        (N'TR_MileageCalculations_SelectedRouteTrip',N'MileageCalculations'),
        (N'TR_MileageCalculations_DecisionEvidence',N'MileageCalculations'),
        (N'TR_VisitTripSnapshots_RouteAttemptTrip',N'VisitTripSnapshots')
    ) AS v(name,table_name)
)
SELECT code, status FROM (VALUES
 (N'TARGET', CASE WHEN DB_NAME() COLLATE Latin1_General_100_BIN2=N'db-fieldvisit-uat'
      AND CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')) COLLATE Latin1_General_100_BIN2=N'sql-fieldvisit-jpe-uat'
      AND CONVERT(int,SERVERPROPERTY(N'EngineEdition'))=5 THEN N'PASS' ELSE N'FAIL' END),
 (N'PREDECESSOR_010', CASE WHEN
      (SELECT COUNT_BIG(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010')=1
      AND (SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC)=N'1.8.0-010'
      THEN N'PASS' ELSE N'FAIL' END),
 (N'RESERVED_013_ABSENT', CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-013') THEN N'PASS' ELSE N'FAIL' END),
 (N'PREMIGRATION_011_020', CASE WHEN NOT EXISTS(
      SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber IN
      (N'1.8.0-011',N'1.8.0-012',N'1.8.0-014',N'1.8.0-015',N'1.8.0-016',N'1.8.0-017',N'1.8.0-018',N'1.8.0-019',N'1.8.0-020'))
      THEN N'PASS' ELSE N'FAIL' END),
 (N'BASE_010_TABLES', CASE WHEN
      OBJECT_ID(N'dbo.Locations',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Teams',N'U') IS NOT NULL
      AND OBJECT_ID(N'dbo.Users',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Organizations',N'U') IS NOT NULL
      AND OBJECT_ID(N'dbo.VisitTrips',N'U') IS NOT NULL
      AND OBJECT_ID(N'dbo.VisitTripSnapshots',N'U') IS NOT NULL
      AND OBJECT_ID(N'dbo.VisitTripSnapshotStops',N'U') IS NOT NULL
      THEN N'PASS' ELSE N'FAIL' END),
 (N'LEGACY_004_007_TRIGGERS', CASE WHEN
      NOT EXISTS(SELECT 1 FROM ExpectedTriggers e WHERE NOT EXISTS (
        SELECT 1 FROM sys.triggers t WHERE t.name=e.name
          AND OBJECT_SCHEMA_NAME(t.parent_id)=N'dbo' AND OBJECT_NAME(t.parent_id)=e.table_name
          AND t.is_disabled=0 AND t.is_ms_shipped=0
          AND t.is_instead_of_trigger=0 AND OBJECT_DEFINITION(t.object_id) IS NOT NULL
          AND EXISTS (SELECT 1 FROM sys.trigger_events ev WHERE ev.object_id=t.object_id)))
      THEN N'PASS' ELSE N'FAIL' END),
 (N'LEGACY_TRIGGER_SOURCE_PARITY',N'UNKNOWN'),
 (N'EXISTING_FK_CHECK_TRUST', CASE WHEN EXISTS(
      SELECT 1 FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1
      UNION ALL SELECT 1 FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1)
      THEN N'FAIL' ELSE N'PASS' END),
 (N'IMPORT_STATUS_CHECKS', CASE WHEN
      OBJECT_ID(N'dbo.ImportBatches',N'U') IS NULL OR OBJECT_ID(N'dbo.ImportBatchItems',N'U') IS NULL
      THEN N'UNKNOWN'
      WHEN EXISTS(SELECT 1 FROM (VALUES (N'CK_ImportBatches_Status'),(N'CK_ImportBatchItems_Status')) x(name)
         WHERE NOT EXISTS(SELECT 1 FROM sys.check_constraints c WHERE c.name=x.name AND c.is_disabled=0 AND c.is_not_trusted=0))
      THEN N'UNKNOWN' ELSE N'PASS' END),
 (N'CONNECTED_DIAGNOSTIC_SELECT', CASE WHEN HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'SELECT')=1
      THEN N'PASS' ELSE N'UNKNOWN' END),
 (N'CONNECTED_VIEW_DEFINITION', CASE WHEN HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'VIEW DEFINITION')=1
      THEN N'PASS' ELSE N'UNKNOWN' END),
 (N'CONNECTED_NO_SCHEMA_ALTER', CASE WHEN HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'ALTER')=0
      THEN N'PASS' WHEN HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'ALTER')=1 THEN N'FAIL' ELSE N'UNKNOWN' END),
 (N'ACTUAL_API_PRINCIPAL',N'UNKNOWN'),
 (N'FULL_010_PARITY',N'UNKNOWN')
) AS findings(code,status)
ORDER BY code;
