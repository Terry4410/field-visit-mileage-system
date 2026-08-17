SET NOCOUNT ON;

/* 1. Migration must exist exactly once. */
SELECT
    VersionNumber,
    Description,
    AppliedAt,
    AppliedBy
FROM dbo.SchemaVersions
WHERE VersionNumber = N'1.7.0-007';

/* 2. Constraint must now allow all four domain states. */
SELECT
    cc.name AS ConstraintName,
    CASE WHEN UPPER(cc.definition) LIKE '%''PENDING''%'
         THEN 'YES' ELSE 'NO' END AS PendingAllowed,
    CASE WHEN UPPER(cc.definition) LIKE '%''APPROVED''%'
         THEN 'YES' ELSE 'NO' END AS ApprovedAllowed,
    CASE WHEN UPPER(cc.definition) LIKE '%''REJECTED''%'
         THEN 'YES' ELSE 'NO' END AS RejectedAllowed,
    CASE WHEN UPPER(cc.definition) LIKE '%''ABANDONED''%'
         THEN 'YES' ELSE 'NO' END AS AbandonedAllowed
FROM sys.check_constraints cc
WHERE cc.parent_object_id = OBJECT_ID(N'dbo.Locations')
  AND cc.name = N'CK_Locations_ApprovalStatus';

/* 3. There must be no true orphan temporary location still Pending. */
SELECT
    l.LocationId,
    l.LocationCode,
    l.LocationName,
    l.ApprovalStatus,
    l.GeocodingStatus,
    l.IsActive
FROM dbo.Locations l
WHERE
    l.IsTemporary = 1
    AND UPPER(LTRIM(RTRIM(l.ApprovalStatus))) = N'PENDING'
    AND NOT EXISTS
    (
        SELECT 1
        FROM dbo.VisitTripStops s
        JOIN dbo.VisitTrips t
          ON t.VisitTripId = s.VisitTripId
        WHERE
            s.LocationId = l.LocationId
            AND t.Status <> N'Cancelled'
    )
ORDER BY l.LocationId;

/* 4. The UAT defect sample should now be abandoned/inactive. */
SELECT
    l.LocationId,
    l.LocationCode,
    l.LocationName,
    l.ApprovalStatus,
    l.GeocodingStatus,
    l.IsActive
FROM dbo.Locations l
WHERE l.LocationName = N'UAT刪除草稿測試地點';
