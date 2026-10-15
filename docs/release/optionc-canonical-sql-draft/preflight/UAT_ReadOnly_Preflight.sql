/*
  Option C UAT read-only preflight candidate.
  Execute only from an approved post-uat/v1.8.0 workflow with Entra
  ApplicationIntent=ReadOnly. This script contains SELECT/THROW only:
  no DDL, DML, migration, repair, or restore.
  Supply EXPECTED_SERVER and EXPECTED_DATABASE as SQLCMD variables.
*/
:setvar EXPECTED_SERVER "REPLACE_WITH_APPROVED_FQDN"
:setvar EXPECTED_DATABASE "REPLACE_WITH_APPROVED_DATABASE"

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedServer nvarchar(256)=N'$(EXPECTED_SERVER)';
DECLARE @ExpectedDatabase sysname=N'$(EXPECTED_DATABASE)';
DECLARE @ActualDatabase sysname=DB_NAME();
DECLARE @ServerName nvarchar(256)=CONVERT(nvarchar(256),SERVERPROPERTY('ServerName'));

IF @ExpectedServer LIKE N'REPLACE_%' OR @ExpectedDatabase LIKE N'REPLACE_%'
  THROW 51000, 'STOP: approved exact target variables are required.', 1;
IF CHARINDEX(N'''', @ExpectedServer)>0 OR CHARINDEX(N';', @ExpectedServer)>0
   OR CHARINDEX(N'''', @ExpectedDatabase)>0 OR CHARINDEX(N';', @ExpectedDatabase)>0
  THROW 51004, 'STOP: target variables contain unsafe SQLCMD characters.', 1;
IF @ActualDatabase IN (N'master',N'model',N'msdb',N'tempdb')
  THROW 51001, 'STOP: system database is not an approved UAT target.', 1;
IF @ActualDatabase <> @ExpectedDatabase
  THROW 51002, 'STOP: database identity mismatch.', 1;
DECLARE @IsDisposableTarget bit = CASE WHEN (@ExpectedServer LIKE N'localhost%' OR @ExpectedServer LIKE N'127.0.0.1%') AND @ExpectedDatabase LIKE N'OptionCIsolated[_]%' THEN 1 ELSE 0 END;

IF @IsDisposableTarget=0 AND @ServerName <> @ExpectedServer AND @ServerName NOT LIKE @ExpectedServer + N'.%'
  THROW 51003, 'STOP: SQL server identity mismatch.', 1;

SELECT @ServerName AS ServerName, @ActualDatabase AS DatabaseName,
       ORIGINAL_LOGIN() AS ReadOnlyIdentity, APP_NAME() AS ApplicationName,
       SYSUTCDATETIME() AS ObservedUtc;

IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
  THROW 51005, 'STOP: SchemaVersions is missing.', 1;

SELECT TOP (20) * FROM dbo.SchemaVersions ORDER BY 1 DESC;

DECLARE @Latest nvarchar(32) =
(
  SELECT TOP (1) CONVERT(nvarchar(32),VersionNumber)
  FROM dbo.SchemaVersions
  ORDER BY AppliedAt DESC, VersionNumber DESC
);
IF @Latest IS NULL OR @Latest NOT IN (N'1.8.0-008',N'1.8.0-009',N'1.8.0-010',N'1.8.0-011',N'1.8.0-012',N'1.8.0-014')
  THROW 51006, 'STOP: unsupported SchemaVersions head.', 1;

DECLARE @cr bit=CASE WHEN OBJECT_ID(N'dbo.ChangeRequests',N'U') IS NULL THEN 0 ELSE 1 END;
DECLARE @ev bit=CASE WHEN OBJECT_ID(N'dbo.ChangeRequestEvents',N'U') IS NULL THEN 0 ELSE 1 END;
IF @cr<>@ev THROW 51007, 'STOP: partial canonical 011 object set.', 1;
IF (@Latest=N'1.8.0-010' AND (@cr<>0 OR @ev<>0))
   OR (@Latest IN (N'1.8.0-011',N'1.8.0-012',N'1.8.0-014') AND (@cr<>1 OR @ev<>1))
  THROW 51015, 'STOP: canonical object set does not match SchemaVersions.', 1;
IF @cr=1 AND @ev=1
BEGIN
  IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ChangeRequests') AND name=N'RequestPublicId')
     OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ChangeRequestEvents') AND name=N'DecisionKey')
    THROW 51008, 'STOP: canonical 011 columns incomplete.', 1;
  IF EXISTS (SELECT 1 FROM sys.check_constraints
             WHERE parent_object_id=OBJECT_ID(N'dbo.ChangeRequests') AND is_not_trusted=1)
    THROW 51009, 'STOP: untrusted ChangeRequests CHECK constraint.', 1;
END;

DECLARE @st bit=CASE WHEN OBJECT_ID(N'dbo.LocationPublicationStages',N'U') IS NULL THEN 0 ELSE 1 END;
DECLARE @pv bit=CASE WHEN OBJECT_ID(N'dbo.LocationPublishedVersions',N'U') IS NULL THEN 0 ELSE 1 END;
IF @st<>@pv THROW 51010, 'STOP: partial 014 Stage/PublishedVersions object set.', 1;
IF @st=1 AND (@cr=0 OR @ev=0)
  THROW 51011, 'STOP: 014 objects exist without canonical 011.', 1;
IF (@Latest=N'1.8.0-014' AND (@st<>1 OR @pv<>1))
   OR (@Latest<>N'1.8.0-014' AND (@st<>0 OR @pv<>0))
  THROW 51016, 'STOP: Stage/PublishedVersions do not match SchemaVersions.', 1;

DECLARE @md bit=CASE WHEN OBJECT_ID(N'dbo.LocationMergeDetails',N'U') IS NULL THEN 0 ELSE 1 END;
IF @md=1 AND (@cr=0 OR @ev=0 OR @st=0)
  THROW 51012, 'STOP: MergeDetails exists without canonical dependencies.', 1;

-- Evidence audit is an additive CHECK extension on ChangeRequestEvents, not a table.
IF OBJECT_ID(N'dbo.StageEvidenceAuditEvents',N'U') IS NOT NULL
  THROW 51013, 'STOP: unexpected parallel evidence audit table.', 1;
DECLARE @auditTypeDefinition nvarchar(max), @auditStateDefinition nvarchar(max);
DECLARE @oldDecisionState bit=0, @newDecisionState bit=0, @auditExtended bit=0;
IF @ev=1
BEGIN
  SELECT @auditTypeDefinition=definition
  FROM sys.check_constraints
  WHERE parent_object_id=OBJECT_ID(N'dbo.ChangeRequestEvents')
    AND name=N'CK_B3_ChangeRequestEvents_EventType' AND is_disabled=0 AND is_not_trusted=0;
  IF @auditTypeDefinition IS NULL OR @auditTypeDefinition NOT LIKE N'%Submitted%'
      OR @auditTypeDefinition NOT LIKE N'%Rejected%'
    THROW 51017, 'STOP: canonical event type constraint missing or untrusted.', 1;
  IF @Latest=N'1.8.0-011'
  BEGIN
    IF @auditTypeDefinition LIKE N'%Approved%' OR @auditTypeDefinition LIKE N'%Applied%'
      THROW 51018, 'STOP: 011 event type constraint has unexpected later types.', 1;
  END
  ELSE IF @auditTypeDefinition NOT LIKE N'%Approved%' OR @auditTypeDefinition NOT LIKE N'%Applied%'
    THROW 51019, 'STOP: 012 event types missing.', 1;
  SET @auditExtended=CASE WHEN @auditTypeDefinition LIKE N'%GeocodeEvidenceRecorded%'
                             AND @auditTypeDefinition LIKE N'%DuplicateReviewEvidenceRecorded%' THEN 1 ELSE 0 END;
  IF (@auditTypeDefinition LIKE N'%GeocodeEvidenceRecorded%' AND @auditTypeDefinition NOT LIKE N'%DuplicateReviewEvidenceRecorded%')
     OR (@auditTypeDefinition NOT LIKE N'%GeocodeEvidenceRecorded%' AND @auditTypeDefinition LIKE N'%DuplicateReviewEvidenceRecorded%')
    THROW 51020, 'STOP: partial evidence event type extension.', 1;
  SELECT @oldDecisionState=1 FROM sys.check_constraints
  WHERE parent_object_id=OBJECT_ID(N'dbo.ChangeRequestEvents')
    AND name=N'CK_B3_ChangeRequestEvents_DecisionState' AND is_disabled=0 AND is_not_trusted=0;
  SELECT @newDecisionState=1 FROM sys.check_constraints
  WHERE parent_object_id=OBJECT_ID(N'dbo.ChangeRequestEvents')
    AND name=N'CK_B3_ChangeRequestEvents_StageEvidenceEventType' AND is_disabled=0 AND is_not_trusted=0;
  IF (@auditExtended=1 AND (@newDecisionState<>1 OR @oldDecisionState<>0 OR @Latest<>N'1.8.0-014'))
     OR (@auditExtended=0 AND (@oldDecisionState<>1 OR @newDecisionState<>0))
    THROW 51021, 'STOP: evidence decision-state constraint drift.', 1;
END;

SELECT t.name AS TableName, SUM(p.rows) AS [RowCount]
FROM sys.tables t
JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1)
WHERE t.name IN (N'SchemaVersions',N'ChangeRequests',N'ChangeRequestEvents',
                 N'LocationPublicationStages',N'LocationPublishedVersions',
                 N'LocationMergeDetails')
GROUP BY t.name ORDER BY t.name;

SELECT fk.name AS ForeignKeyName, OBJECT_SCHEMA_NAME(fk.parent_object_id) AS ParentSchema,
       OBJECT_NAME(fk.parent_object_id) AS ParentTable,
       OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable,
       fk.is_not_trusted AS IsNotTrusted
FROM sys.foreign_keys fk
WHERE OBJECT_NAME(fk.parent_object_id) IN
 (N'ChangeRequests',N'ChangeRequestEvents',N'LocationPublicationStages',
  N'LocationPublishedVersions',N'LocationMergeDetails');

IF EXISTS (
  SELECT 1 FROM sys.foreign_keys
  WHERE is_not_trusted=1 AND OBJECT_NAME(parent_object_id) IN
   (N'ChangeRequests',N'ChangeRequestEvents',N'LocationPublicationStages',
    N'LocationPublishedVersions',N'LocationMergeDetails')
) THROW 51014, 'STOP: untrusted canonical/additive FK.', 1;

SELECT cc.name AS ConstraintName, OBJECT_NAME(cc.parent_object_id) AS TableName,
       cc.is_not_trusted, cc.definition
FROM sys.check_constraints cc
WHERE OBJECT_NAME(cc.parent_object_id) IN
 (N'ChangeRequests',N'ChangeRequestEvents',N'LocationPublicationStages',
  N'LocationPublishedVersions',N'LocationMergeDetails',N'StageEvidenceAuditEvents');

SELECT i.name AS IndexName, OBJECT_NAME(i.object_id) AS TableName,
       i.is_unique, i.is_unique_constraint, i.is_disabled,
       HASHBYTES('SHA2_256',CONCAT(OBJECT_NAME(i.object_id),N'|',i.name,N'|',
       i.is_unique,N'|',i.is_unique_constraint)) AS IndexFingerprint
FROM sys.indexes i
WHERE OBJECT_NAME(i.object_id) IN
 (N'ChangeRequests',N'ChangeRequestEvents',N'LocationPublicationStages',
  N'LocationPublishedVersions',N'LocationMergeDetails',N'StageEvidenceAuditEvents')
  AND i.index_id>0;

-- Optional catalog queries use dynamic SQL so baseline 010/011/012 does not
-- compile references to tables that are created only by later migrations.
IF OBJECT_ID(N'dbo.ProjectLocations',N'U') IS NOT NULL
  EXEC sys.sp_executesql N'SELECT N''ProjectLocationsWithoutLocation'' AS CheckName, COUNT(*) AS Violations FROM dbo.ProjectLocations pl LEFT JOIN dbo.Locations l ON l.LocationId=pl.LocationId WHERE l.LocationId IS NULL';
IF OBJECT_ID(N'dbo.UserFavoriteLocations',N'U') IS NOT NULL
  EXEC sys.sp_executesql N'SELECT N''FavoriteLocationsWithoutLocation'' AS CheckName, COUNT(*) AS Violations FROM dbo.UserFavoriteLocations f LEFT JOIN dbo.Locations l ON l.LocationId=f.LocationId WHERE l.LocationId IS NULL';
IF @pv=1
  EXEC sys.sp_executesql N'SELECT N''CurrentPublishedVersionDuplicates'' AS CheckName, COUNT(*) AS Violations FROM (SELECT LocationId FROM dbo.LocationPublishedVersions WHERE IsCurrent=1 GROUP BY LocationId HAVING COUNT(*)>1) d';
IF @cr=1
  EXEC sys.sp_executesql N'SELECT N''PendingDuplicateRequestSlots'' AS CheckName, COUNT(*) AS Violations FROM (SELECT OrganizationId,EntityId FROM dbo.ChangeRequests WHERE Status=N''Pending'' GROUP BY OrganizationId,EntityId HAVING COUNT(*)>1) d';

-- Permission evidence is diagnostic only; this block performs no write or grant.
-- A read-only principal must not have database DDL or representative table-write rights.
SELECT ORIGINAL_LOGIN() AS OriginalLogin,
       SUSER_SNAME() AS ServerPrincipal,
       USER_NAME() AS DatabaseUser,
       IS_ROLEMEMBER(N'db_owner') AS IsDbOwner,
       IS_ROLEMEMBER(N'db_ddladmin') AS IsDbDdlAdmin,
       IS_ROLEMEMBER(N'db_datawriter') AS IsDbDatawriter,
       HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CREATE'+N' TABLE') AS CanCreateTable,
       HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER ANY USER') AS CanAlterAnyUser,
       HAS_PERMS_BY_NAME(N'dbo.ChangeRequests',N'OBJECT',N'INSERT') AS CanInsertChangeRequests,
       HAS_PERMS_BY_NAME(N'dbo.ChangeRequests',N'OBJECT',N'UPDATE') AS CanUpdateChangeRequests,
       HAS_PERMS_BY_NAME(N'dbo.ChangeRequests',N'OBJECT',N'DELETE') AS CanDeleteChangeRequests,
       HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'INSERT') AS CanInsertSchemaVersions,
       HAS_PERMS_BY_NAME(N'dbo.Locations',N'OBJECT',N'UPDATE') AS CanUpdateLocations;

IF @IsDisposableTarget=0 AND (IS_ROLEMEMBER(N'db_owner')=1 OR IS_ROLEMEMBER(N'db_ddladmin')=1 OR IS_ROLEMEMBER(N'db_datawriter')=1
   OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CREATE'+N' TABLE')=1
   OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER ANY USER')=1
   OR HAS_PERMS_BY_NAME(N'dbo.ChangeRequests',N'OBJECT',N'INSERT')=1
   OR HAS_PERMS_BY_NAME(N'dbo.ChangeRequests',N'OBJECT',N'UPDATE')=1
   OR HAS_PERMS_BY_NAME(N'dbo.ChangeRequests',N'OBJECT',N'DELETE')=1
   OR HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'INSERT')=1
   OR HAS_PERMS_BY_NAME(N'dbo.Locations',N'OBJECT',N'UPDATE')=1)
  THROW 51022, 'STOP: connected principal exposes unexpected write or DDL permission.', 1;

SELECT @Latest AS MigrationPhase,
       @cr AS CanonicalRequestTablesPresent,
       @st AS StageAndPublishedVersionsPresent,
       @md AS MergeDetailsPresent,
       @auditExtended AS CanonicalEvidenceAuditExtended;

SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',
  COALESCE((
    SELECT STRING_AGG(
      CONVERT(nvarchar(max), CONCAT(s.name COLLATE DATABASE_DEFAULT,N'.',
        o.name COLLATE DATABASE_DEFAULT,N'|',o.type COLLATE DATABASE_DEFAULT,
        N'|',CONVERT(nvarchar(33),o.modify_date,126)) COLLATE DATABASE_DEFAULT),
      N';' COLLATE DATABASE_DEFAULT)
      WITHIN GROUP (ORDER BY s.name COLLATE DATABASE_DEFAULT,
        o.name COLLATE DATABASE_DEFAULT, o.type COLLATE DATABASE_DEFAULT)
    FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
    WHERE o.type IN ('U','V','PK','F','UQ','D','C') AND s.name=N'dbo'
  ),N'')),2) AS CatalogFingerprintSha256;

SELECT N'PREFLIGHT_COMPLETE_READ_ONLY' AS Result, @ServerName AS ServerName,
       @ActualDatabase AS DatabaseName, @Latest AS LatestSchemaVersion,
       SYSUTCDATETIME() AS CompletedUtc;
