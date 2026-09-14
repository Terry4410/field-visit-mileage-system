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

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.TeamLocationNotes')
      AND name = N'Note'
      AND is_nullable = 0
)
    THROW 53708, N'Verify failed: TeamLocationNotes.Note 必須允許 NULL 作為 Cleared tombstone。', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.TeamLocationNotes')
      AND name = N'Note'
      AND is_nullable = 1
)
    THROW 53709, N'Verify failed: TeamLocationNotes.Note 欄位不存在或 nullable metadata 不正確。', 1;

IF COL_LENGTH(N'dbo.TeamLocationNotes', N'RowVersion') IS NULL
    THROW 53710, N'Verify failed: TeamLocationNotes.RowVersion 不存在。', 1;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Locations') AND name=N'IX_Locations_Organization_TaxId')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Locations') AND name=N'IX_Locations_NormalizedNameAddress')
   OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TeamLocationNotes') AND name=N'UQ_TeamLocationNotes_Team_Location' AND is_unique=1)
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
        N'FK_TeamLocationNoteHistory_Location',
        N'FK_TeamLocationNoteHistory_Note'
    )
      AND delete_referential_action_desc <> N'NO_ACTION'
)
    THROW 53703, N'Verify failed: Location governance / Note History FK 不得 Cascade Delete。', 1;

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
    SELECT 1
    FROM dbo.TeamLocationNotes
    WHERE Note IS NOT NULL
      AND LEN(LTRIM(RTRIM(Note))) = 0
)
    THROW 53711, N'Verify failed: TeamLocationNotes.Note 不得為 empty/whitespace；NULL 才代表 Cleared。', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.TeamLocationNotes')
      AND name = N'CK_TeamLocationNotes_NotBlank'
      AND is_disabled = 0
      AND is_not_trusted = 0
      AND LOWER(definition) LIKE N'%note%is null%'
      AND LOWER(definition) LIKE N'%len%'
      AND LOWER(definition) LIKE N'%ltrim%'
      AND LOWER(definition) LIKE N'%rtrim%'
)
    THROW 53712, N'Verify failed: TeamLocationNotes nullable/nonblank constraint definition 不正確。', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.TeamLocationNoteHistory')
      AND name = N'CK_TeamLocationNoteHistory_Action'
      AND is_disabled = 0
      AND is_not_trusted = 0
      AND definition LIKE N'%Created%'
      AND definition LIKE N'%Updated%'
      AND definition LIKE N'%Cleared%'
)
    THROW 53713, N'Verify failed: TeamLocationNoteHistory Action 必須維持 Created / Updated / Cleared。', 1;

DECLARE @BusinessToday date =
    CONVERT(date, DATEADD(HOUR, 8, SYSUTCDATETIME()));

IF EXISTS
(
    SELECT 1
    FROM dbo.Locations l
    JOIN dbo.DeploymentSiteLocationAssignments a
      ON a.LocationId = l.LocationId
    WHERE l.IsActive = 0
      AND
      (
          a.EffectiveTo IS NULL
          OR a.EffectiveTo >= @BusinessToday
      )
)
    THROW 53714, N'Verify failed: inactive Location 不得存在 current/future Deployment Site Location assignment。', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.triggers t
    JOIN sys.objects p ON p.object_id = t.parent_id
    JOIN sys.schemas s ON s.schema_id = p.schema_id
    WHERE t.object_id = OBJECT_ID(N'dbo.TR_Locations_ProtectCurrentDeploymentSiteLocations')
      AND t.name = N'TR_Locations_ProtectCurrentDeploymentSiteLocations'
      AND s.name = N'dbo'
      AND p.name = N'Locations'
      AND t.is_disabled = 0
)
    THROW 53715, N'Verify failed: Location reverse-protection trigger 不存在、parent 不正確或已停用。', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.triggers t
    JOIN sys.objects p ON p.object_id = t.parent_id
    JOIN sys.schemas s ON s.schema_id = p.schema_id
    WHERE t.object_id = OBJECT_ID(N'dbo.TR_DeploymentSiteLocationAssignments_ProtectActiveLocation')
      AND t.name = N'TR_DeploymentSiteLocationAssignments_ProtectActiveLocation'
      AND s.name = N'dbo'
      AND p.name = N'DeploymentSiteLocationAssignments'
      AND t.is_disabled = 0
)
    THROW 53716, N'Verify failed: assignment forward-protection trigger 不存在、parent 不正確或已停用。', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.trigger_events
    WHERE object_id = OBJECT_ID(N'dbo.TR_Locations_ProtectCurrentDeploymentSiteLocations')
      AND type_desc = N'UPDATE'
)
    THROW 53717, N'Verify failed: Location reverse-protection trigger 必須涵蓋 UPDATE。', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.trigger_events
    WHERE object_id = OBJECT_ID(N'dbo.TR_DeploymentSiteLocationAssignments_ProtectActiveLocation')
      AND type_desc = N'INSERT'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.trigger_events
    WHERE object_id = OBJECT_ID(N'dbo.TR_DeploymentSiteLocationAssignments_ProtectActiveLocation')
      AND type_desc = N'UPDATE'
)
    THROW 53718, N'Verify failed: assignment forward-protection trigger 必須同時涵蓋 INSERT / UPDATE。', 1;

DECLARE @LocationTriggerDefinition NVARCHAR(MAX) =
    OBJECT_DEFINITION(OBJECT_ID(N'dbo.TR_Locations_ProtectCurrentDeploymentSiteLocations'));
DECLARE @AssignmentTriggerDefinition NVARCHAR(MAX) =
    OBJECT_DEFINITION(OBJECT_ID(N'dbo.TR_DeploymentSiteLocationAssignments_ProtectActiveLocation'));

DECLARE @LocationTriggerNormalized NVARCHAR(MAX) =
    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(@LocationTriggerDefinition, N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N''));
DECLARE @AssignmentTriggerNormalized NVARCHAR(MAX) =
    LOWER(REPLACE(REPLACE(REPLACE(REPLACE(@AssignmentTriggerDefinition, N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N''));

IF @LocationTriggerDefinition IS NULL
   OR @AssignmentTriggerDefinition IS NULL
    THROW 53719, N'Verify failed: 無法讀取 Location active invariant trigger definition。', 1;

IF CHARINDEX(N'sys.sp_getapplock', @LocationTriggerNormalized) = 0
   OR CHARINDEX(N'fieldvisit.locationdeploymentassignmentactiveinvariant', @LocationTriggerNormalized) = 0
   OR CHARINDEX(N'@lockmode=n''exclusive''', @LocationTriggerNormalized) = 0
   OR CHARINDEX(N'@lockowner=n''transaction''', @LocationTriggerNormalized) = 0
   OR CHARINDEX(N'@locktimeout=10000', @LocationTriggerNormalized) = 0
   OR CHARINDEX(N'@invariantlockresult<0', @LocationTriggerNormalized) = 0
   OR CHARINDEX(N'sys.sp_getapplock', @AssignmentTriggerNormalized) = 0
   OR CHARINDEX(N'fieldvisit.locationdeploymentassignmentactiveinvariant', @AssignmentTriggerNormalized) = 0
   OR CHARINDEX(N'@lockmode=n''exclusive''', @AssignmentTriggerNormalized) = 0
   OR CHARINDEX(N'@lockowner=n''transaction''', @AssignmentTriggerNormalized) = 0
   OR CHARINDEX(N'@locktimeout=10000', @AssignmentTriggerNormalized) = 0
   OR CHARINDEX(N'@invariantlockresult<0', @AssignmentTriggerNormalized) = 0
    THROW 53720, N'Verify failed: 兩個 trigger 必須使用完全相同的 Exclusive/Transaction invariant applock。', 1;

IF CHARINDEX(N'joindbo.deploymentsitelocationassignmentsawith(updlock,holdlock)', @LocationTriggerNormalized) = 0
   OR CHARINDEX(N'joindbo.locationslwith(updlock,holdlock)', @AssignmentTriggerNormalized) = 0
    THROW 53724, N'Verify failed: 兩個 invariant base-table read 都必須使用 UPDLOCK + HOLDLOCK。', 1;

IF CHARINDEX(N'd.isactive=1', @LocationTriggerNormalized) = 0
   OR CHARINDEX(N'i.isactive=0', @LocationTriggerNormalized) = 0
   OR CHARINDEX(N'a.effectivetoisnull', @LocationTriggerNormalized) = 0
   OR CHARINDEX(N'a.effectiveto>=@businesstoday', @LocationTriggerNormalized) = 0
   OR CHARINDEX(N'convert(date,dateadd(hour,8,sysutcdatetime()))', @LocationTriggerNormalized) = 0
   OR CHARINDEX(N'getdate(', @LocationTriggerNormalized) > 0
    THROW 53721, N'Verify failed: Location trigger 必須只保護 1→0，並以 Taipei BusinessToday 阻擋 current/future dependency。', 1;

IF CHARINDEX(N'l.isactive=0', @AssignmentTriggerNormalized) = 0
   OR CHARINDEX(N'i.effectivetoisnull', @AssignmentTriggerNormalized) = 0
   OR CHARINDEX(N'i.effectiveto>=@businesstoday', @AssignmentTriggerNormalized) = 0
   OR CHARINDEX(N'convert(date,dateadd(hour,8,sysutcdatetime()))', @AssignmentTriggerNormalized) = 0
   OR CHARINDEX(N'getdate(', @AssignmentTriggerNormalized) > 0
    THROW 53722, N'Verify failed: assignment trigger 必須以 Taipei BusinessToday 阻擋 inactive Location 的 current/future resulting row。', 1;

IF CHARINDEX(N'updatedbo.deploymentsitelocationassignments', @LocationTriggerNormalized) > 0
   OR CHARINDEX(N'deletefromdbo.deploymentsitelocationassignments', @LocationTriggerNormalized) > 0
   OR CHARINDEX(N'updatedbo.locations', @AssignmentTriggerNormalized) > 0
   OR CHARINDEX(N'deletefromdbo.locations', @AssignmentTriggerNormalized) > 0
    THROW 53723, N'Verify failed: active invariant trigger 不得自動修復或改寫 Location / assignment。', 1;

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
    @BusinessToday AS BusinessToday,
    (SELECT COUNT_BIG(*) FROM dbo.Locations) AS LocationCount,
    (SELECT COUNT_BIG(*) FROM dbo.TeamLocationNotes) AS TeamLocationNoteCount,
    (SELECT COUNT_BIG(*) FROM dbo.TeamLocationNotes WHERE Note IS NULL) AS ClearedTeamLocationNoteCount,
    (SELECT COUNT_BIG(*) FROM dbo.Locations WHERE DuplicateOfLocationId IS NOT NULL) AS MarkedDuplicateCount,
    (
        SELECT COUNT_BIG(*)
        FROM dbo.Locations l
        JOIN dbo.DeploymentSiteLocationAssignments a ON a.LocationId = l.LocationId
        WHERE l.IsActive = 0
          AND a.EffectiveTo < @BusinessToday
    ) AS HistoricalInactiveLocationAssignmentCount;

SELECT TOP(20)
    OrganizationId,
    TaxId,
    COUNT(*) AS LocationCount
FROM dbo.Locations
WHERE TaxId IS NOT NULL
GROUP BY OrganizationId, TaxId
ORDER BY LocationCount DESC, OrganizationId, TaxId;
