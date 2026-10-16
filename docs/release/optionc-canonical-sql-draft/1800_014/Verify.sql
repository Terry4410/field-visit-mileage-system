/* OPTION C 014 CANDIDATE — read-only verification for disposable rehearsal. */
SET NOCOUNT ON;
IF DB_NAME() NOT LIKE N'OptionCIsolated[_]%'
    THROW 51420, N'OPTIONC_014_VERIFY_DISPOSABLE_DATABASE_ONLY', 1;
SELECT VersionNumber,Description,AppliedAt
FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-014';
SELECT t.name AS TableName,c.name AS ColumnName,ty.name AS SqlType,c.is_nullable
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE t.name IN(N'LocationPublicationStages',N'LocationPublishedVersions')
ORDER BY t.name,c.column_id;
SELECT i.name,OBJECT_NAME(i.object_id) AS TableName,i.is_unique,i.has_filter,i.filter_definition
FROM sys.indexes i
WHERE i.object_id IN(OBJECT_ID(N'dbo.LocationPublicationStages'),OBJECT_ID(N'dbo.LocationPublishedVersions'))
  AND i.name LIKE N'UX_OptionC[_]%';
SELECT name,is_disabled,is_not_trusted
FROM sys.check_constraints
WHERE parent_object_id IN(OBJECT_ID(N'dbo.LocationPublicationStages'),OBJECT_ID(N'dbo.LocationPublishedVersions'));
