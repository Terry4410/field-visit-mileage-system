/* Structural verification for 1800_016. */
SET NOCOUNT ON;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-016') THROW 51910,N'R5_MARKER_MISSING_1800_016',1;
IF NOT (OBJECT_ID(N'dbo.ChangeRequestLeaderReviews',N'U') IS NOT NULL AND EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ChangeRequestLeaderReviews') AND name=N'UX_OptionC_LeaderReview_Request_Stage')) THROW 51911,N'R5_CONTRACT_SHAPE_INVALID_1800_016',1;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id IN(OBJECT_ID(N'dbo.ChangeRequestLeaderReviews'),OBJECT_ID(N'dbo.LocationCreateReservations'),OBJECT_ID(N'dbo.LocationMergeDetails')) AND (is_disabled=1 OR is_not_trusted=1)) THROW 51912,N'R5_UNTRUSTED_FK_1800_016',1;
IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ChangeRequestLeaderReviews') AND name=N'RowVersion' AND system_type_id=189) THROW 51913,N'R5_LEADER_ROWVERSION_MISSING',1;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.ChangeRequestLeaderReviews') AND name=N'FK_OptionC_LeaderReview_Request' AND is_disabled=0 AND is_not_trusted=0) THROW 51914,N'R5_LEADER_REQUEST_FK_INVALID',1;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id IN(OBJECT_ID(N'dbo.ChangeRequests'),OBJECT_ID(N'dbo.ChangeRequestEvents'),OBJECT_ID(N'dbo.LocationPublicationStages'),OBJECT_ID(N'dbo.LocationPublishedVersions'),OBJECT_ID(N'dbo.ChangeRequestLeaderReviews'),OBJECT_ID(N'dbo.LocationCreateReservations'),OBJECT_ID(N'dbo.LocationMergeDetails'),OBJECT_ID(N'dbo.ImportBatches'),OBJECT_ID(N'dbo.ImportBatchItems')) AND (is_disabled=1 OR is_not_trusted=1)) THROW 51915,N'R5_UNTRUSTED_CHECK_CONSTRAINT',1;
SELECT N'1800_016' AS VerifiedMigration, N'1.8.0-016' AS VersionNumber;
