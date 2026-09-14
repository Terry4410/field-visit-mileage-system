SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'db-fieldvisit-uat'
    THROW 55100, N'Historical fingerprint verification refused: unexpected database.', 1;

IF OBJECT_ID(N'dbo.SchemaMigrationDataBaselines', N'U') IS NULL
    THROW 55101, N'Historical fingerprint verification failed: baseline table missing.', 1;

DECLARE
    @MaxTripId BIGINT,
    @TripCount BIGINT,
    @TripHash VARBINARY(32),
    @MaxSnapshotId BIGINT,
    @SnapshotCount BIGINT,
    @SnapshotHash VARBINARY(32),
    @MaxSnapshotStopId BIGINT,
    @SnapshotStopCount BIGINT,
    @SnapshotStopHash VARBINARY(32);

SELECT
    @MaxTripId = MaxVisitTripId,
    @TripCount = VisitTripCount,
    @TripHash = VisitTripHash,
    @MaxSnapshotId = MaxVisitTripSnapshotId,
    @SnapshotCount = VisitTripSnapshotCount,
    @SnapshotHash = VisitTripSnapshotHash,
    @MaxSnapshotStopId = MaxVisitTripSnapshotStopId,
    @SnapshotStopCount = VisitTripSnapshotStopCount,
    @SnapshotStopHash = VisitTripSnapshotStopHash
FROM dbo.SchemaMigrationDataBaselines
WHERE MigrationVersion = N'1.8.0-001';

IF @TripHash IS NULL OR @SnapshotHash IS NULL OR @SnapshotStopHash IS NULL
    THROW 55102, N'Historical fingerprint verification failed: 1.8.0-001 baseline missing.', 1;

IF @TripCount <> (SELECT COUNT_BIG(*) FROM dbo.VisitTrips WHERE VisitTripId <= @MaxTripId)
   OR @TripHash <> HASHBYTES(N'SHA2_256', CONVERT(VARBINARY(MAX), COALESCE((
        SELECT VisitTripId, TripNo, UserId, OrganizationId, TeamId, VisitDate,
               StartTime, EndTime, HasTimeOverlapWarning, TimeOverlapConfirmed,
               Status, VehicleType, Purpose, Notes, ReturnReason, SubmittedAt,
               ApprovedAt, CreatedAt, CreatedByUserId, UpdatedAt, UpdatedByUserId
        FROM dbo.VisitTrips
        WHERE VisitTripId <= @MaxTripId
        ORDER BY VisitTripId
        FOR JSON PATH, INCLUDE_NULL_VALUES
   ), N'[]')))
    THROW 55103, N'Historical fingerprint verification failed: frozen VisitTrips changed or disappeared.', 1;

IF @SnapshotCount <> (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshots WHERE VisitTripSnapshotId <= @MaxSnapshotId)
   OR @SnapshotHash <> HASHBYTES(N'SHA2_256', CONVERT(VARBINARY(MAX), COALESCE((
        SELECT VisitTripSnapshotId, VisitTripId, SnapshotVersion, SnapshotType,
               TripNo, UserId, EmployeeNoSnapshot, DisplayNameSnapshot,
               OrganizationId, OrganizationNameSnapshot, TeamId, TeamNameSnapshot,
               VisitDate, StartTime, EndTime, StatusSnapshot, VehicleTypeSnapshot,
               ClaimedDistanceKmSnapshot, SystemDistanceKmSnapshot,
               ApprovedDistanceKmSnapshot, RatePerKmSnapshot, SubsidyAmountSnapshot,
               RouteProviderSnapshot, SubmittedAtSnapshot, ApprovedAtSnapshot,
               ApproverUserId, ApproverNameSnapshot, NotesSnapshot, CreatedAt,
               CreatedByUserId
        FROM dbo.VisitTripSnapshots
        WHERE VisitTripSnapshotId <= @MaxSnapshotId
        ORDER BY VisitTripSnapshotId
        FOR JSON PATH, INCLUDE_NULL_VALUES
   ), N'[]')))
    THROW 55104, N'Historical fingerprint verification failed: frozen VisitTripSnapshots changed or disappeared.', 1;

IF @SnapshotStopCount <> (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshotStops WHERE VisitTripSnapshotStopId <= @MaxSnapshotStopId)
   OR @SnapshotStopHash <> HASHBYTES(N'SHA2_256', CONVERT(VARBINARY(MAX), COALESCE((
        SELECT VisitTripSnapshotStopId, VisitTripSnapshotId, StopSequence, LocationId,
               LocationCodeSnapshot, LocationNameSnapshot, AddressSnapshot,
               ProjectId, ProjectCodeSnapshot, ProjectNameSnapshot, VisitTypeId,
               VisitTypeCodeSnapshot, VisitTypeNameSnapshot, VisitPurposeSnapshot,
               NotesSnapshot, CreatedAt
        FROM dbo.VisitTripSnapshotStops
        WHERE VisitTripSnapshotStopId <= @MaxSnapshotStopId
        ORDER BY VisitTripSnapshotStopId
        FOR JSON PATH, INCLUDE_NULL_VALUES
   ), N'[]')))
    THROW 55105, N'Historical fingerprint verification failed: frozen VisitTripSnapshotStops changed or disappeared.', 1;

SELECT
    N'PASS' AS VerifyStatus,
    N'V180_HISTORICAL_SHA256_FINGERPRINTS' AS GateName,
    @TripCount AS PreservedTripCount,
    @SnapshotCount AS PreservedSnapshotCount,
    @SnapshotStopCount AS PreservedSnapshotStopCount;
