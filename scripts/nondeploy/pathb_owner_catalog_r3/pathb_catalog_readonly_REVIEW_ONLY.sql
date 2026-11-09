-- FIELD VISIT PATH B / REVIEW_ONLY / NOT AUTHORIZED FOR LIVE EXECUTION
-- One fixed SQL batch, read-only catalog inspection. No DDL, DML or permission changes.
-- Intended for separate, explicitly authorized Owner-Entra private SQL connection.
SET NOCOUNT ON;
DECLARE @version_rows bigint = NULL;
DECLARE @expected_version_rows bigint = NULL;
DECLARE @version_exists bit = CASE WHEN OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NOT NULL THEN 1 ELSE 0 END;
DECLARE @ledger_exists bit = CASE WHEN OBJECT_ID(N'dbo.MigrationLedger', N'U') IS NOT NULL THEN 1 ELSE 0 END;

-- Dynamic reference ONLY to a fixed, fully qualified object. An empty database
-- must not fail compilation because dbo.SchemaVersions does not yet exist.
IF @version_exists = 1
BEGIN
  EXEC sys.sp_executesql
    N'SELECT @all = COUNT_BIG(*), @matched = COUNT_BIG(DISTINCT CASE WHEN VersionNumber IN
      (N''1.8.0-CREATE-LOWRISK'',N''1.8.0-LOWRISK-DIRECT'',N''1.8.0-EXCEL-CANONICAL-STATUS'')
      THEN VersionNumber END) FROM dbo.SchemaVersions;',
    N'@all bigint OUTPUT,@matched bigint OUTPUT',
    @all=@version_rows OUTPUT, @matched=@expected_version_rows OUTPUT;
END;

-- Fixed, status-only output. Do not emit identities, catalog names, or SQL errors.
SELECT CONCAT(
  'FV_CAT|',
  CASE WHEN DB_NAME() = N'db-fieldvisit-pathb-uat' THEN 'PASS' ELSE 'HOLD' END, '|',
  CASE WHEN LOWER(CONVERT(nvarchar(256),SERVERPROPERTY('ServerName'))) IN
    (N'sql-fieldvisit-jpe-pathb-uat',N'sql-fieldvisit-jpe-pathb-uat.database.windows.net')
    THEN 'PASS' ELSE 'HOLD' END, '|',
  CASE WHEN CONVERT(int,SERVERPROPERTY('EngineEdition')) = 5 THEN 'PASS' ELSE 'HOLD' END, '|',
  CASE WHEN HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CONTROL') = 1 AND
            HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION') = 1
    THEN 'PASS' ELSE 'HOLD' END, '|',
  (SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped=0), '|',
  (SELECT COUNT_BIG(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo')), '|',
  @version_exists, '|', @ledger_exists, '|',
  COALESCE(CONVERT(varchar(16),@version_rows),'NA'), '|',
  COALESCE(CONVERT(varchar(16),@expected_version_rows),'NA'), '|',
  (SELECT COUNT_BIG(*) FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1), '|',
  (SELECT COUNT_BIG(*) FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1), '|',
  (SELECT COUNT_BIG(*) FROM sys.triggers AS tr
    LEFT JOIN sys.sql_modules AS m ON m.object_id=tr.object_id
    WHERE tr.parent_class=1 AND tr.is_ms_shipped=0
      AND (tr.is_disabled=1 OR m.definition IS NULL))
) AS CatalogReceipt;
GO
