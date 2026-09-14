SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-003')
    THROW 53500, N'Verify failed: 找不到 SchemaVersion 1.8.0-003。', 1;

IF OBJECT_ID(N'dbo.DeploymentSites', N'U') IS NULL
   OR OBJECT_ID(N'dbo.DeploymentSiteLocationAssignments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.TeamDeploymentSiteAssignments', N'U') IS NULL
   OR OBJECT_ID(N'dbo.EmploymentDeploymentSiteAssignments', N'U') IS NULL
    THROW 53501, N'Verify failed: 1.8.0-003 必要資料表不存在。', 1;

IF COL_LENGTH(N'dbo.VisitTrips', N'StartDeploymentSiteId') IS NULL
   OR COL_LENGTH(N'dbo.VisitTrips', N'EndDeploymentSiteId') IS NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'StartDeploymentSiteCodeSnapshot') IS NULL
   OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'EndDeploymentSiteCodeSnapshot') IS NULL
    THROW 53502, N'Verify failed: Trip/快照派駐點欄位不存在。', 1;

IF OBJECT_ID(N'dbo.TR_DeploymentSites_CenterPeriod', N'TR') IS NULL
   OR OBJECT_ID(N'dbo.TR_DeploymentSiteLocations_NoOverlap', N'TR') IS NULL
   OR OBJECT_ID(N'dbo.TR_TeamDeploymentSites_NoOverlap', N'TR') IS NULL
   OR OBJECT_ID(N'dbo.TR_EmploymentDeploymentSites_SameSiteNoOverlap', N'TR') IS NULL
   OR OBJECT_ID(N'dbo.TR_EmploymentDeploymentSites_OnePrimary', N'TR') IS NULL
   OR OBJECT_ID(N'dbo.TR_TeamCenterAssignments_ProtectTeamSites', N'TR') IS NULL
   OR OBJECT_ID(N'dbo.TR_DeploymentSites_ProtectTeamSites', N'TR') IS NULL
    THROW 53510, N'Verify failed: 1.8.0-003 effective-date/reverse-protection trigger 不完整。', 1;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DeploymentSites') AND name=N'IX_DeploymentSites_Center_Effective')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TeamDeploymentSiteAssignments') AND name=N'IX_TeamDeploymentSiteAssignments_Team_AsOf')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.EmploymentDeploymentSiteAssignments') AND name=N'IX_EmploymentDeploymentSiteAssignments_AsOf')
    THROW 53511, N'Verify failed: 1.8.0-003 必要 index 不存在。', 1;

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.DeploymentSites'), OBJECT_ID(N'dbo.DeploymentSiteLocationAssignments'),
        OBJECT_ID(N'dbo.TeamDeploymentSiteAssignments'), OBJECT_ID(N'dbo.EmploymentDeploymentSiteAssignments')
    )
      AND (is_disabled=1 OR is_not_trusted=1)
)
   OR EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.DeploymentSites'), OBJECT_ID(N'dbo.DeploymentSiteLocationAssignments'),
        OBJECT_ID(N'dbo.TeamDeploymentSiteAssignments'), OBJECT_ID(N'dbo.EmploymentDeploymentSiteAssignments')
    )
      AND (is_disabled=1 OR is_not_trusted=1)
)
    THROW 53512, N'Verify failed: 1.8.0-003 FK/CHECK constraint 未啟用或不受信任。', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.DeploymentSiteLocationAssignments a
    JOIN dbo.DeploymentSiteLocationAssignments b
      ON b.DeploymentSiteId = a.DeploymentSiteId
     AND b.DeploymentSiteLocationAssignmentId > a.DeploymentSiteLocationAssignmentId
     AND a.EffectiveFrom <= ISNULL(b.EffectiveTo, CONVERT(date, N'99991231'))
     AND b.EffectiveFrom <= ISNULL(a.EffectiveTo, CONVERT(date, N'99991231'))
)
    THROW 53503, N'Verify failed: Deployment Site Location periods 存在重疊。', 1;

/* A1: inclusive periods for the same Employment + DeploymentSite must never overlap. */
IF EXISTS
(
    SELECT 1 FROM dbo.EmploymentDeploymentSiteAssignments a
    JOIN dbo.EmploymentDeploymentSiteAssignments b
      ON b.EmploymentId = a.EmploymentId
     AND b.DeploymentSiteId = a.DeploymentSiteId
     AND b.EmploymentDeploymentSiteAssignmentId > a.EmploymentDeploymentSiteAssignmentId
     AND a.EffectiveFrom <= ISNULL(b.EffectiveTo, CONVERT(date, N'99991231'))
     AND b.EffectiveFrom <= ISNULL(a.EffectiveTo, CONVERT(date, N'99991231'))
)
    THROW 53513, N'Verify failed A1: 相同 Employment/Site effective periods 存在重疊。', 1;

/* A2: an Employment may have at most one Primary DeploymentSite on an effective date. */
IF EXISTS
(
    SELECT 1 FROM dbo.EmploymentDeploymentSiteAssignments a
    JOIN dbo.EmploymentDeploymentSiteAssignments b
      ON b.EmploymentId = a.EmploymentId
     AND b.EmploymentDeploymentSiteAssignmentId > a.EmploymentDeploymentSiteAssignmentId
     AND a.IsPrimary = 1 AND b.IsPrimary = 1
     AND a.EffectiveFrom <= ISNULL(b.EffectiveTo, CONVERT(date, N'99991231'))
     AND b.EffectiveFrom <= ISNULL(a.EffectiveTo, CONVERT(date, N'99991231'))
)
    THROW 53504, N'Verify failed A2: 同期間存在多個 Primary Deployment Site。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.DeploymentSites s
    JOIN dbo.Centers c ON c.CenterId = s.CenterId
    WHERE s.EffectiveFrom < c.EffectiveFrom
       OR ISNULL(s.EffectiveTo, CONVERT(date, N'99991231')) > ISNULL(c.EffectiveTo, CONVERT(date, N'99991231'))
)
    THROW 53505, N'Verify failed: Deployment Site 有效期間超出所屬 Center。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.DeploymentSiteLocationAssignments a
    JOIN dbo.DeploymentSites s ON s.DeploymentSiteId = a.DeploymentSiteId
    WHERE a.EffectiveFrom < s.EffectiveFrom
       OR ISNULL(a.EffectiveTo, CONVERT(date, N'99991231')) > ISNULL(s.EffectiveTo, CONVERT(date, N'99991231'))
)
    THROW 53507, N'Verify failed: Site Location period 超出 Site 有效期間。', 1;

/* B1: every Team-Site period must be fully covered by Team-Center for the Site Center. */
IF EXISTS
(
    SELECT 1
    FROM dbo.TeamDeploymentSiteAssignments a
    JOIN dbo.DeploymentSites s ON s.DeploymentSiteId = a.DeploymentSiteId
    WHERE a.EffectiveFrom < s.EffectiveFrom
       OR ISNULL(a.EffectiveTo, CONVERT(date, N'99991231')) > ISNULL(s.EffectiveTo, CONVERT(date, N'99991231'))
       OR NOT EXISTS
          (
              SELECT 1
              FROM dbo.TeamCenterAssignments tc
              WHERE tc.TeamId = a.TeamId
                AND tc.CenterId = s.CenterId
                AND tc.EffectiveFrom <= a.EffectiveFrom
                AND ISNULL(tc.EffectiveTo, CONVERT(date, N'99991231')) >= ISNULL(a.EffectiveTo, CONVERT(date, N'99991231'))
          )
)
    THROW 53508, N'Verify failed B1: Team-Site Center 或完整有效期間 coverage 不一致。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.EmploymentDeploymentSiteAssignments a
    JOIN dbo.DeploymentSites s ON s.DeploymentSiteId = a.DeploymentSiteId
    WHERE a.EffectiveFrom < s.EffectiveFrom
       OR ISNULL(a.EffectiveTo, CONVERT(date, N'99991231')) > ISNULL(s.EffectiveTo, CONVERT(date, N'99991231'))
)
    THROW 53509, N'Verify failed: Employment-Site period 超出 Site 有效期間。', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshots)
   < (SELECT VisitTripSnapshotCount FROM dbo.SchemaMigrationDataBaselines WHERE MigrationVersion = N'1.8.0-001')
    THROW 53506, N'Verify failed: v1.7.2 Snapshot 筆數少於 migration baseline。', 1;

SELECT
    N'PASS' AS VerifyStatus,
    DB_NAME() AS DatabaseName,
    N'1.8.0-003' AS MigrationVersion,
    (SELECT COUNT_BIG(*) FROM dbo.DeploymentSites) AS DeploymentSiteCount,
    (SELECT COUNT_BIG(*) FROM dbo.TeamDeploymentSiteAssignments) AS TeamSiteAssignmentCount,
    (SELECT COUNT_BIG(*) FROM dbo.EmploymentDeploymentSiteAssignments) AS EmploymentSiteAssignmentCount;

SELECT
    s.SiteCode,
    s.SiteName,
    c.CenterCode,
    la.LocationId,
    l.LocationName,
    la.EffectiveFrom,
    la.EffectiveTo
FROM dbo.DeploymentSites s
JOIN dbo.Centers c ON c.CenterId = s.CenterId
LEFT JOIN dbo.DeploymentSiteLocationAssignments la ON la.DeploymentSiteId = s.DeploymentSiteId
LEFT JOIN dbo.Locations l ON l.LocationId = la.LocationId
ORDER BY c.CenterCode, s.SiteCode, la.EffectiveFrom;
