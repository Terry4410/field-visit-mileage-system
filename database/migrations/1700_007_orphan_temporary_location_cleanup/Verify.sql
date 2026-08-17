SET NOCOUNT ON;

SELECT
    VersionNumber,
    Description,
    AppliedAt,
    AppliedBy
FROM dbo.SchemaVersions
WHERE VersionNumber = N'1.7.0-007';

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
    AND l.ApprovalStatus = N'Pending'
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

SELECT
    l.LocationId,
    l.LocationName,
    l.ApprovalStatus,
    l.GeocodingStatus,
    l.IsActive
FROM dbo.Locations l
WHERE l.LocationName = N'UAT刪除草稿測試地點';
