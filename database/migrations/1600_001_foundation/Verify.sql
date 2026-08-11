SELECT
    CASE WHEN OBJECT_ID(N'dbo.UserTeamScopes', N'U') IS NOT NULL THEN 1 ELSE 0 END AS UserTeamScopesExists,
    CASE WHEN OBJECT_ID(N'dbo.VisitTripSnapshots', N'U') IS NOT NULL THEN 1 ELSE 0 END AS VisitTripSnapshotsExists,
    CASE WHEN OBJECT_ID(N'dbo.VisitTripSnapshotStops', N'U') IS NOT NULL THEN 1 ELSE 0 END AS VisitTripSnapshotStopsExists,
    CASE WHEN OBJECT_ID(N'dbo.CorrectionRequests', N'U') IS NOT NULL THEN 1 ELSE 0 END AS CorrectionRequestsExists,
    CASE WHEN OBJECT_ID(N'dbo.CorrectionRequestChanges', N'U') IS NOT NULL THEN 1 ELSE 0 END AS CorrectionRequestChangesExists,
    CASE WHEN OBJECT_ID(N'dbo.BackgroundJobs', N'U') IS NOT NULL THEN 1 ELSE 0 END AS BackgroundJobsExists,
    CASE WHEN OBJECT_ID(N'dbo.BackgroundJobItems', N'U') IS NOT NULL THEN 1 ELSE 0 END AS BackgroundJobItemsExists;

SELECT COUNT(*) AS UserTeamScopeCount FROM dbo.UserTeamScopes;
SELECT COUNT(*) AS ExistingUsersWithTeam FROM dbo.Users WHERE TeamId IS NOT NULL;

SELECT TOP 20 UserId, TeamId, IsPrimary, IsActive, AssignedAt
FROM dbo.UserTeamScopes
ORDER BY UserId, IsPrimary DESC, TeamId;
