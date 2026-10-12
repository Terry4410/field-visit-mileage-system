/* CANDIDATE ONLY — do not execute against UAT/Production without a separate Owner/DBA authorization. */
SET NOCOUNT ON; SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.TeamLeaderAssignments',N'U') IS NULL OR OBJECT_ID(N'dbo.TeamLeaderDelegations',N'U') IS NULL
    THROW 54610, N'VAL-B1-002 requires 1800_002 TeamLeader tables.', 1;
IF COL_LENGTH(N'dbo.TeamLeaderAssignments',N'GrantStatus') IS NOT NULL
    THROW 54611, N'VAL-B1-002 candidate already appears applied; stop for DBA reconciliation.', 1;
IF EXISTS(SELECT 1 FROM dbo.TeamLeaderAssignments WHERE TeamId IS NULL OR EmploymentId IS NULL)
    THROW 54612, N'Invalid historical appointment key; do not classify automatically.', 1;
SELECT N'PASS_PREFLIGHT_ONLY' AS Status, COUNT_BIG(*) AS HistoricalAssignments FROM dbo.TeamLeaderAssignments;
