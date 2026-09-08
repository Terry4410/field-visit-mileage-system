SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-007')
    THROW 54300, N'Verify failed: 找不到 SchemaVersion 1.8.0-007。', 1;

IF OBJECT_ID(N'dbo.RouteCalculationAttempts', N'U') IS NULL
   OR OBJECT_ID(N'dbo.GeocodingAttempts', N'U') IS NULL
   OR OBJECT_ID(N'dbo.MileageGovernanceEvents', N'U') IS NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'SelectedRouteCalculationAttemptId') IS NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'DistanceDecisionGovernanceVersion') IS NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'ApprovedDistanceSource') IS NULL
   OR COL_LENGTH(N'dbo.MileageCalculations', N'ApprovalBasisHash') IS NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'MileageRouteAttemptIdSnapshot') IS NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'ApprovedDistanceSourceSnapshot') IS NULL
   OR COL_LENGTH(N'dbo.Locations', N'SelectedGeocodingAttemptId') IS NULL
    THROW 54301, N'Verify failed: mileage / Google governance schema 不完整。', 1;

IF OBJECT_ID(N'dbo.TR_RouteCalculationAttempts_BasisTrip', N'TR') IS NULL
   OR OBJECT_ID(N'dbo.TR_Locations_SelectedGeocodingAttempt', N'TR') IS NULL
   OR OBJECT_ID(N'dbo.TR_MileageCalculations_SelectedRouteTrip', N'TR') IS NULL
   OR OBJECT_ID(N'dbo.TR_MileageCalculations_DecisionEvidence', N'TR') IS NULL
   OR OBJECT_ID(N'dbo.TR_VisitTripSnapshots_RouteAttemptTrip', N'TR') IS NULL
    THROW 54310, N'Verify failed: 1.8.0-007 cross-entity guard trigger 不完整。', 1;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.GeocodingAttempts') AND name=N'IX_GeocodingAttempts_Location_Requested')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.RouteCalculationAttempts') AND name=N'IX_RouteCalculationAttempts_Trip_Requested')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MileageGovernanceEvents') AND name=N'IX_MileageGovernanceEvents_Trip_Occurred')
    THROW 54311, N'Verify failed: 1.8.0-007 必要 index 不存在。', 1;

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.GeocodingAttempts'), OBJECT_ID(N'dbo.RouteCalculationAttempts'),
        OBJECT_ID(N'dbo.MileageGovernanceEvents'), OBJECT_ID(N'dbo.MileageCalculations'),
        OBJECT_ID(N'dbo.VisitTripSnapshots'), OBJECT_ID(N'dbo.Locations')
    )
      AND (is_disabled=1 OR is_not_trusted=1)
)
   OR EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.GeocodingAttempts'), OBJECT_ID(N'dbo.RouteCalculationAttempts'),
        OBJECT_ID(N'dbo.MileageGovernanceEvents'), OBJECT_ID(N'dbo.VisitTripSnapshots'),
        OBJECT_ID(N'dbo.VisitTripSnapshotStops')
    )
      AND (is_disabled=1 OR is_not_trusted=1)
)
    THROW 54312, N'Verify failed: 1.8.0-007 FK/CHECK constraint 未啟用或不受信任。', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.RouteCalculationAttempts
    WHERE (RequestedVehicleType = N'Car' AND TravelMode <> N'DRIVE')
       OR (RequestedVehicleType = N'Motorcycle' AND TravelMode <> N'TWO_WHEELER')
)
    THROW 54302, N'Verify failed: VehicleType 與 Google travel mode mapping 不正確。', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.RouteCalculationAttempts
    WHERE CalculationReason = N'LeaderRetry'
      AND (BasisType <> N'SubmittedSnapshot' OR BasisVisitTripSnapshotId IS NULL)
)
    THROW 54303, N'Verify failed: Leader retry 未使用 submitted Trip Snapshot。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.RouteCalculationAttempts a
    JOIN dbo.VisitTripSnapshots s ON s.VisitTripSnapshotId = a.BasisVisitTripSnapshotId
    WHERE a.VisitTripId <> s.VisitTripId
)
    THROW 54307, N'Verify failed: Route basis Snapshot 不屬於同一 Trip。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.MileageCalculations m
    JOIN dbo.RouteCalculationAttempts a ON a.RouteCalculationAttemptId = m.SelectedRouteCalculationAttemptId
    WHERE a.VisitTripId <> m.VisitTripId OR a.Status <> N'Succeeded'
)
    THROW 54308, N'Verify failed: selected Route attempt 與 Mileage/Trip 不一致。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Locations l
    JOIN dbo.GeocodingAttempts g ON g.GeocodingAttemptId = l.SelectedGeocodingAttemptId
    WHERE g.LocationId <> l.LocationId OR g.Status <> N'Succeeded'
)
    THROW 54309, N'Verify failed: selected Geocoding attempt 與 Location 不一致。', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.MileageCalculations
    WHERE DistanceDecisionGovernanceVersion = N'1.8.0'
      AND
      (
          ApprovedDistanceSource IS NULL OR ApprovalBasisCode IS NULL OR ApprovalBasisHash IS NULL
          OR DistanceApprovedAt IS NULL OR DistanceApprovedByUserId IS NULL
      )
)
    THROW 54313, N'Verify failed: ApprovedDistanceKm 缺少公司核定來源、basis hash、核定人或時間。', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id IN(OBJECT_ID(N'dbo.RouteCalculationAttempts'), OBJECT_ID(N'dbo.GeocodingAttempts'), OBJECT_ID(N'dbo.MileageGovernanceEvents'))
      AND
      (
          name LIKE N'%Polyline%'
          OR name LIKE N'%TurnByTurn%'
          OR name LIKE N'%Alternative%'
          OR name LIKE N'%Optimized%'
          OR name IN
          (
              N'ResponseJson', N'RawResponse', N'RoutePayload', N'ProviderRequestId',
              N'DistanceMeters', N'DurationSeconds', N'Latitude', N'Longitude',
              N'LatitudeSnapshot', N'LongitudeSnapshot'
          )
      )
)
    THROW 54304, N'Verify failed: 未經 IT/Legal retention 核准，不得永久保存 Google-derived coordinates、distance、duration、provider request id、完整路線或 response。', 1;

IF COL_LENGTH(N'dbo.VisitTripSnapshotStops', N'LatitudeSnapshot') IS NOT NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshotStops', N'LongitudeSnapshot') IS NOT NULL
    THROW 54314, N'Verify failed: Trip Snapshot 不得永久保存 Google-derived coordinates。', 1;

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.RouteCalculationAttempts'),
        OBJECT_ID(N'dbo.GeocodingAttempts'),
        OBJECT_ID(N'dbo.MileageGovernanceEvents'),
        OBJECT_ID(N'dbo.MileageCalculations'),
        OBJECT_ID(N'dbo.VisitTripSnapshots')
    )
      AND delete_referential_action_desc <> N'NO_ACTION'
)
    THROW 54305, N'Verify failed: Mileage/Snapshot audit FK 不得 Cascade Delete。', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.VisitTrips)
   < (SELECT VisitTripCount FROM dbo.SchemaMigrationDataBaselines WHERE MigrationVersion = N'1.8.0-001')
   OR (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshots)
   < (SELECT VisitTripSnapshotCount FROM dbo.SchemaMigrationDataBaselines WHERE MigrationVersion = N'1.8.0-001')
   OR (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshotStops)
   < (SELECT VisitTripSnapshotStopCount FROM dbo.SchemaMigrationDataBaselines WHERE MigrationVersion = N'1.8.0-001')
    THROW 54306, N'Verify failed: v1.7.2 Trip/Snapshot 筆數少於 migration baseline。', 1;

SELECT
    N'PASS' AS VerifyStatus,
    DB_NAME() AS DatabaseName,
    N'1.8.0-007' AS MigrationVersion,
    (SELECT COUNT_BIG(*) FROM dbo.GeocodingAttempts) AS GeocodingAttemptCount,
    (SELECT COUNT_BIG(*) FROM dbo.RouteCalculationAttempts) AS RouteAttemptCount,
    (SELECT COUNT_BIG(*) FROM dbo.RouteCalculationAttempts WHERE Status=N'Succeeded') AS SuccessfulAttemptCount,
    (SELECT COUNT_BIG(*) FROM dbo.RouteCalculationAttempts WHERE Status=N'Failed') AS FailedAttemptCount,
    (SELECT COUNT_BIG(*) FROM dbo.MileageGovernanceEvents WHERE EventType=N'ManualFallback') AS ManualFallbackCount,
    (SELECT COUNT_BIG(*) FROM dbo.RouteCalculationAttempts WHERE CalculationReason=N'LeaderRetry') AS LeaderRetryCount;

SELECT TOP(20)
    RouteCalculationAttemptId,
    VisitTripId,
    BasisType,
    BasisVisitTripSnapshotId,
    CalculationReason,
    RequestedVehicleType,
    TravelMode,
    Provider,
    Status,
    ErrorCode,
    CorrelationId,
    RequestedAt,
    CompletedAt
FROM dbo.RouteCalculationAttempts
ORDER BY RouteCalculationAttemptId DESC;
