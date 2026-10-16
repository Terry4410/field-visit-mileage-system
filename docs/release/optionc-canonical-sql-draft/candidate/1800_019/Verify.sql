/* Structural verification for 1800_019. */
SET NOCOUNT ON;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-019') THROW 51910,N'R5_MARKER_MISSING_1800_019',1;
IF NOT (OBJECT_ID(N'dbo.LocationMergeDetails',N'U') IS NOT NULL) THROW 51911,N'R5_CONTRACT_SHAPE_INVALID_1800_019',1;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id IN(OBJECT_ID(N'dbo.ChangeRequestLeaderReviews'),OBJECT_ID(N'dbo.LocationCreateReservations'),OBJECT_ID(N'dbo.LocationMergeDetails')) AND (is_disabled=1 OR is_not_trusted=1)) THROW 51912,N'R5_UNTRUSTED_FK_1800_019',1;
IF EXISTS(SELECT 1 FROM (VALUES(N'SourceOrganizationId'),(N'SurvivorOrganizationId'),(N'SourceTeamId'),(N'SurvivorTeamId'),(N'SourceLocationRowVersion'),(N'SurvivorLocationRowVersion'),(N'BeforeJson'),(N'ProposedJson')) v(Name) WHERE COL_LENGTH(N'dbo.LocationMergeDetails',v.Name) IS NULL) THROW 51913,N'R5_MERGE_DETAILS_COLUMN_MISSING',1;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.LocationMergeDetails') AND name=N'FK_OptionC_MergeDetails_Request' AND is_disabled=0 AND is_not_trusted=0) THROW 51914,N'R5_MERGE_REQUEST_FK_INVALID',1;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id IN(OBJECT_ID(N'dbo.ChangeRequests'),OBJECT_ID(N'dbo.ChangeRequestEvents'),OBJECT_ID(N'dbo.LocationPublicationStages'),OBJECT_ID(N'dbo.LocationPublishedVersions'),OBJECT_ID(N'dbo.ChangeRequestLeaderReviews'),OBJECT_ID(N'dbo.LocationCreateReservations'),OBJECT_ID(N'dbo.LocationMergeDetails'),OBJECT_ID(N'dbo.ImportBatches'),OBJECT_ID(N'dbo.ImportBatchItems')) AND (is_disabled=1 OR is_not_trusted=1)) THROW 51915,N'R5_UNTRUSTED_CHECK_CONSTRAINT',1;
SELECT N'1800_019' AS VerifiedMigration, N'1.8.0-019' AS VersionNumber;
