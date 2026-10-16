/*
  R6 metadata-only UAT schema inventory.
  Prepared for a future, separately approved read-only evidence run.
  This file has not been executed against UAT.
  No row values or applied-by identities are returned.
*/
DECLARE @ExpectedServer sysname=N'sql-fieldvisit-jpe-uat';
DECLARE @ExpectedDatabase sysname=N'db-fieldvisit-uat';
DECLARE @ActualServer sysname=CONVERT(sysname,SERVERPROPERTY(N'ServerName'));

IF DB_NAME()<>@ExpectedDatabase
    THROW 51990,N'R6_SCHEMA_FIDELITY_WRONG_DATABASE_STOP',1;
IF @ActualServer<>@ExpectedServer AND @ActualServer NOT LIKE @ExpectedServer+N'.%'
    THROW 51991,N'R6_SCHEMA_FIDELITY_WRONG_SERVER_STOP',1;

SELECT @ActualServer AS ServerName,DB_NAME() AS DatabaseName,
       N'METADATA_ONLY_NO_ROW_VALUES' AS EvidenceMode;

IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
    THROW 51992,N'R6_SCHEMA_FIDELITY_SCHEMA_VERSIONS_MISSING',1;

SELECT COUNT_BIG(*) AS SchemaVersionCount,
       MAX(CASE WHEN rn=1 THEN VersionNumber END) AS LatestVersion
FROM
(
    SELECT CONVERT(nvarchar(40),VersionNumber) AS VersionNumber,
           ROW_NUMBER() OVER(ORDER BY AppliedAt DESC,VersionNumber DESC) AS rn
    FROM dbo.SchemaVersions
) AS versions;

SELECT s.name AS SchemaName,t.name AS TableName,c.column_id AS Ordinal,
       c.name AS ColumnName,ty.name AS SqlType,
       CASE WHEN ty.name IN(N'nvarchar',N'nchar') AND c.max_length>0
            THEN c.max_length/2 ELSE c.max_length END AS MaxLengthBytesOrCharacters,
       c.precision AS NumericPrecision,c.scale AS NumericScale,
       c.is_nullable AS IsNullable,c.is_identity AS IsIdentity,
       c.is_computed AS IsComputed,
       CASE WHEN ty.name=N'timestamp' OR ty.name=N'rowversion'
            THEN CONVERT(bit,1) ELSE CONVERT(bit,0) END AS IsRowVersion,
       dc.definition AS DefaultDefinition
FROM sys.tables AS t
JOIN sys.schemas AS s ON s.schema_id=t.schema_id
JOIN sys.columns AS c ON c.object_id=t.object_id
JOIN sys.types AS ty ON ty.user_type_id=c.user_type_id
LEFT JOIN sys.default_constraints AS dc ON dc.object_id=c.default_object_id
WHERE t.is_ms_shipped=0
ORDER BY s.name,t.name,c.column_id;

SELECT s.name AS SchemaName,t.name AS TableName,
       i.name AS IndexName,i.type_desc AS IndexType,
       i.is_primary_key AS IsPrimaryKey,
       i.is_unique_constraint AS IsUniqueConstraint,
       i.is_unique AS IsUnique,i.is_disabled AS IsDisabled,
       i.has_filter AS HasFilter,i.filter_definition AS FilterDefinition,
       ic.key_ordinal AS KeyOrdinal,ic.is_included_column AS IsIncluded,
       c.name AS ColumnName,ic.is_descending_key AS IsDescending
FROM sys.tables AS t
JOIN sys.schemas AS s ON s.schema_id=t.schema_id
JOIN sys.indexes AS i ON i.object_id=t.object_id
JOIN sys.index_columns AS ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns AS c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE t.is_ms_shipped=0 AND i.index_id>0
ORDER BY s.name,t.name,i.name,ic.is_included_column,ic.key_ordinal,ic.index_column_id;

SELECT s.name AS SchemaName,t.name AS TableName,
       fk.name AS ForeignKeyName,pc.name AS ParentColumn,
       rs.name AS ReferencedSchema,rt.name AS ReferencedTable,
       rc.name AS ReferencedColumn,fkc.constraint_column_id AS ColumnOrdinal,
       fk.delete_referential_action_desc AS DeleteAction,
       fk.update_referential_action_desc AS UpdateAction,
       fk.is_disabled AS IsDisabled,fk.is_not_trusted AS IsNotTrusted
FROM sys.foreign_keys AS fk
JOIN sys.tables AS t ON t.object_id=fk.parent_object_id
JOIN sys.schemas AS s ON s.schema_id=t.schema_id
JOIN sys.tables AS rt ON rt.object_id=fk.referenced_object_id
JOIN sys.schemas AS rs ON rs.schema_id=rt.schema_id
JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id=fk.object_id
JOIN sys.columns AS pc ON pc.object_id=fkc.parent_object_id AND pc.column_id=fkc.parent_column_id
JOIN sys.columns AS rc ON rc.object_id=fkc.referenced_object_id AND rc.column_id=fkc.referenced_column_id
WHERE t.is_ms_shipped=0
ORDER BY s.name,t.name,fk.name,fkc.constraint_column_id;

SELECT s.name AS SchemaName,t.name AS TableName,
       cc.name AS CheckConstraintName,cc.definition AS Definition,
       cc.is_disabled AS IsDisabled,cc.is_not_trusted AS IsNotTrusted
FROM sys.check_constraints AS cc
JOIN sys.tables AS t ON t.object_id=cc.parent_object_id
JOIN sys.schemas AS s ON s.schema_id=t.schema_id
WHERE t.is_ms_shipped=0
ORDER BY s.name,t.name,cc.name;

SELECT N'VisitTrips' AS HistoricalTable,COUNT_BIG(*) AS RowCount,
       MAX(VisitTripId) AS HighWatermark FROM dbo.VisitTrips
UNION ALL
SELECT N'VisitTripStops',COUNT_BIG(*),MAX(VisitTripStopId) FROM dbo.VisitTripStops
UNION ALL
SELECT N'VisitTripSnapshots',COUNT_BIG(*),MAX(VisitTripSnapshotId) FROM dbo.VisitTripSnapshots
UNION ALL
SELECT N'VisitTripSnapshotStops',COUNT_BIG(*),MAX(VisitTripSnapshotStopId) FROM dbo.VisitTripSnapshotStops;
