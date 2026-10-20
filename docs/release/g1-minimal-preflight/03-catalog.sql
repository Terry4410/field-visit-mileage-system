-- @requirements is a pinned JSON parameter; never user SQL. No table rows read.
-- PASS means ONLY the named necessary condition, not complete schema equivalence.
WITH required AS (
 SELECT [table], [column] FROM OPENJSON(@requirements)
 WITH ([table] nvarchar(128),[column] nvarchar(128))
), missing AS (
 SELECT 1 AS absent FROM required r WHERE NOT EXISTS(
 SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
 JOIN sys.columns c ON c.object_id=t.object_id
 WHERE s.name=N'dbo' AND t.name=r.[table] AND c.name=r.[column])
), scoped AS (
 SELECT t.object_id FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
 WHERE s.name=N'dbo' AND t.name IN(SELECT [table] FROM required)
), facets AS (
 SELECT * FROM OPENJSON(@reference,'$.columns') WITH(
 [table] nvarchar(128),[column] nvarchar(128),[type] nvarchar(128),
 max_length int,[precision] int,scale int,is_nullable bit,is_identity bit,migration varchar(3)) WHERE migration<>'019'
), expected_fk AS (
 SELECT * FROM OPENJSON(@reference,'$.foreignKeys') WITH(
 [table] nvarchar(128),name nvarchar(128),[columns] nvarchar(max) AS JSON,
 referencedTable nvarchar(128),referencedColumns nvarchar(max) AS JSON,
 deleteAction nvarchar(60),updateAction nvarchar(60),migration varchar(3)) WHERE migration<>'019'
), expected_index AS (
 SELECT * FROM OPENJSON(@reference,'$.indexes') WITH(
 [table] nvarchar(128),name nvarchar(128),[unique] bit,keys nvarchar(max) AS JSON,
 filter nvarchar(max),migration varchar(3)) WHERE migration<>'019'
), expected_check AS (
 SELECT * FROM OPENJSON(@reference,'$.checks') WITH(
 [table] nvarchar(128),name nvarchar(128),definition nvarchar(max),migration varchar(3)) WHERE migration<>'019'
)
SELECT code,status FROM (VALUES
 ('REQUIRED_COLUMNS',CASE WHEN EXISTS(SELECT 1 FROM missing) THEN 'FAIL' ELSE 'PASS' END),
 ('TRUSTED_RELATIONSHIPS',CASE WHEN EXISTS(
 SELECT 1 FROM sys.foreign_keys WHERE parent_object_id IN(SELECT object_id FROM scoped) AND(is_disabled=1 OR is_not_trusted=1)
 UNION ALL SELECT 1 FROM sys.check_constraints WHERE parent_object_id IN(SELECT object_id FROM scoped) AND(is_disabled=1 OR is_not_trusted=1)) THEN 'FAIL' ELSE 'PASS' END),
 ('ENABLED_INDEXES',CASE WHEN EXISTS(SELECT 1 FROM sys.indexes WHERE object_id IN(SELECT object_id FROM scoped) AND(is_disabled=1 OR is_hypothetical=1)) THEN 'FAIL' ELSE 'PASS' END),
 ('SCOPED_COLUMN_FACETS',CASE WHEN EXISTS(
 SELECT 1 FROM facets f LEFT JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+QUOTENAME(f.[table]),N'U') AND c.name=f.[column]
 LEFT JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.column_id IS NULL OR t.name<>f.[type] OR t.is_user_defined=1 OR c.max_length<>f.max_length
 OR c.precision<>f.precision OR c.scale<>f.scale OR c.is_nullable<>f.is_nullable OR c.is_identity<>f.is_identity
 OR c.is_computed=1) THEN 'FAIL' ELSE 'PASS' END),
 ('SCOPED_FK_MAPPINGS',CASE WHEN EXISTS(
 SELECT 1 FROM expected_fk e LEFT JOIN sys.foreign_keys f ON f.parent_object_id=OBJECT_ID(N'dbo.'+QUOTENAME(e.[table]),N'U') AND f.name=e.name
 WHERE f.object_id IS NULL OR f.is_disabled=1 OR f.is_not_trusted=1
 OR f.referenced_object_id<>OBJECT_ID(N'dbo.'+QUOTENAME(e.referencedTable),N'U')
 OR f.delete_referential_action_desc<>e.deleteAction OR f.update_referential_action_desc<>e.updateAction
 OR (SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id=f.object_id)<>(SELECT COUNT(*) FROM OPENJSON(e.[columns]))
 OR EXISTS(SELECT 1 FROM OPENJSON(e.[columns]) k LEFT JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id AND fc.constraint_column_id=CONVERT(int,k.[key])+1
 WHERE fc.constraint_column_id IS NULL OR COL_NAME(fc.parent_object_id,fc.parent_column_id)<>k.value
 OR COL_NAME(fc.referenced_object_id,fc.referenced_column_id)<>JSON_VALUE(e.referencedColumns,'$['+k.[key]+']'))
 ) THEN 'FAIL' ELSE 'PASS' END),
 ('SCOPED_INDEX_KEYS',CASE WHEN EXISTS(
 SELECT 1 FROM expected_index e LEFT JOIN sys.indexes i ON i.object_id=OBJECT_ID(N'dbo.'+QUOTENAME(e.[table]),N'U') AND i.name=e.name
 WHERE i.index_id IS NULL OR i.is_unique<>e.[unique] OR i.is_disabled=1 OR i.is_hypothetical=1
 OR (SELECT COUNT(*) FROM sys.index_columns WHERE object_id=i.object_id AND index_id=i.index_id AND key_ordinal>0)<>(SELECT COUNT(*) FROM OPENJSON(e.keys))
 OR EXISTS(SELECT 1 FROM OPENJSON(e.keys) k LEFT JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=CONVERT(int,k.[key])+1
 WHERE ic.column_id IS NULL OR COL_NAME(ic.object_id,ic.column_id)<>k.value OR ic.is_descending_key=1)
 OR EXISTS(SELECT 1 FROM sys.index_columns WHERE object_id=i.object_id AND index_id=i.index_id AND is_included_column=1)
 OR (e.filter IS NULL AND i.has_filter=1) OR (e.filter IS NOT NULL AND i.has_filter=0)
 ) THEN 'FAIL' WHEN EXISTS(SELECT 1 FROM expected_index e JOIN sys.indexes i ON i.object_id=OBJECT_ID(N'dbo.'+QUOTENAME(e.[table]),N'U') AND i.name=e.name
 WHERE e.filter IS NOT NULL AND (i.filter_definition IS NULL OR e.filter COLLATE Latin1_General_100_BIN2<>i.filter_definition COLLATE Latin1_General_100_BIN2)) THEN 'UNKNOWN' ELSE 'PASS' END),
 ('SCOPED_CHECK_DEFINITIONS',CASE WHEN EXISTS(
 SELECT 1 FROM expected_check e LEFT JOIN sys.check_constraints c ON c.parent_object_id=OBJECT_ID(N'dbo.'+QUOTENAME(e.[table]),N'U') AND c.name=e.name
 WHERE c.object_id IS NULL OR c.is_disabled=1 OR c.is_not_trusted=1) THEN 'FAIL'
 WHEN EXISTS(SELECT 1 FROM expected_check e JOIN sys.check_constraints c ON c.parent_object_id=OBJECT_ID(N'dbo.'+QUOTENAME(e.[table]),N'U') AND c.name=e.name
 WHERE c.definition IS NULL OR c.definition COLLATE Latin1_General_100_BIN2<>e.definition COLLATE Latin1_General_100_BIN2) THEN 'UNKNOWN' ELSE 'PASS' END),
 ('COMPLETE_FACET_EQUIVALENCE','UNKNOWN'),
 ('HISTORICAL_COMPATIBILITY','UNKNOWN')
) AS c(code,status) ORDER BY code;
