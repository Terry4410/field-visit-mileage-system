SET NOCOUNT ON;

DECLARE @ExpectedDatabase SYSNAME = N'db-fieldvisit-uat';
DECLARE @TargetObjectId INT = OBJECT_ID(N'dbo.Teams', N'U');
DECLARE @TargetName SYSNAME = N'UX_Teams_Organization_TeamCode';

IF DB_NAME() <> @ExpectedDatabase
    THROW 54100, N'Diagnostic refused: target database is not db-fieldvisit-uat.', 1;

IF @TargetObjectId IS NULL
    THROW 54101, N'Diagnostic refused: dbo.Teams does not exist.', 1;

SELECT
    N'TARGET_INDEX' AS EvidenceSection,
    SCHEMA_NAME(t.schema_id) AS SchemaName,
    t.name AS TableName,
    i.index_id AS IndexId,
    i.name AS IndexName,
    i.type AS IndexType,
    i.type_desc AS IndexTypeDescription,
    i.is_unique AS IsUnique,
    i.is_primary_key AS IsPrimaryKey,
    i.is_unique_constraint AS IsUniqueConstraint,
    i.is_disabled AS IsDisabled,
    i.is_hypothetical AS IsHypothetical,
    i.has_filter AS HasFilter,
    i.filter_definition AS FilterDefinition
FROM sys.indexes AS i
JOIN sys.tables AS t
  ON t.object_id = i.object_id
WHERE i.object_id = @TargetObjectId
  AND i.name = @TargetName;

SELECT
    N'TARGET_INDEX_COLUMNS' AS EvidenceSection,
    i.index_id AS IndexId,
    i.name AS IndexName,
    ic.index_column_id AS IndexColumnId,
    ic.key_ordinal AS KeyOrdinal,
    c.column_id AS ColumnId,
    c.name AS ColumnName,
    ic.is_descending_key AS IsDescendingKey,
    ic.is_included_column AS IsIncludedColumn
FROM sys.indexes AS i
JOIN sys.index_columns AS ic
  ON ic.object_id = i.object_id
 AND ic.index_id = i.index_id
JOIN sys.columns AS c
  ON c.object_id = ic.object_id
 AND c.column_id = ic.column_id
WHERE i.object_id = @TargetObjectId
  AND i.name = @TargetName
ORDER BY
    CASE WHEN ic.key_ordinal > 0 THEN 0 ELSE 1 END,
    ic.key_ordinal,
    ic.index_column_id;

SELECT
    N'TARGET_STATISTICS' AS EvidenceSection,
    s.stats_id AS StatsId,
    s.name AS StatisticsName,
    s.auto_created AS AutoCreated,
    s.user_created AS UserCreated,
    s.no_recompute AS NoRecompute,
    s.has_filter AS HasFilter,
    s.filter_definition AS FilterDefinition
FROM sys.stats AS s
WHERE s.object_id = @TargetObjectId
  AND s.name = @TargetName;

SELECT
    N'TARGET_STATISTICS_COLUMNS' AS EvidenceSection,
    s.stats_id AS StatsId,
    s.name AS StatisticsName,
    sc.stats_column_id AS StatsColumnId,
    c.column_id AS ColumnId,
    c.name AS ColumnName
FROM sys.stats AS s
JOIN sys.stats_columns AS sc
  ON sc.object_id = s.object_id
 AND sc.stats_id = s.stats_id
JOIN sys.columns AS c
  ON c.object_id = sc.object_id
 AND c.column_id = sc.column_id
WHERE s.object_id = @TargetObjectId
  AND s.name = @TargetName
ORDER BY
    sc.stats_column_id;

SELECT
    N'INDEX_STATISTICS_RELATIONSHIP' AS EvidenceSection,
    s.stats_id AS StatsId,
    s.name AS StatisticsName,
    i.index_id AS MatchingIndexId,
    i.name AS MatchingIndexName,
    CASE
        WHEN i.index_id IS NOT NULL
         AND i.index_id = s.stats_id
         AND i.name = s.name
        THEN 1
        ELSE 0
    END AS SameNumericIdAndName
FROM sys.stats AS s
LEFT JOIN sys.indexes AS i
  ON i.object_id = s.object_id
 AND i.index_id = s.stats_id
WHERE s.object_id = @TargetObjectId
  AND s.name = @TargetName;

SELECT
    N'ALL_TEAMS_INDEXES' AS EvidenceSection,
    i.index_id AS IndexId,
    i.name AS IndexName,
    i.type AS IndexType,
    i.type_desc AS IndexTypeDescription,
    i.is_unique AS IsUnique,
    i.is_primary_key AS IsPrimaryKey,
    i.is_unique_constraint AS IsUniqueConstraint,
    i.is_disabled AS IsDisabled,
    i.is_hypothetical AS IsHypothetical,
    i.has_filter AS HasFilter,
    i.filter_definition AS FilterDefinition
FROM sys.indexes AS i
WHERE i.object_id = @TargetObjectId
  AND i.name IS NOT NULL
ORDER BY
    i.index_id;

SELECT
    N'ALL_TEAMS_STATISTICS' AS EvidenceSection,
    s.stats_id AS StatsId,
    s.name AS StatisticsName,
    s.auto_created AS AutoCreated,
    s.user_created AS UserCreated,
    s.no_recompute AS NoRecompute,
    s.has_filter AS HasFilter,
    s.filter_definition AS FilterDefinition
FROM sys.stats AS s
WHERE s.object_id = @TargetObjectId
ORDER BY
    s.stats_id;
