SET NOCOUNT ON;
SELECT (
 SELECT t.name AS [table],
   JSON_QUERY((SELECT c.name,ty.name AS [type],c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity,c.is_computed,c.collation_name,
      dc.definition AS [default],cc.definition AS computed
     FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id
     LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
     LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id
     WHERE c.object_id=t.object_id ORDER BY c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES)) AS columns,
   JSON_QUERY((SELECT i.name,i.is_unique,i.type,i.has_filter,i.filter_definition,i.is_disabled,
      JSON_QUERY((SELECT col.name,ic.key_ordinal,ic.is_descending_key,ic.is_included_column
        FROM sys.index_columns ic JOIN sys.columns col ON col.object_id=ic.object_id AND col.column_id=ic.column_id
        WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id ORDER BY ic.index_column_id
        FOR JSON PATH,INCLUDE_NULL_VALUES)) AS keys
     FROM sys.indexes i WHERE i.object_id=t.object_id AND i.index_id>0 ORDER BY i.name
     FOR JSON PATH,INCLUDE_NULL_VALUES)) AS indexes,
   JSON_QUERY((SELECT c.name,c.definition,c.is_disabled,c.is_not_trusted
     FROM sys.check_constraints c WHERE c.parent_object_id=t.object_id ORDER BY c.name
     FOR JSON PATH,INCLUDE_NULL_VALUES)) AS checks,
   JSON_QUERY((SELECT f.name,OBJECT_NAME(f.referenced_object_id) AS target,f.delete_referential_action,f.update_referential_action,f.is_disabled,f.is_not_trusted,
      JSON_QUERY((SELECT pc.name AS parentColumn,rc.name AS referencedColumn
       FROM sys.foreign_key_columns fc JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id
       JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id
       WHERE fc.constraint_object_id=f.object_id ORDER BY fc.constraint_column_id FOR JSON PATH)) AS columns
     FROM sys.foreign_keys f WHERE f.parent_object_id=t.object_id ORDER BY f.name FOR JSON PATH,INCLUDE_NULL_VALUES)) AS foreignKeys,
   JSON_QUERY((SELECT tr.name,tr.is_disabled,m.definition,m.uses_ansi_nulls,m.uses_quoted_identifier
     FROM sys.triggers tr JOIN sys.sql_modules m ON m.object_id=tr.object_id
     WHERE tr.parent_id=t.object_id ORDER BY tr.name FOR JSON PATH,INCLUDE_NULL_VALUES)) AS triggers
 FROM sys.tables t WHERE t.schema_id=SCHEMA_ID(N'dbo') ORDER BY t.name FOR JSON PATH,INCLUDE_NULL_VALUES
) AS ContractJson;
