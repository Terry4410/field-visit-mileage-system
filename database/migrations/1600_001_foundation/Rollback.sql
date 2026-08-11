SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BackgroundJobItems', N'U') IS NOT NULL DROP TABLE dbo.BackgroundJobItems;
IF OBJECT_ID(N'dbo.BackgroundJobs', N'U') IS NOT NULL DROP TABLE dbo.BackgroundJobs;
IF OBJECT_ID(N'dbo.CorrectionRequestChanges', N'U') IS NOT NULL DROP TABLE dbo.CorrectionRequestChanges;
IF OBJECT_ID(N'dbo.CorrectionRequests', N'U') IS NOT NULL DROP TABLE dbo.CorrectionRequests;
IF OBJECT_ID(N'dbo.VisitTripSnapshotStops', N'U') IS NOT NULL DROP TABLE dbo.VisitTripSnapshotStops;
IF OBJECT_ID(N'dbo.VisitTripSnapshots', N'U') IS NOT NULL DROP TABLE dbo.VisitTripSnapshots;
IF OBJECT_ID(N'dbo.UserTeamScopes', N'U') IS NOT NULL DROP TABLE dbo.UserTeamScopes;

COMMIT TRANSACTION;
