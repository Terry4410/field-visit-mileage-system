-- G1 UAT010 Trigger metadata supplement: NONDEPLOY, not wired to Azure.
-- SQL produces *metadata only*, never trigger body text or any business rows.
-- Result set MUST remain private; only the safe comparator may emit status.
SET NOCOUNT ON;
DECLARE @server sysname=CONVERT(sysname,SERVERPROPERTY(N'ServerName'));
IF DB_NAME() COLLATE Latin1_General_100_BIN2<>N'db-fieldvisit-uat'
 OR @server COLLATE Latin1_General_100_BIN2<>N'sql-fieldvisit-jpe-uat'
 THROW 51990,N'G1_FIXED_TARGET_STOP',1;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010')
 THROW 51992,N'G1_PREDECESSOR_STOP',1;
IF COALESCE(HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'VIEW DEFINITION'),0)<>1
 THROW 51993,N'G1_METADATA_VISIBILITY_STOP',1;
SELECT s.name AS SchemaName,t.name AS TableName,tr.name AS TriggerName,
 CONVERT(bit,tr.is_disabled) AS IsDisabled,
 CONVERT(bit,tr.is_instead_of_trigger) AS IsInsteadOfTrigger,
 CONVERT(varchar(64),HASHBYTES('SHA2_256',
     CONVERT(varbinary(max),OBJECT_DEFINITION(tr.object_id))),2) AS DefinitionSha256,
 (SELECT STRING_AGG(CONVERT(nvarchar(30),e.type_desc),N',')
    WITHIN GROUP (ORDER BY e.type_desc)
  FROM sys.trigger_events AS e WHERE e.object_id=tr.object_id) AS Events
FROM sys.triggers AS tr
JOIN sys.tables AS t ON t.object_id=tr.parent_id
JOIN sys.schemas AS s ON s.schema_id=t.schema_id
WHERE tr.is_ms_shipped=0
ORDER BY s.name,t.name,tr.name;
