-- NONDEPLOY / ONE-TIME SELECT-ONLY schema evidence capture.
-- Fixed UAT target only. Run only after Owner approves a single connection/cost window.
-- Returns catalog metadata only; no business row values, credentials, or principal identifiers.
SET NOCOUNT ON;
IF COALESCE(CONVERT(int,SERVERPROPERTY(N'EngineEdition')),0)<>5
 OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')),N'') COLLATE Latin1_General_100_BIN2 NOT IN
 (N'sql-fieldvisit-jpe-uat',N'sql-fieldvisit-jpe-uat.database.windows.net')
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N'db-fieldvisit-uat'
BEGIN THROW 52101,N'EXISTING010_CAPTURE_TARGET_HOLD',1; END;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION'),0)<>1
BEGIN THROW 52102,N'EXISTING010_CAPTURE_METADATA_PERMISSION_HOLD',1; END;
IF (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo'))<>63
BEGIN THROW 52103,N'EXISTING010_CAPTURE_TABLE_COUNT_HOLD',1; END;
IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
BEGIN THROW 52104,N'EXISTING010_CAPTURE_SCHEMA_VERSIONS_MISSING',1; END;
IF (SELECT COUNT_BIG(*) FROM dbo.SchemaVersions)<>26
BEGIN THROW 52105,N'EXISTING010_CAPTURE_HISTORY_COUNT_HOLD',1; END;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010')
BEGIN THROW 52106,N'EXISTING010_CAPTURE_MARKER_HOLD',1; END;

-- 1. Target and fixed historical predecessor markers.
SELECT CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')) AS ServerName,DB_NAME() AS DatabaseName,
 N'METADATA_ONLY_NO_BUSINESS_ROW_VALUES' AS EvidenceMode,
 (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo')) AS DboTableCount,
 (SELECT COUNT_BIG(*) FROM dbo.SchemaVersions) AS SchemaVersionCount,
 (SELECT MAX(VersionNumber) FROM dbo.SchemaVersions) AS LatestVersion;

-- 2. Complete dbo table inventory.
SELECT s.name AS SchemaName,t.name AS TableName,t.temporal_type_desc AS TemporalType,
 t.is_memory_optimized AS IsMemoryOptimized,t.is_filetable AS IsFileTable,t.lock_escalation_desc AS LockEscalation
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name=N'dbo' ORDER BY t.name;

-- 3. Column type, nullability, identity/computed/default/collation/generated facets.
SELECT s.name AS SchemaName,t.name AS TableName,c.column_id AS Ordinal,c.name AS ColumnName,
 SCHEMA_NAME(ty.schema_id) AS TypeSchema,ty.name AS SqlType,c.max_length AS MaxLengthBytes,
 c.precision AS NumericPrecision,c.scale AS NumericScale,c.is_nullable AS IsNullable,c.is_identity AS IsIdentity,
 ic.seed_value AS IdentitySeed,ic.increment_value AS IdentityIncrement,c.is_computed AS IsComputed,
 cc.definition AS ComputedDefinition,cc.is_persisted AS IsPersisted,c.is_rowguidcol AS IsRowGuid,
 c.is_sparse AS IsSparse,c.is_column_set AS IsColumnSet,c.generated_always_type_desc AS GeneratedAlwaysType,
 c.collation_name AS CollationName,dc.definition AS DefaultDefinition
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
LEFT JOIN sys.identity_columns ic ON ic.object_id=t.object_id AND ic.column_id=c.column_id
LEFT JOIN sys.computed_columns cc ON cc.object_id=t.object_id AND cc.column_id=c.column_id
LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
WHERE s.name=N'dbo' ORDER BY t.name,c.column_id;

-- 4. Primary/unique keys and indexes, key/include order, filters and enforcement state.
SELECT s.name AS SchemaName,t.name AS TableName,i.name AS IndexName,i.type_desc AS IndexType,
 i.is_primary_key AS IsPrimaryKey,i.is_unique_constraint AS IsUniqueConstraint,i.is_unique AS IsUnique,
 i.is_disabled AS IsDisabled,i.is_hypothetical AS IsHypothetical,i.ignore_dup_key AS IgnoreDuplicateKey,
 i.has_filter AS HasFilter,i.filter_definition AS FilterDefinition,ic.key_ordinal AS KeyOrdinal,
 ic.is_included_column AS IsIncluded,c.name AS ColumnName,ic.is_descending_key AS IsDescending
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id>0
JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE s.name=N'dbo' ORDER BY t.name,i.name,ic.index_column_id;

-- 5. FK columns, trust/enabled state, and referential actions.
SELECT ps.name AS SchemaName,pt.name AS TableName,f.name AS ForeignKeyName,pc.name AS ParentColumn,
 rs.name AS ReferencedSchema,rt.name AS ReferencedTable,rc.name AS ReferencedColumn,
 fkc.constraint_column_id AS ColumnOrdinal,f.delete_referential_action_desc AS DeleteAction,
 f.update_referential_action_desc AS UpdateAction,f.is_disabled AS IsDisabled,
 f.is_not_trusted AS IsNotTrusted,f.is_not_for_replication AS IsNotForReplication
FROM sys.foreign_keys f JOIN sys.tables pt ON pt.object_id=f.parent_object_id
JOIN sys.schemas ps ON ps.schema_id=pt.schema_id JOIN sys.tables rt ON rt.object_id=f.referenced_object_id
JOIN sys.schemas rs ON rs.schema_id=rt.schema_id JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=f.object_id
JOIN sys.columns pc ON pc.object_id=fkc.parent_object_id AND pc.column_id=fkc.parent_column_id
JOIN sys.columns rc ON rc.object_id=fkc.referenced_object_id AND rc.column_id=fkc.referenced_column_id
WHERE ps.name=N'dbo' ORDER BY pt.name,f.name,fkc.constraint_column_id;

-- 6. CHECK definitions and trust/enabled state.
SELECT s.name AS SchemaName,t.name AS TableName,cc.name AS CheckConstraintName,cc.definition AS Definition,
 cc.is_disabled AS IsDisabled,cc.is_not_trusted AS IsNotTrusted,cc.is_not_for_replication AS IsNotForReplication
FROM sys.check_constraints cc JOIN sys.tables t ON t.object_id=cc.parent_object_id
JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name=N'dbo' ORDER BY t.name,cc.name;

-- 7. Trigger definitions and operational attributes.
SELECT s.name AS SchemaName,t.name AS TableName,tr.name AS TriggerName,tr.is_disabled AS IsDisabled,
 tr.is_instead_of_trigger AS IsInsteadOfTrigger,tr.is_not_for_replication AS IsNotForReplication,
 tr.is_ms_shipped AS IsMicrosoftShipped,tr.create_date AS CreatedAt,tr.modify_date AS ModifiedAt,
 ev.type_desc AS EventType,m.uses_ansi_nulls AS UsesAnsiNulls,m.uses_quoted_identifier AS UsesQuotedIdentifier,
 m.definition AS Definition
FROM sys.triggers tr JOIN sys.tables t ON t.object_id=tr.parent_id
JOIN sys.schemas s ON s.schema_id=t.schema_id LEFT JOIN sys.sql_modules m ON m.object_id=tr.object_id
LEFT JOIN sys.trigger_events ev ON ev.object_id=tr.object_id
WHERE s.name=N'dbo' ORDER BY t.name,tr.name,ev.type_desc;
