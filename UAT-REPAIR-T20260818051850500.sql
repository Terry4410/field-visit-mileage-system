SET XACT_ABORT ON;

DECLARE @TripNo nvarchar(50) = N'T20260818051850500';
DECLARE @TripId bigint;
DECLARE @V1Id bigint;
DECLARE @V2Id bigint;
DECLARE @V3Id bigint;
DECLARE @ActorUserId int;

SELECT @TripId = VisitTripId
FROM dbo.VisitTrips
WHERE TripNo = @TripNo;

IF @TripId IS NULL
    THROW 51001, N'UAT repair aborted: target TripNo not found.', 1;

SELECT @V1Id = VisitTripSnapshotId
FROM dbo.VisitTripSnapshots
WHERE VisitTripId = @TripId
  AND SnapshotVersion = 1;

SELECT @V2Id = VisitTripSnapshotId
FROM dbo.VisitTripSnapshots
WHERE VisitTripId = @TripId
  AND SnapshotVersion = 2;

IF @V1Id IS NULL OR @V2Id IS NULL
    THROW 51002, N'UAT repair aborted: expected Snapshot V1 and V2 were not found.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.VisitTripSnapshots
    WHERE VisitTripSnapshotId = @V1Id
      AND VisitDate = '2026-08-18'
      AND RatePerKmSnapshot = 3.00
      AND SubsidyAmountSnapshot = 57.90
)
    THROW 51003, N'UAT repair aborted: V1 does not match the expected $3.00 / $57.90 baseline.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.VisitTripSnapshots
    WHERE VisitTripSnapshotId = @V2Id
      AND VisitDate = '2026-08-18'
      AND ApprovedDistanceKmSnapshot = 19.00
      AND RatePerKmSnapshot = 2.50
      AND SubsidyAmountSnapshot = 47.50
)
    THROW 51004, N'UAT repair aborted: V2 does not match the reproduced defect.', 1;

SELECT @V3Id = VisitTripSnapshotId
FROM dbo.VisitTripSnapshots
WHERE VisitTripId = @TripId
  AND SnapshotVersion = 3
  AND SnapshotType = N'CorrectionRepair'
  AND ApprovedDistanceKmSnapshot = 19.00
  AND RatePerKmSnapshot = 3.00
  AND SubsidyAmountSnapshot = 57.00;

IF @V3Id IS NOT NULL
BEGIN
    PRINT N'UAT correction repair already exists; no changes made.';
    SELECT SnapshotVersion, SnapshotType, VisitDate,
           ApprovedDistanceKmSnapshot, RatePerKmSnapshot, SubsidyAmountSnapshot
    FROM dbo.VisitTripSnapshots
    WHERE VisitTripId = @TripId
    ORDER BY SnapshotVersion;
    RETURN;
END;

IF EXISTS (
    SELECT 1
    FROM dbo.VisitTripSnapshots
    WHERE VisitTripId = @TripId
      AND SnapshotVersion >= 3
)
    THROW 51005, N'UAT repair aborted: Snapshot V3 or later already exists with unexpected content.', 1;

SELECT TOP (1)
    @ActorUserId = AdminClosedByUserId
FROM dbo.CorrectionRequests
WHERE VisitTripId = @TripId
  AND ResultSnapshotId = @V2Id
  AND Status = N'Closed'
ORDER BY CorrectionRequestId DESC;

IF @ActorUserId IS NULL
BEGIN
    SELECT TOP (1) @ActorUserId = UserId
    FROM dbo.Users
    WHERE EmployeeNo = N'admin01';
END;

BEGIN TRY
    BEGIN TRANSACTION;

    INSERT dbo.VisitTripSnapshots(
        VisitTripId, SnapshotVersion, SnapshotType, TripNo, UserId,
        EmployeeNoSnapshot, DisplayNameSnapshot, OrganizationId, OrganizationNameSnapshot,
        TeamId, TeamNameSnapshot, VisitDate, StartTime, EndTime, StatusSnapshot,
        VehicleTypeSnapshot, ClaimedDistanceKmSnapshot, SystemDistanceKmSnapshot,
        ApprovedDistanceKmSnapshot, RatePerKmSnapshot, SubsidyAmountSnapshot,
        RouteProviderSnapshot, SubmittedAtSnapshot, ApprovedAtSnapshot,
        ApproverUserId, ApproverNameSnapshot, NotesSnapshot, CreatedAt, CreatedByUserId
    )
    SELECT
        VisitTripId, 3, N'CorrectionRepair', TripNo, UserId,
        EmployeeNoSnapshot, DisplayNameSnapshot, OrganizationId, OrganizationNameSnapshot,
        TeamId, TeamNameSnapshot, VisitDate, StartTime, EndTime, StatusSnapshot,
        VehicleTypeSnapshot, ClaimedDistanceKmSnapshot, SystemDistanceKmSnapshot,
        ApprovedDistanceKmSnapshot, 3.00,
        CAST(ApprovedDistanceKmSnapshot * 3.00 AS decimal(12,2)),
        RouteProviderSnapshot, SubmittedAtSnapshot, ApprovedAtSnapshot,
        ApproverUserId, ApproverNameSnapshot, NotesSnapshot,
        SYSUTCDATETIME(), @ActorUserId
    FROM dbo.VisitTripSnapshots
    WHERE VisitTripSnapshotId = @V2Id;

    SET @V3Id = SCOPE_IDENTITY();

    INSERT dbo.VisitTripSnapshotStops(
        VisitTripSnapshotId, StopSequence, LocationId, LocationCodeSnapshot,
        LocationNameSnapshot, AddressSnapshot, ProjectId, ProjectCodeSnapshot,
        ProjectNameSnapshot, VisitTypeId, VisitTypeCodeSnapshot,
        VisitTypeNameSnapshot, VisitPurposeSnapshot, NotesSnapshot, CreatedAt
    )
    SELECT
        @V3Id, StopSequence, LocationId, LocationCodeSnapshot,
        LocationNameSnapshot, AddressSnapshot, ProjectId, ProjectCodeSnapshot,
        ProjectNameSnapshot, VisitTypeId, VisitTypeCodeSnapshot,
        VisitTypeNameSnapshot, VisitPurposeSnapshot, NotesSnapshot,
        SYSUTCDATETIME()
    FROM dbo.VisitTripSnapshotStops
    WHERE VisitTripSnapshotId = @V2Id;

    COMMIT TRANSACTION;
    PRINT N'UAT repair completed: V1/V2 preserved; correct V3 created.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT SnapshotVersion, SnapshotType, VisitDate,
       ClaimedDistanceKmSnapshot, SystemDistanceKmSnapshot,
       ApprovedDistanceKmSnapshot, RatePerKmSnapshot, SubsidyAmountSnapshot
FROM dbo.VisitTripSnapshots
WHERE VisitTripId = @TripId
ORDER BY SnapshotVersion;
