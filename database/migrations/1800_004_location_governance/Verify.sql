SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-004')
    THROW 53700, N'Verify failed: 找不到 SchemaVersion 1.8.0-004。', 1;

IF OBJECT_ID(N'dbo.TeamLocationNotes', N'U') IS NULL
   OR OBJECT_ID(N'dbo.TeamLocationNoteHistory', N'U') IS NULL
   OR COL_LENGTH(N'dbo.Locations', N'TaxId') IS NULL
   OR COL_LENGTH(N'dbo.Locations', N'MasterNote') IS NULL
   OR COL_LENGTH(N'dbo.Locations', N'NormalizedLocationName') IS NULL
   OR COL_LENGTH(N'dbo.Locations', N'NormalizedAddress') IS NULL
    THROW 53701, N'Verify failed: Location governance schema 不完整。', 1;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Locations') AND name=N'IX_Locations_Organization_TaxId')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Locations') AND name=N'IX_Locations_NormalizedNameAddress')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TeamLocationNotes') AND name=N'UQ_TeamLocationNotes_Team_Location')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TeamLocationNoteHistory') AND name=N'IX_TeamLocationNoteHistory_Note_Changed')
    THROW 53706, N'Verify failed: 1.8.0-004 必要 index 不存在。', 1;

IF EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id IN(OBJECT_ID(N'dbo.Locations'), OBJECT_ID(N'dbo.TeamLocationNotes'), OBJECT_ID(N'dbo.TeamLocationNoteHistory'))
      AND (is_disabled=1 OR is_not_trusted=1)
)
   OR EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id IN(OBJECT_ID(N'dbo.Locations'), OBJECT_ID(N'dbo.TeamLocationNotes'), OBJECT_ID(N'dbo.TeamLocationNoteHistory'))
      AND (is_disabled=1 OR is_not_trusted=1)
)
    THROW 53707, N'Verify failed: 1.8.0-004 FK/CHECK constraint 未啟用或不受信任。', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Locations
    WHERE DuplicateOfLocationId = LocationId
)
    THROW 53702, N'Verify failed: Location 不可標記自己為 duplicate target。', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name IN
    (
        N'FK_Locations_DuplicateOf',
        N'FK_TeamLocationNotes_Locations',
        N'FK_TeamLocationNoteHistory_Location'
    )
      AND delete_referential_action_desc <> N'NO_ACTION'
)
    THROW 53703, N'Verify failed: Location governance FK 不得 Cascade Delete。', 1;

IF EXISTS
(
    SELECT TeamId, LocationId
    FROM dbo.TeamLocationNotes
    GROUP BY TeamId, LocationId
    HAVING COUNT(*) > 1
)
    THROW 53704, N'Verify failed: Team/Location 共用備註不唯一。', 1;

IF EXISTS
(
    SELECT TaxId
    FROM dbo.Locations
    WHERE TaxId IS NOT NULL
    GROUP BY TaxId
    HAVING COUNT(*) > 1
)
BEGIN
    PRINT N'INFO: TaxId 重複是允許狀態；同一法人可有多個地址，不得視為唯一鍵。';
END;

IF (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshots)
   < (SELECT VisitTripSnapshotCount FROM dbo.SchemaMigrationDataBaselines WHERE MigrationVersion = N'1.8.0-001')
    THROW 53705, N'Verify failed: v1.7.2 Snapshot 筆數少於 migration baseline。', 1;

SELECT
    N'PASS' AS VerifyStatus,
    DB_NAME() AS DatabaseName,
    N'1.8.0-004' AS MigrationVersion,
    (SELECT COUNT_BIG(*) FROM dbo.Locations) AS LocationCount,
    (SELECT COUNT_BIG(*) FROM dbo.TeamLocationNotes) AS TeamLocationNoteCount,
    (SELECT COUNT_BIG(*) FROM dbo.Locations WHERE DuplicateOfLocationId IS NOT NULL) AS MarkedDuplicateCount;

SELECT TOP(20)
    OrganizationId,
    TaxId,
    COUNT(*) AS LocationCount
FROM dbo.Locations
WHERE TaxId IS NOT NULL
GROUP BY OrganizationId, TaxId
ORDER BY LocationCount DESC, OrganizationId, TaxId;
