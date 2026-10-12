/* CANDIDATE ONLY. Forward-only. Existing/backfilled records remain HistoricalUnverified. */
SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRANSACTION;
ALTER TABLE dbo.TeamLeaderAssignments ADD
 GrantStatus nvarchar(30) NOT NULL CONSTRAINT DF_TLA_GrantStatus DEFAULT(N'HistoricalUnverified'),
 AppointmentType nvarchar(30) NULL, ProvenanceType nvarchar(50) NULL, EvidenceReference nvarchar(200) NULL,
 ApprovedByUserId int NULL, ApprovedAt datetime2(3) NULL, RevokedByUserId int NULL, RevokedAt datetime2(3) NULL,
 RevocationReason nvarchar(500) NULL, UpdatedAt datetime2(3) NULL, UpdatedByUserId int NULL;
ALTER TABLE dbo.TeamLeaderDelegations ADD
 GrantStatus nvarchar(30) NOT NULL CONSTRAINT DF_TLD_GrantStatus DEFAULT(N'HistoricalUnverified'),
 Scope nvarchar(50) NOT NULL CONSTRAINT DF_TLD_Scope DEFAULT(N'LocationMaintenance'), ProvenanceType nvarchar(50) NULL,
 EvidenceReference nvarchar(200) NULL, ApprovedByUserId int NULL, ApprovedAt datetime2(3) NULL,
 RevokedByUserId int NULL, RevokedAt datetime2(3) NULL, RevocationReason nvarchar(500) NULL,
 UpdatedAt datetime2(3) NULL, UpdatedByUserId int NULL;
ALTER TABLE dbo.TeamLeaderAssignments WITH CHECK ADD CONSTRAINT CK_TLA_TrustedEvidence CHECK
 (GrantStatus<>N'Active' OR (AppointmentType=N'FormalAppointment' AND ProvenanceType=N'AdminMaintained' AND EvidenceReference IS NOT NULL AND ApprovedByUserId IS NOT NULL AND ApprovedAt IS NOT NULL AND RevokedAt IS NULL));
ALTER TABLE dbo.TeamLeaderDelegations WITH CHECK ADD CONSTRAINT CK_TLD_TrustedEvidence CHECK
 (GrantStatus<>N'Active' OR (Scope=N'LocationMaintenance' AND ProvenanceType=N'AdminMaintained' AND EvidenceReference IS NOT NULL AND ApprovedByUserId IS NOT NULL AND ApprovedAt IS NOT NULL AND RevokedAt IS NULL));
CREATE INDEX IX_TLA_Trusted_AsOf ON dbo.TeamLeaderAssignments(EmploymentId,TeamId,EffectiveFrom,EffectiveTo,GrantStatus) INCLUDE(EvidenceReference,ApprovedAt,RevokedAt);
CREATE INDEX IX_TLD_Trusted_AsOf ON dbo.TeamLeaderDelegations(DelegateEmploymentId,EffectiveFrom,EffectiveTo,GrantStatus) INCLUDE(TeamLeaderAssignmentId,EvidenceReference,ApprovedAt,RevokedAt);
INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy) VALUES(N'1.8.0-013-CANDIDATE',N'Candidate only: VAL-B1-002 trusted admin-maintained leader grants',SYSUTCDATETIME(),N'ControlledCandidate');
COMMIT;
