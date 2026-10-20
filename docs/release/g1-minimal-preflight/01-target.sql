-- REVIEW CANDIDATE ONLY. No execution authorized. Only sanitized flags leave SQL.
SELECT code,status FROM (VALUES
 ('TARGET', CASE WHEN DB_NAME() COLLATE Latin1_General_100_BIN2=N'db-fieldvisit-uat'
 AND CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) COLLATE Latin1_General_100_BIN2=N'sql-fieldvisit-jpe-uat'
 AND CONVERT(int,SERVERPROPERTY('EngineEdition'))=5 THEN 'PASS' ELSE 'FAIL' END),
 ('VISIBILITY',CASE WHEN HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION')=1 THEN 'PASS' ELSE 'UNKNOWN' END),
 ('VERSION_SHAPE',CASE WHEN OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NOT NULL
 AND COL_LENGTH(N'dbo.SchemaVersions',N'VersionNumber') IS NOT NULL
 AND COL_LENGTH(N'dbo.SchemaVersions',N'AppliedAt') IS NOT NULL THEN 'PASS' ELSE 'UNKNOWN' END)
) AS c(code,status) ORDER BY code;
