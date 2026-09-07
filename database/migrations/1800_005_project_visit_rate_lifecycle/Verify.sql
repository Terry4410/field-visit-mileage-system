SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-005')
    THROW 53900, N'Verify failed: 找不到 SchemaVersion 1.8.0-005。', 1;

IF COL_LENGTH(N'dbo.Projects', N'RowVersion') IS NULL
   OR COL_LENGTH(N'dbo.VisitTypes', N'RowVersion') IS NULL
   OR COL_LENGTH(N'dbo.MileageRateRules', N'RowVersion') IS NULL
   OR OBJECT_ID(N'dbo.TR_MileageRateRules_NoActiveOverlap', N'TR') IS NULL
    THROW 53901, N'Verify failed: Project/VisitType/Rate lifecycle schema 不完整。', 1;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Projects') AND name=N'IX_Projects_Search')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.VisitTypes') AND name=N'IX_VisitTypes_Active_Sort')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MileageRateRules') AND name=N'UX_MileageRateRules_Scope_Vehicle_Start')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MileageRateRules') AND name=N'IX_MileageRateRules_AsOf')
    THROW 53907, N'Verify failed: 1.8.0-005 必要 index 不存在。', 1;

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id IN(OBJECT_ID(N'dbo.Projects'), OBJECT_ID(N'dbo.VisitTypes'), OBJECT_ID(N'dbo.MileageRateRules'))
      AND (is_disabled=1 OR is_not_trusted=1)
)
   OR EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id IN(OBJECT_ID(N'dbo.Projects'), OBJECT_ID(N'dbo.MileageRateRules'))
      AND (is_disabled=1 OR is_not_trusted=1)
)
    THROW 53908, N'Verify failed: 1.8.0-005 FK/CHECK constraint 未啟用或不受信任。', 1;

IF EXISTS(SELECT 1 FROM dbo.Projects WHERE EndDate IS NOT NULL AND StartDate IS NOT NULL AND EndDate < StartDate)
    THROW 53902, N'Verify failed: Project date range 無效。', 1;

IF EXISTS(SELECT 1 FROM dbo.MileageRateRules WHERE VehicleType NOT IN(N'Motorcycle', N'Car'))
    THROW 53903, N'Verify failed: Mileage Rate 包含非 Motorcycle/Car 車種。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.MileageRateRules a
    JOIN dbo.MileageRateRules b
      ON ISNULL(b.OrganizationId, -1) = ISNULL(a.OrganizationId, -1)
     AND b.VehicleType = a.VehicleType
     AND b.MileageRateRuleId > a.MileageRateRuleId
     AND a.IsActive = 1 AND b.IsActive = 1
     AND a.EffectiveFrom <= ISNULL(b.EffectiveTo, CONVERT(date, N'99991231'))
     AND b.EffectiveFrom <= ISNULL(a.EffectiveTo, CONVERT(date, N'99991231'))
)
    THROW 53904, N'Verify failed: Mileage Rate effective periods 存在重疊。', 1;

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id IN(OBJECT_ID(N'dbo.Projects'), OBJECT_ID(N'dbo.MileageRateRules'))
      AND delete_referential_action_desc <> N'NO_ACTION'
)
    THROW 53905, N'Verify failed: Project/Rate FK 不得 Cascade Delete。', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshots)
   < (SELECT VisitTripSnapshotCount FROM dbo.SchemaMigrationDataBaselines WHERE MigrationVersion = N'1.8.0-001')
    THROW 53906, N'Verify failed: v1.7.2 Snapshot 筆數少於 migration baseline。', 1;

SELECT
    N'PASS' AS VerifyStatus,
    DB_NAME() AS DatabaseName,
    N'1.8.0-005' AS MigrationVersion,
    (SELECT COUNT_BIG(*) FROM dbo.Projects) AS ProjectCount,
    (SELECT COUNT_BIG(*) FROM dbo.VisitTypes) AS VisitTypeCount,
    (SELECT COUNT_BIG(*) FROM dbo.MileageRateRules) AS MileageRateCount;

SELECT
    OrganizationId,
    VehicleType,
    EffectiveFrom,
    EffectiveTo,
    RatePerKm,
    IsActive
FROM dbo.MileageRateRules
ORDER BY OrganizationId, VehicleType, EffectiveFrom;
