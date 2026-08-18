DECLARE @TripNo nvarchar(50) = N'T20260818051850500';

SELECT
    t.TripNo,
    s.SnapshotVersion,
    s.SnapshotType,
    s.VisitDate,
    s.ApprovedDistanceKmSnapshot,
    s.RatePerKmSnapshot,
    s.SubsidyAmountSnapshot
FROM dbo.VisitTrips t
JOIN dbo.VisitTripSnapshots s
  ON s.VisitTripId = t.VisitTripId
WHERE t.TripNo = @TripNo
ORDER BY s.SnapshotVersion;

SELECT TOP (1)
    CASE
        WHEN s.SnapshotVersion = 3
         AND s.ApprovedDistanceKmSnapshot = 19.00
         AND s.RatePerKmSnapshot = 3.00
         AND s.SubsidyAmountSnapshot = 57.00
        THEN N'PASS'
        ELSE N'FAIL'
    END AS RepairResult,
    s.SnapshotVersion,
    s.ApprovedDistanceKmSnapshot,
    s.RatePerKmSnapshot,
    s.SubsidyAmountSnapshot
FROM dbo.VisitTrips t
JOIN dbo.VisitTripSnapshots s
  ON s.VisitTripId = t.VisitTripId
WHERE t.TripNo = @TripNo
ORDER BY s.SnapshotVersion DESC;
