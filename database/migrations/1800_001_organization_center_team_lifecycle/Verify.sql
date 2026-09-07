SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS
(
    SELECT 1 FROM dbo.SchemaVersions
    WHERE VersionNumber = N'1.8.0-001'
)
    THROW 53100, N'Verify failed: 找不到 SchemaVersion 1.8.0-001。', 1;

IF OBJECT_ID(N'dbo.Centers', N'U') IS NULL
   OR OBJECT_ID(N'dbo.TeamCenterAssignments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.SchemaMigrationDataBaselines', N'U') IS NULL
    THROW 53101, N'Verify failed: 1.8.0-001 必要資料表不存在。', 1;

IF COL_LENGTH(N'dbo.Organizations', N'RowVersion') IS NULL
   OR COL_LENGTH(N'dbo.Teams', N'EffectiveFrom') IS NULL
   OR COL_LENGTH(N'dbo.Teams', N'EffectiveTo') IS NULL
   OR COL_LENGTH(N'dbo.Teams', N'RowVersion') IS NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'CenterIdSnapshot') IS NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'TeamCodeSnapshot') IS NULL
    THROW 53102, N'Verify failed: 1.8.0-001 必要欄位不存在。', 1;

IF OBJECT_ID(N'dbo.TR_TeamCenterAssignments_NoOverlap', N'TR') IS NULL
    THROW 53103, N'Verify failed: Team-Center overlap trigger 不存在。', 1;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Teams') AND name=N'UX_Teams_Organization_TeamCode')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Centers') AND name=N'IX_Centers_Organization_Effective')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TeamCenterAssignments') AND name=N'IX_TeamCenterAssignments_Center_Effective')
    THROW 53111, N'Verify failed: 1.8.0-001 必要 index 不存在。', 1;

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id IN(OBJECT_ID(N'dbo.Organizations'), OBJECT_ID(N'dbo.Teams'), OBJECT_ID(N'dbo.Centers'), OBJECT_ID(N'dbo.TeamCenterAssignments'))
      AND (is_disabled=1 OR is_not_trusted=1)
)
   OR EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id IN(OBJECT_ID(N'dbo.Teams'), OBJECT_ID(N'dbo.Centers'), OBJECT_ID(N'dbo.TeamCenterAssignments'))
      AND (is_disabled=1 OR is_not_trusted=1)
)
    THROW 53112, N'Verify failed: 1.8.0-001 FK/CHECK constraint 未啟用或不受信任。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.TeamCenterAssignments a
    JOIN dbo.TeamCenterAssignments b
      ON b.TeamId = a.TeamId
     AND b.TeamCenterAssignmentId > a.TeamCenterAssignmentId
     AND a.EffectiveFrom <= ISNULL(b.EffectiveTo, CONVERT(date, N'99991231'))
     AND b.EffectiveFrom <= ISNULL(a.EffectiveTo, CONVERT(date, N'99991231'))
)
    THROW 53104, N'Verify failed: Team-Center effective periods 存在重疊。', 1;

IF EXISTS
(
    SELECT OrganizationId, TeamCode
    FROM dbo.Teams
    GROUP BY OrganizationId, TeamCode
    HAVING COUNT(*) > 1
)
    THROW 53105, N'Verify failed: Organization 內存在重複 TeamCode。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.TeamCenterAssignments a
    JOIN dbo.Teams t ON t.TeamId = a.TeamId
    JOIN dbo.Centers c ON c.CenterId = a.CenterId
    WHERE t.OrganizationId <> c.OrganizationId
       OR (t.EffectiveFrom IS NOT NULL AND a.EffectiveFrom < t.EffectiveFrom)
       OR (t.EffectiveTo IS NOT NULL AND ISNULL(a.EffectiveTo, CONVERT(date, N'99991231')) > t.EffectiveTo)
       OR a.EffectiveFrom < c.EffectiveFrom
       OR ISNULL(a.EffectiveTo, CONVERT(date, N'99991231')) > ISNULL(c.EffectiveTo, CONVERT(date, N'99991231'))
)
    THROW 53110, N'Verify failed: Team-Center Organization 或有效期間不一致。', 1;

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
    THROW 53106, N'Verify failed: 找不到 1.8.0-001 歷史資料 baseline。', 1;

IF @TripCount <> (SELECT COUNT_BIG(*) FROM dbo.VisitTrips WHERE VisitTripId <= @MaxTripId)
   OR @TripHash <> HASHBYTES(N'SHA2_256', CONVERT(VARBINARY(MAX), COALESCE((
        SELECT VisitTripId, TripNo, UserId, OrganizationId, TeamId, VisitDate,
               StartTime, EndTime, HasTimeOverlapWarning, TimeOverlapConfirmed,
               Status, VehicleType, Purpose, Notes, ReturnReason, SubmittedAt,
               ApprovedAt, CreatedAt, CreatedByUserId, UpdatedAt, UpdatedByUserId
        FROM dbo.VisitTrips WHERE VisitTripId <= @MaxTripId
        ORDER BY VisitTripId FOR JSON PATH, INCLUDE_NULL_VALUES
   ), N'[]')))
    THROW 53107, N'Verify failed: 既有 v1.7.2 VisitTrips 被改變或遺失。', 1;

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
        FROM dbo.VisitTripSnapshots WHERE VisitTripSnapshotId <= @MaxSnapshotId
        ORDER BY VisitTripSnapshotId FOR JSON PATH, INCLUDE_NULL_VALUES
   ), N'[]')))
    THROW 53108, N'Verify failed: 既有 v1.7.2 VisitTripSnapshots 被改變或遺失。', 1;

IF @SnapshotStopCount <> (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshotStops WHERE VisitTripSnapshotStopId <= @MaxSnapshotStopId)
   OR @SnapshotStopHash <> HASHBYTES(N'SHA2_256', CONVERT(VARBINARY(MAX), COALESCE((
        SELECT VisitTripSnapshotStopId, VisitTripSnapshotId, StopSequence, LocationId,
               LocationCodeSnapshot, LocationNameSnapshot, AddressSnapshot,
               ProjectId, ProjectCodeSnapshot, ProjectNameSnapshot, VisitTypeId,
               VisitTypeCodeSnapshot, VisitTypeNameSnapshot, VisitPurposeSnapshot,
               NotesSnapshot, CreatedAt
        FROM dbo.VisitTripSnapshotStops WHERE VisitTripSnapshotStopId <= @MaxSnapshotStopId
        ORDER BY VisitTripSnapshotStopId FOR JSON PATH, INCLUDE_NULL_VALUES
   ), N'[]')))
    THROW 53109, N'Verify failed: 既有 v1.7.2 VisitTripSnapshotStops 被改變或遺失。', 1;

SELECT
    N'PASS' AS VerifyStatus,
    DB_NAME() AS DatabaseName,
    N'1.8.0-001' AS MigrationVersion,
    @TripCount AS PreservedTripCount,
    @SnapshotCount AS PreservedSnapshotCount,
    @SnapshotStopCount AS PreservedSnapshotStopCount;

SELECT
    o.OrganizationCode,
    t.TeamCode,
    t.TeamName,
    t.EffectiveFrom,
    t.EffectiveTo,
    c.CenterCode,
    c.CenterName
FROM dbo.Teams t
JOIN dbo.Organizations o ON o.OrganizationId = t.OrganizationId
LEFT JOIN dbo.TeamCenterAssignments a
  ON a.TeamId = t.TeamId
 AND CONVERT(date, SYSUTCDATETIME()) BETWEEN a.EffectiveFrom AND ISNULL(a.EffectiveTo, CONVERT(date, N'99991231'))
LEFT JOIN dbo.Centers c ON c.CenterId = a.CenterId
ORDER BY o.OrganizationCode, t.TeamCode;
