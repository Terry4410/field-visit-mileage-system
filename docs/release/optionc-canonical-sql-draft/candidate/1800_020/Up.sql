-- SCHEME A / EXCEL CANONICAL STATUS -- NONEXECUTABLE PRODUCTION DRAFT
-- Execution is permitted only by isolated disposable SQL tests. Do not
-- deploy this file or run it on UAT/Production without separate Owner+DBA
-- authorization, preflight, rollback plan and future version sequencing.
SET XACT_ABORT ON;
IF DB_NAME() NOT LIKE N'OptionCIsolated[_]%'
    THROW 55110,N'OPTIONC_EXCEL_STATUS_DISPOSABLE_ONLY',1;
BEGIN TRY
  BEGIN TRANSACTION;
  IF OBJECT_ID(N'dbo.ImportBatches',N'U') IS NULL
      OR OBJECT_ID(N'dbo.ImportBatchItems',N'U') IS NULL
      OR OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
      THROW 55111,N'OPTIONC_EXCEL_LEGACY_IMPORT_SCHEMA_REQUIRED',1;
  IF COL_LENGTH(N'dbo.ImportBatches',N'Status') < 30
      OR COL_LENGTH(N'dbo.ImportBatchItems',N'Status') < 18
      THROW 55112,N'OPTIONC_EXCEL_STATUS_WIDTH_INVALID',1;
  IF EXISTS(SELECT 1 FROM sys.check_constraints
      WHERE parent_object_id=OBJECT_ID(N'dbo.ImportBatches')
        AND name=N'CK_ImportBatches_Status')
      ALTER TABLE dbo.ImportBatches DROP CONSTRAINT CK_ImportBatches_Status;
  IF EXISTS(SELECT 1 FROM sys.check_constraints
      WHERE parent_object_id=OBJECT_ID(N'dbo.ImportBatchItems')
        AND name=N'CK_ImportBatchItems_Status')
      ALTER TABLE dbo.ImportBatchItems DROP CONSTRAINT CK_ImportBatchItems_Status;
  ALTER TABLE dbo.ImportBatches WITH CHECK ADD
    CONSTRAINT CK_ImportBatches_Status CHECK(Status IN(
      N'Previewed',N'Confirmed',N'PartiallyFailed',
      N'PartiallySubmittedForReview',N'SubmittedForReview'));
  ALTER TABLE dbo.ImportBatchItems WITH CHECK ADD
    CONSTRAINT CK_ImportBatchItems_Status CHECK(Status IN(
      N'Valid',N'Error',N'Applied',N'Failed',N'SubmittedForReview'));
  IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions
      WHERE VersionNumber=N'1.8.0-EXCEL-CANONICAL-STATUS')
      INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
      VALUES(N'1.8.0-EXCEL-CANONICAL-STATUS',
             N'EXCEL canonical reviewed submission status contract / disposable test',
             SYSUTCDATETIME(),N'OptionCIsolated-test');
  IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-020')
 INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
 VALUES(N'1.8.0-020',N'R5 canonical additive contract 1800_020',DATEADD(millisecond,-1,(SELECT AppliedAt FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-EXCEL-CANONICAL-STATUS')),N'isolated-candidate');
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
