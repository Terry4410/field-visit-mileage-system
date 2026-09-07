SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-002')
    THROW 53300, N'Verify failed: 找不到 SchemaVersion 1.8.0-002。', 1;

IF OBJECT_ID(N'dbo.Persons', N'U') IS NULL
   OR OBJECT_ID(N'dbo.Employments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.EmploymentStatusPeriods', N'U') IS NULL
   OR OBJECT_ID(N'dbo.EmploymentRoleAssignments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.TeamMemberships', N'U') IS NULL
   OR OBJECT_ID(N'dbo.TeamLeaderAssignments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.TeamLeaderDelegations', N'U') IS NULL
    THROW 53301, N'Verify failed: 1.8.0-002 必要資料表不存在。', 1;

IF OBJECT_ID(N'dbo.TR_EmploymentStatusPeriods_NoOverlap', N'TR') IS NULL
   OR OBJECT_ID(N'dbo.TR_EmploymentRoleAssignments_NoOverlap', N'TR') IS NULL
   OR OBJECT_ID(N'dbo.TR_TeamMemberships_OnePrimary', N'TR') IS NULL
   OR OBJECT_ID(N'dbo.TR_TeamLeaderAssignments_NoOverlap', N'TR') IS NULL
   OR OBJECT_ID(N'dbo.TR_TeamLeaderDelegations_NoOverlap', N'TR') IS NULL
    THROW 53309, N'Verify failed: 1.8.0-002 effective-date trigger 不完整。', 1;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Employments') AND name=N'UX_Employments_Organization_EmployeeNo')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.EmploymentStatusPeriods') AND name=N'IX_EmploymentStatusPeriods_AsOf')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TeamMemberships') AND name=N'IX_TeamMemberships_Employment_AsOf')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TeamLeaderAssignments') AND name=N'IX_TeamLeaderAssignments_Team_AsOf')
    THROW 53310, N'Verify failed: 1.8.0-002 必要 index 不存在。', 1;

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.Persons'), OBJECT_ID(N'dbo.Employments'), OBJECT_ID(N'dbo.EmploymentStatusPeriods'),
        OBJECT_ID(N'dbo.EmploymentRoleAssignments'), OBJECT_ID(N'dbo.TeamMemberships'),
        OBJECT_ID(N'dbo.TeamLeaderAssignments'), OBJECT_ID(N'dbo.TeamLeaderDelegations')
    )
      AND (is_disabled=1 OR is_not_trusted=1)
)
   OR EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.Employments'), OBJECT_ID(N'dbo.EmploymentStatusPeriods'),
        OBJECT_ID(N'dbo.EmploymentRoleAssignments'), OBJECT_ID(N'dbo.TeamMemberships'),
        OBJECT_ID(N'dbo.TeamLeaderAssignments'), OBJECT_ID(N'dbo.TeamLeaderDelegations')
    )
      AND (is_disabled=1 OR is_not_trusted=1)
)
    THROW 53311, N'Verify failed: 1.8.0-002 FK/CHECK constraint 未啟用或不受信任。', 1;

IF EXISTS(SELECT 1 FROM dbo.Persons GROUP BY LegacyUserId HAVING LegacyUserId IS NOT NULL AND COUNT(*) > 1)
   OR EXISTS(SELECT 1 FROM dbo.Employments GROUP BY LegacyUserId HAVING LegacyUserId IS NOT NULL AND COUNT(*) > 1)
    THROW 53302, N'Verify failed: legacy User 對 Person/Employment mapping 不唯一。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Users u
    WHERE u.OrganizationId IS NOT NULL
      AND NOT EXISTS(SELECT 1 FROM dbo.Employments e WHERE e.LegacyUserId = u.UserId)
)
    THROW 53303, N'Verify failed: Organization 內 legacy User 缺少相容 Employment。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.UserIdentityProfiles p
    JOIN dbo.Employments e ON e.LegacyUserId = p.UserId
    WHERE p.EmploymentId <> e.EmploymentId OR p.EmploymentId IS NULL
)
    THROW 53304, N'Verify failed: identity profile 未綁定對應 Employment。', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.EmploymentStatusPeriods a
    JOIN dbo.EmploymentStatusPeriods b
      ON b.EmploymentId = a.EmploymentId
     AND b.EmploymentStatusPeriodId > a.EmploymentStatusPeriodId
     AND a.EffectiveFrom <= ISNULL(b.EffectiveTo, CONVERT(date, N'99991231'))
     AND b.EffectiveFrom <= ISNULL(a.EffectiveTo, CONVERT(date, N'99991231'))
)
    THROW 53305, N'Verify failed: Employment status periods 存在重疊。', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.EmploymentRoleAssignments a
    JOIN dbo.EmploymentRoleAssignments b
      ON b.EmploymentId = a.EmploymentId
     AND b.RoleId = a.RoleId
     AND b.EmploymentRoleAssignmentId > a.EmploymentRoleAssignmentId
     AND a.EffectiveFrom <= ISNULL(b.EffectiveTo, CONVERT(date, N'99991231'))
     AND b.EffectiveFrom <= ISNULL(a.EffectiveTo, CONVERT(date, N'99991231'))
)
    THROW 53306, N'Verify failed: Role effective periods 存在重疊。', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.TeamMemberships a
    JOIN dbo.TeamMemberships b
      ON b.EmploymentId = a.EmploymentId
     AND b.TeamMembershipId > a.TeamMembershipId
     AND a.IsPrimary = 1 AND b.IsPrimary = 1
     AND a.EffectiveFrom <= ISNULL(b.EffectiveTo, CONVERT(date, N'99991231'))
     AND b.EffectiveFrom <= ISNULL(a.EffectiveTo, CONVERT(date, N'99991231'))
)
    THROW 53307, N'Verify failed: 同期間存在多個 Primary Team。', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.VisitTrips)
   < (SELECT VisitTripCount FROM dbo.SchemaMigrationDataBaselines WHERE MigrationVersion = N'1.8.0-001')
   OR (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshots)
   < (SELECT VisitTripSnapshotCount FROM dbo.SchemaMigrationDataBaselines WHERE MigrationVersion = N'1.8.0-001')
    THROW 53308, N'Verify failed: v1.7.2 Trip/Snapshot 筆數少於 migration baseline。', 1;

SELECT
    N'PASS' AS VerifyStatus,
    DB_NAME() AS DatabaseName,
    N'1.8.0-002' AS MigrationVersion,
    (SELECT COUNT_BIG(*) FROM dbo.Persons) AS PersonCount,
    (SELECT COUNT_BIG(*) FROM dbo.Employments) AS EmploymentCount,
    (SELECT COUNT_BIG(*) FROM dbo.TeamMemberships) AS MembershipCount,
    (SELECT COUNT_BIG(*) FROM dbo.TeamLeaderAssignments) AS LeaderAssignmentCount;

SELECT
    u.UserId,
    u.EmployeeNo AS LegacyEmployeeNo,
    p.PersonId,
    e.EmploymentId,
    e.EmployeeNo,
    e.HireDate,
    e.TerminationDate,
    CASE WHEN EXISTS(SELECT 1 FROM dbo.EmploymentStatusPeriods s WHERE s.EmploymentId=e.EmploymentId) THEN N'AuthoritativePeriodsCopied' ELSE N'LegacyEligibleNoInventedDates' END AS BackfillStatus
FROM dbo.Users u
LEFT JOIN dbo.Persons p ON p.LegacyUserId = u.UserId
LEFT JOIN dbo.Employments e ON e.LegacyUserId = u.UserId
ORDER BY u.UserId;
