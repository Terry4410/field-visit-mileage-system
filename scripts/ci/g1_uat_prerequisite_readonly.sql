-- G1 UAT pre-migration READ ONLY. Returns enum status rows ONLY.
-- This is not a full 1.5/010 fidelity certificate, API principal test or migration authorization.
-- Fixed source code only; no parameters or raw metadata, counts, identities or business rows exported.
SET NOCOUNT ON;
SELECT code,status FROM (VALUES
 (N'BASE_OBJECTS', CASE WHEN
     OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.Organizations',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.Teams',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.Users',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.Locations',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.VisitTrips',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.VisitTripStops',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.VisitTripSnapshots',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.VisitTripSnapshotStops',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.ImportBatches',N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.ImportBatchItems',N'U') IS NOT NULL
 THEN N'PASS' ELSE N'FAIL' END),
 (N'LATEST_PREDECESSOR_010', CASE WHEN
     (SELECT COUNT_BIG(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010')=1
 AND (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC)=N'1.8.0-010'
 THEN N'PASS' ELSE N'FAIL' END),
 (N'UNASSIGNED_MARKERS', CASE WHEN NOT EXISTS
     (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber IN (
      N'1.8.0-011',N'1.8.0-012',N'1.8.0-013',N'1.8.0-014',N'1.8.0-015',
      N'1.8.0-016',N'1.8.0-LEADER-REVIEW',N'1.8.0-017',N'1.8.0-CREATE-LOWRISK',
      N'1.8.0-018',N'1.8.0-LOWRISK-DIRECT',N'1.8.0-019',N'1.8.0-020',
      N'1.8.0-EXCEL-CANONICAL-STATUS'))
 AND NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N'1.8.0-%'
      AND VersionNumber NOT IN (N'1.8.0-001',N'1.8.0-002',N'1.8.0-003',
      N'1.8.0-004',N'1.8.0-005',N'1.8.0-006',N'1.8.0-007',
      N'1.8.0-008',N'1.8.0-009',N'1.8.0-010'))
 THEN N'PASS' ELSE N'FAIL' END),
 (N'NEW_MANAGED_OBJECTS_ABSENT', CASE WHEN
     OBJECT_ID(N'dbo.ChangeRequests',N'U') IS NULL
 AND OBJECT_ID(N'dbo.ChangeRequestEvents',N'U') IS NULL
 AND OBJECT_ID(N'dbo.ChangeRequestLeaderReviews',N'U') IS NULL
 AND OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NULL
 AND OBJECT_ID(N'dbo.LocationPublicationStages',N'U') IS NULL
 AND OBJECT_ID(N'dbo.LocationPublishedVersions',N'U') IS NULL
 AND OBJECT_ID(N'dbo.FieldVisitMigrationLedger',N'U') IS NULL
 THEN N'PASS' ELSE N'FAIL' END),
 (N'014_PUBLISHED_SOURCE', CASE WHEN NOT EXISTS (
    SELECT 1 FROM dbo.Locations l
    WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1
      AND (l.LocationName IS NULL OR NULLIF(LTRIM(RTRIM(l.LocationName)),N'') IS NULL
      OR l.Address IS NULL OR NULLIF(LTRIM(RTRIM(l.Address)),N'') IS NULL
      OR l.CreatedByUserId<=0 OR l.RowVersion IS NULL))
 THEN N'PASS' ELSE N'FAIL' END),
 (N'014_AUTHORITY_REFERENCES', CASE WHEN NOT EXISTS (
    SELECT 1 FROM dbo.Locations l
    LEFT JOIN dbo.Organizations o ON o.OrganizationId=l.OrganizationId
    LEFT JOIN dbo.Teams t ON t.TeamId=l.TeamId
    LEFT JOIN dbo.Users u ON u.UserId=l.CreatedByUserId
    WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1
      AND (o.OrganizationId IS NULL OR t.TeamId IS NULL OR u.UserId IS NULL))
 THEN N'PASS' ELSE N'FAIL' END),
 (N'020_IMPORT_EXISTING_ROWS', CASE WHEN NOT EXISTS (
    SELECT 1 FROM dbo.ImportBatches WHERE Status NOT IN
      (N'Previewed',N'Confirmed',N'PartiallyFailed') OR Status IS NULL)
 AND NOT EXISTS (
    SELECT 1 FROM dbo.ImportBatchItems WHERE Status NOT IN
      (N'Valid',N'Error',N'Applied',N'Failed') OR Status IS NULL)
 THEN N'PASS' ELSE N'FAIL' END),
 (N'020_IMPORT_STATUS_WIDTH', CASE WHEN
    COL_LENGTH(N'dbo.ImportBatches',N'Status')>=30
 AND COL_LENGTH(N'dbo.ImportBatchItems',N'Status')>=18
 THEN N'PASS' ELSE N'FAIL' END)
) x(code,status) ORDER BY code;
