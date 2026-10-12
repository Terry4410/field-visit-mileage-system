/* CANDIDATE ONLY — verifies no legacy row was silently activated. */
SET NOCOUNT ON; SET XACT_ABORT ON;
IF EXISTS(SELECT 1 FROM dbo.TeamLeaderAssignments WHERE GrantStatus=N'Active' AND (AppointmentType<>N'FormalAppointment' OR ProvenanceType<>N'AdminMaintained' OR EvidenceReference IS NULL OR ApprovedByUserId IS NULL OR ApprovedAt IS NULL OR RevokedAt IS NOT NULL))
 THROW 54620,N'Untrusted active appointment.',1;
IF EXISTS(SELECT 1 FROM dbo.TeamLeaderDelegations WHERE GrantStatus=N'Active' AND (Scope<>N'LocationMaintenance' OR ProvenanceType<>N'AdminMaintained' OR EvidenceReference IS NULL OR ApprovedByUserId IS NULL OR ApprovedAt IS NULL OR RevokedAt IS NOT NULL))
 THROW 54621,N'Untrusted active delegation.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.TeamLeaderAssignments') AND name=N'IX_TLA_Trusted_AsOf')
 THROW 54622,N'Missing trusted appointment index.',1;
SELECT N'PASS_CANDIDATE_VERIFY' AS Status,
 (SELECT COUNT_BIG(*) FROM dbo.TeamLeaderAssignments WHERE GrantStatus=N'HistoricalUnverified') AS HistoricalUnverifiedAppointments,
 (SELECT COUNT_BIG(*) FROM dbo.TeamLeaderAssignments WHERE GrantStatus=N'Active') AS ActiveTrustedAppointments;
