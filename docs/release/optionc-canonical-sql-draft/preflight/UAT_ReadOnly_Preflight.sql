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
IF @ActualDatabase IN (N'master',N'model',N'msdb',N'tempdb')
  THROW 51001, 'STOP: system database is not an approved UAT target.', 1;
IF @ActualDatabase <> @ExpectedDatabase
  THROW 51002, 'STOP: database identity mismatch.', 1;
IF @ServerName <> @ExpectedServer AND @ServerName NOT LIKE @ExpectedServer + N'.%'
  THROW 51003, 'STOP: SQL server identity mismatch.', 1;

SELECT @ServerName AS ServerName, @ActualDatabase AS DatabaseName,
       ORIGINAL_LOGIN() AS ReadOnlyIdentity, APP_NAME() AS ApplicationName,
       SYSUTCDATETIME() AS ObservedUtc;

IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
  THROW 51005, 'STOP: SchemaVersions is missing.', 1;

SELECT TOP (20) * FROM dbo.SchemaVersions ORDER BY 1 DESC;

DECLARE @Latest nvarchar(32) =
(
  SELECT TOP (1) CONVERT(nvarchar(32),Version)
  FROM dbo.SchemaVersions
  ORDER BY AppliedAtUtc DESC, Version DESC
);
IF @Latest IS NULL OR @Latest NOT IN (N'1.8.0-010',N'1.8.0-011',N'1.8.0-012',N'1.8.0-014')
  THROW 51006, 'STOP: unsupported SchemaVersions head.', 1;

DECLARE @cr bit=CASE WHEN OBJECT_ID(N'dbo.ChangeRequests',N'U') IS NULL THEN 0 ELSE 1 END;
DECLARE @ev bit=CASE WHEN OBJECT_ID(N'dbo.ChangeRequestEvents',N'U') IS NULL THEN 0 ELSE 1 END;
IF @cr<>@ev THROW 51007, 'STOP: partial canonical 011 object set.', 1;
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

DECLARE @md bit=CASE WHEN OBJECT_ID(N'dbo.LocationMergeDetails',N'U') IS NULL THEN 0 ELSE 1 END;
IF @md=1 AND (@cr=0 OR @ev=0 OR @st=0)
  THROW 51012, 'STOP: MergeDetails exists without canonical dependencies.', 1;

DECLARE @ae bit=CASE WHEN OBJECT_ID(N'dbo.StageEvidenceAuditEvents',N'U') IS NULL THEN 0 ELSE 1 END;
IF @ae=1 AND @st=0 THROW 51013, 'STOP: evidence audit exists without Stage.', 1;

SELECT t.name AS TableName, SUM(p.rows) AS RowCount
FROM sys.tables t
JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1)
WHERE t.name IN (N'SchemaVersions',N'ChangeRequests',N'ChangeRequestEvents',
                 N'LocationPublicationStages',N'LocationPublishedVersions',
                 N'LocationMergeDetails',N'StageEvidenceAuditEvents')
GROUP BY t.name ORDER BY t.name;

SELECT fk.name AS ForeignKeyName, OBJECT_SCHEMA_NAME(fk.parent_object_id) AS ParentSchema,
       OBJECT_NAME(fk.parent_object_id) AS ParentTable,
       OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable,
       fk.is_not_trusted AS IsNotTrusted
FROM sys.foreign_keys fk
WHERE OBJECT_NAME(fk.parent_object_id) IN
 (N'ChangeRequests',N'ChangeRequestEvents',N'LocationPublicationStages',
  N'LocationPublishedVersions',N'LocationMergeDetails',N'StageEvidenceAuditEvents');

IF EXISTS (
  SELECT 1 FROM sys.foreign_keys
  WHERE is_not_trusted=1 AND OBJECT_NAME(parent_object_id) IN
   (N'ChangeRequests',N'ChangeRequestEvents',N'LocationPublicationStages',
    N'LocationPublishedVersions',N'LocationMergeDetails',N'StageEvidenceAuditEvents')
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

SELECT N'ProjectLocationsWithoutLocation' AS CheckName, COUNT(*) AS Violations
FROM dbo.ProjectLocations pl LEFT JOIN dbo.Locations l ON l.LocationId=pl.LocationId
WHERE l.LocationId IS NULL
UNION ALL
SELECT N'FavoriteLocationsWithoutLocation', COUNT(*)
FROM dbo.UserFavoriteLocations f LEFT JOIN dbo.Locations l ON l.LocationId=f.LocationId
WHERE l.LocationId IS NULL
UNION ALL
SELECT N'CurrentPublishedVersionDuplicates', COUNT(*)
FROM (SELECT LocationId FROM dbo.LocationPublishedVersions WHERE IsCurrent=1
      GROUP BY LocationId HAVING COUNT(*)>1) d
UNION ALL
SELECT N'PendingDuplicateRequestSlots', COUNT(*)
FROM (SELECT OrganizationId,EntityId FROM dbo.ChangeRequests WHERE Status=N'Pending'
      GROUP BY OrganizationId,EntityId HAVING COUNT(*)>1) d;

SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',
  COALESCE((
    SELECT STRING_AGG(CONCAT(s.name,N'.',o.name,N'|',o.type,N'|',o.modify_date),N';')
    FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
    WHERE o.type IN ('U','V','PK','F','UQ','D','C') AND s.name=N'dbo'
  ),N'')),2) AS CatalogFingerprintSha256;

SELECT N'PREFLIGHT_COMPLETE_READ_ONLY' AS Result, @ServerName AS ServerName,
       @ActualDatabase AS DatabaseName, @Latest AS LatestSchemaVersion,
       SYSUTCDATETIME() AS CompletedUtc;
