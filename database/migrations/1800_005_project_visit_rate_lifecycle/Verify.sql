SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-005')
    THROW 53900,N'Verify failed: SchemaVersion 1.8.0-005 missing.',1;

/* Project lifecycle/date contract. */
IF COL_LENGTH(N'dbo.Projects',N'InactivatedAt') IS NULL
 OR COL_LENGTH(N'dbo.Projects',N'InactivatedByUserId') IS NULL
 OR COL_LENGTH(N'dbo.Projects',N'RowVersion') IS NULL
    THROW 53901,N'Verify failed: Project lifecycle columns missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.Projects') AND c.name=N'InactivatedAt' AND t.name=N'datetime2' AND c.scale=3 AND c.is_nullable=1)
 OR NOT EXISTS(SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.Projects') AND c.name=N'InactivatedByUserId' AND t.name=N'int' AND c.is_nullable=1)
 OR NOT EXISTS(SELECT 1 FROM sys.columns c JOIN sys.types t ON c.system_type_id=t.system_type_id AND t.user_type_id=t.system_type_id WHERE c.object_id=OBJECT_ID(N'dbo.Projects') AND c.name=N'RowVersion' AND t.name=N'timestamp' AND c.is_nullable=0)
    THROW 53907,N'Verify failed: Project lifecycle column types invalid.',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Projects') AND name=N'CK_Projects_Dates' AND is_disabled=0 AND is_not_trusted=0)
 OR EXISTS(SELECT 1 FROM dbo.Projects WHERE EndDate IS NOT NULL AND StartDate IS NOT NULL AND EndDate<StartDate)
    THROW 53902,N'Verify failed: trusted Project date-range contract missing/violated.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Projects') AND name=N'IX_Projects_Search')
 OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.Projects') AND name=N'FK_Projects_InactivatedByUser' AND is_disabled=0 AND is_not_trusted=0 AND delete_referential_action=0)
    THROW 53903,N'Verify failed: Project index/FK lifecycle contract missing.',1;

/* VisitType is GLOBAL and globally code-unique. */
IF COL_LENGTH(N'dbo.VisitTypes',N'OrganizationId') IS NOT NULL
 OR COL_LENGTH(N'dbo.VisitTypes',N'InactivatedAt') IS NULL
 OR COL_LENGTH(N'dbo.VisitTypes',N'InactivatedByUserId') IS NULL
 OR COL_LENGTH(N'dbo.VisitTypes',N'RowVersion') IS NULL
    THROW 53904,N'Verify failed: VisitType GLOBAL lifecycle shape invalid.',1;
IF NOT EXISTS(SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.VisitTypes') AND c.name=N'InactivatedAt' AND t.name=N'datetime2' AND c.scale=3 AND c.is_nullable=1)
 OR NOT EXISTS(SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.VisitTypes') AND c.name=N'InactivatedByUserId' AND t.name=N'int' AND c.is_nullable=1)
 OR NOT EXISTS(SELECT 1 FROM sys.columns c JOIN sys.types t ON c.system_type_id=t.system_type_id AND t.user_type_id=t.system_type_id WHERE c.object_id=OBJECT_ID(N'dbo.VisitTypes') AND c.name=N'RowVersion' AND t.name=N'timestamp' AND c.is_nullable=0)
    THROW 53908,N'Verify failed: VisitType lifecycle column types invalid.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.VisitTypes') AND name=N'UX_VisitTypes_VisitTypeCode' AND is_unique=1)
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.VisitTypes') AND name=N'IX_VisitTypes_Active_Sort')
 OR EXISTS(SELECT 1 FROM dbo.VisitTypes GROUP BY VisitTypeCode HAVING COUNT_BIG(*)>1)
    THROW 53905,N'Verify failed: VisitType global uniqueness/order indexes invalid.',1;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.VisitTypes') AND name=N'FK_VisitTypes_InactivatedByUser' AND is_disabled=0 AND is_not_trusted=0 AND delete_referential_action=0)
    THROW 53906,N'Verify failed: VisitType lifecycle FK invalid.',1;

/* MileageRate schema + exact canonical enum. */
IF COL_LENGTH(N'dbo.MileageRateRules',N'CreatedByUserId') IS NULL
 OR COL_LENGTH(N'dbo.MileageRateRules',N'UpdatedByUserId') IS NULL
 OR COL_LENGTH(N'dbo.MileageRateRules',N'InactivatedAt') IS NULL
 OR COL_LENGTH(N'dbo.MileageRateRules',N'InactivatedByUserId') IS NULL
 OR COL_LENGTH(N'dbo.MileageRateRules',N'RowVersion') IS NULL
    THROW 53910,N'Verify failed: MileageRate lifecycle columns missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.MileageRateRules') AND c.name=N'InactivatedAt' AND t.name=N'datetime2' AND c.scale=3 AND c.is_nullable=1)
 OR NOT EXISTS(SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id WHERE c.object_id=OBJECT_ID(N'dbo.MileageRateRules') AND c.name=N'InactivatedByUserId' AND t.name=N'int' AND c.is_nullable=1)
 OR NOT EXISTS(SELECT 1 FROM sys.columns c JOIN sys.types t ON c.system_type_id=t.system_type_id AND t.user_type_id=t.system_type_id WHERE c.object_id=OBJECT_ID(N'dbo.MileageRateRules') AND c.name=N'RowVersion' AND t.name=N'timestamp' AND c.is_nullable=0)
    THROW 53915,N'Verify failed: MileageRate lifecycle column types invalid.',1;
IF EXISTS(
    SELECT 1 FROM dbo.MileageRateRules
    WHERE VehicleType COLLATE Latin1_General_100_BIN2 NOT IN
      (N'MOTORCYCLE' COLLATE Latin1_General_100_BIN2,N'CAR' COLLATE Latin1_General_100_BIN2)
)
    THROW 53911,N'Verify failed: noncanonical VehicleType persisted.',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.MileageRateRules') AND name=N'CK_MileageRateRules_VehicleType' AND is_disabled=0 AND is_not_trusted=0)
 OR NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.MileageRateRules') AND name=N'CK_MileageRateRules_Dates' AND is_disabled=0 AND is_not_trusted=0)
 OR NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.MileageRateRules') AND name=N'CK_MileageRateRules_NonNegativeRate' AND is_disabled=0 AND is_not_trusted=0)
    THROW 53912,N'Verify failed: MileageRate trusted checks missing.',1;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id IN(OBJECT_ID(N'dbo.Projects'),OBJECT_ID(N'dbo.VisitTypes'),OBJECT_ID(N'dbo.MileageRateRules')) AND (is_disabled=1 OR is_not_trusted=1 OR delete_referential_action<>0))
    THROW 53913,N'Verify failed: lifecycle FK disabled/untrusted/cascade.',1;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MileageRateRules') AND name=N'UX_MileageRateRules_Scope_Vehicle_Start' AND is_unique=1)
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MileageRateRules') AND name=N'IX_MileageRateRules_AsOf')
    THROW 53914,N'Verify failed: MileageRate series indexes missing.',1;

DECLARE @TriggerObjectId INT=OBJECT_ID(N'dbo.TR_MileageRateRules_ProtectSeries',N'TR');
IF @TriggerObjectId IS NULL
 OR OBJECTPROPERTY(@TriggerObjectId,'ExecIsTriggerDisabled')=1
 OR OBJECTPROPERTY(@TriggerObjectId,'ExecIsTriggerNotForRepl')=1
 OR NOT EXISTS(SELECT 1 FROM sys.triggers WHERE object_id=@TriggerObjectId AND parent_id=OBJECT_ID(N'dbo.MileageRateRules'))
    THROW 53920,N'Verify failed: authoritative MileageRate trigger missing/disabled/wrong table.',1;
IF NOT EXISTS(SELECT 1 FROM sys.trigger_events WHERE object_id=@TriggerObjectId AND type_desc=N'INSERT')
 OR NOT EXISTS(SELECT 1 FROM sys.trigger_events WHERE object_id=@TriggerObjectId AND type_desc=N'UPDATE')
 OR NOT EXISTS(SELECT 1 FROM sys.trigger_events WHERE object_id=@TriggerObjectId AND type_desc=N'DELETE')
    THROW 53921,N'Verify failed: MileageRate trigger must cover INSERT/UPDATE/DELETE.',1;

DECLARE @Def NVARCHAR(MAX)=LOWER(OBJECT_DEFINITION(@TriggerObjectId));
DECLARE @Compact NVARCHAR(MAX)=REPLACE(REPLACE(REPLACE(REPLACE(@Def,N' ',N''),CHAR(13),N''),CHAR(10),N''),CHAR(9),N'');
IF @Def NOT LIKE N'%fieldvisit.mileagerateseries|org=%'
 OR @Def NOT LIKE N'%lockmode=n''exclusive''%'
 OR @Def NOT LIKE N'%lockowner=n''transaction''%'
 OR @Compact NOT LIKE N'%@locktimeout=10000%'
 OR @Def NOT LIKE N'%order by resourcename collate latin1_general_100_bin2 asc%'
    THROW 53922,N'Verify failed: canonical applock or binary deterministic multi-series lock order missing.',1;
IF @Def NOT LIKE N'%updlock,holdlock%'
 OR @Def NOT LIKE N'%r.organizationid=a.organizationid or (r.organizationid is null and a.organizationid is null)%'
    THROW 53923,N'Verify failed: SNAPSHOT-safe current read or NULL-safe exact-series equality missing.',1;
IF @Compact LIKE N'%isnull(r.organizationid,-1)%'
 OR @Compact LIKE N'%isnull(a.organizationid,-1)%'
 OR @Compact LIKE N'%isnull(organizationid,-1)%'
    THROW 53924,N'Verify failed: sentinel Organization series identity detected.',1;
IF @Def NOT LIKE N'%physical delete is prohibited%'
 OR @Def NOT LIKE N'%trigger_nestlevel(object_id(n''dbo.tr_mileageraterules_protectseries''), ''after'', ''dml'')%'
 OR @Def NOT LIKE N'%lead(r.effectivefrom)%'
 OR @Def NOT LIKE N'%dateadd(day,-1,o.nextfrom)%'
    THROW 53925,N'Verify failed: DELETE protection or system-derived EffectiveTo contract missing.',1;
IF @Def NOT LIKE N'%inactive mileagerate effectiveto is historical system evidence%'
    THROW 53926,N'Verify failed: inactive historical EffectiveTo protection missing.',1;
IF @Compact NOT LIKE N'%wherei.isactive=1andi.effectivetoisnotnulland(d.mileagerateruleidisnullorupdate(effectiveto)))throw53839,%'
 OR @Def NOT LIKE N'%active mileagerate effectiveto is database-derived; non-null caller-authored values are prohibited.%'
    THROW 53928,N'Verify failed: active caller-authored EffectiveTo rejection contract missing.',1;
IF @Def NOT LIKE N'%mileagerate canonical vehicletype final invariant failed%'
 OR @Def NOT LIKE N'%mileagerate duplicate active effectivefrom final invariant failed%'
 OR @Def NOT LIKE N'%mileagerate active exact-series overlap detected%'
 OR @Def NOT LIKE N'%mileagerate derived effectiveto final invariant failed%'
 OR @Def NOT LIKE N'%mileagerate terminal effectiveto final invariant failed%'
    THROW 53927,N'Verify failed: outer final base-table invariant revalidation incomplete.',1;

IF EXISTS(
 SELECT 1 FROM dbo.MileageRateRules a
 JOIN dbo.MileageRateRules b
   ON (a.OrganizationId=b.OrganizationId OR (a.OrganizationId IS NULL AND b.OrganizationId IS NULL))
  AND a.VehicleType=b.VehicleType AND a.EffectiveFrom=b.EffectiveFrom
  AND a.MileageRateRuleId<b.MileageRateRuleId AND a.IsActive=1 AND b.IsActive=1
)
    THROW 53930,N'Verify failed: duplicate active EffectiveFrom.',1;

IF EXISTS(
 SELECT 1
 FROM (
   SELECT MileageRateRuleId,OrganizationId,VehicleType,EffectiveFrom,EffectiveTo,
          LEAD(EffectiveFrom) OVER(PARTITION BY OrganizationId,VehicleType ORDER BY EffectiveFrom,MileageRateRuleId) NextFrom
   FROM dbo.MileageRateRules WHERE IsActive=1
 ) ordered
 WHERE (NextFrom IS NULL AND EffectiveTo IS NOT NULL)
    OR (NextFrom IS NOT NULL AND (EffectiveTo IS NULL OR EffectiveTo<>DATEADD(day,-1,NextFrom)))
)
    THROW 53931,N'Verify failed: derived EffectiveTo/terminal NULL invariant violated.',1;

IF EXISTS(
 SELECT 1 FROM dbo.MileageRateRules a
 JOIN dbo.MileageRateRules b
   ON (a.OrganizationId=b.OrganizationId OR (a.OrganizationId IS NULL AND b.OrganizationId IS NULL))
  AND a.VehicleType=b.VehicleType AND a.MileageRateRuleId<b.MileageRateRuleId
  AND a.IsActive=1 AND b.IsActive=1
  AND a.EffectiveFrom<=COALESCE(b.EffectiveTo,CONVERT(date,'99991231'))
  AND b.EffectiveFrom<=COALESCE(a.EffectiveTo,CONVERT(date,'99991231'))
)
    THROW 53932,N'Verify failed: same exact-series active overlap.',1;

IF OBJECT_ID(N'dbo.VisitTripSnapshots',N'U') IS NULL
    THROW 53940,N'Verify failed: historical Snapshot structure missing.',1;
IF OBJECT_ID(N'dbo.SchemaMigrationDataBaselines',N'U') IS NOT NULL
 AND EXISTS(SELECT 1 FROM dbo.SchemaMigrationDataBaselines WHERE MigrationVersion=N'1.8.0-001')
 AND (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshots)
     < (SELECT VisitTripSnapshotCount FROM dbo.SchemaMigrationDataBaselines WHERE MigrationVersion=N'1.8.0-001')
    THROW 53941,N'Verify failed: historical Snapshot count below protected baseline.',1;

SELECT N'PASS' VerifyStatus,DB_NAME() DatabaseName,N'1.8.0-005' MigrationVersion;
