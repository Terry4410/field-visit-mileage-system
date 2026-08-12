/* Conservative rollback: removes only v1.6.0 FINAL staging structures/index constraints.
   It intentionally DOES NOT erase generated LocationCode values or restore old overlapping rate end dates,
   because doing so could damage records created after deployment. */
SET XACT_ABORT ON;
BEGIN TRY
  BEGIN TRANSACTION;
  IF OBJECT_ID(N'dbo.ImportBatchItems',N'U') IS NOT NULL DROP TABLE dbo.ImportBatchItems;
  IF OBJECT_ID(N'dbo.ImportBatches',N'U') IS NOT NULL DROP TABLE dbo.ImportBatches;
  IF EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_UserTeamScopes_OneActivePrimary' AND object_id=OBJECT_ID(N'dbo.UserTeamScopes')) DROP INDEX UX_UserTeamScopes_OneActivePrimary ON dbo.UserTeamScopes;
  IF EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_Locations_Organization_LocationCode' AND object_id=OBJECT_ID(N'dbo.Locations')) DROP INDEX UX_Locations_Organization_LocationCode ON dbo.Locations;
  IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NOT NULL DELETE FROM dbo.SchemaVersions WHERE VersionNumber=N'1.6.0';
  COMMIT TRANSACTION;
  PRINT N'v1.6.0 FINAL conservative rollback completed.';
END TRY
BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
