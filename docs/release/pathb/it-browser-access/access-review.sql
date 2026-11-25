IF DB_NAME()<>N'FieldVisitCompanyCoreTest' OR USER_NAME()<>N'dbo' THROW 51000,'DISPOSABLE_ONLY',1;
-- REVIEW candidate for restricted people/login/team flows ONLY.
-- DBA supplies pre-created database role [FieldVisitCoreRuntimeReview]; no login creation here.
-- Test harness uses a disposable SQL login, not Windows identity validation.
-- Never use as complete application ACL. No broad db_datareader/db_datawriter/db_owner.
GRANT SELECT ON dbo.Organizations TO [FieldVisitCoreRuntimeReview];
GRANT SELECT ON dbo.Teams TO [FieldVisitCoreRuntimeReview];
GRANT SELECT ON dbo.Roles TO [FieldVisitCoreRuntimeReview];
GRANT SELECT, INSERT ON dbo.Users TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON dbo.Users (TeamId,UpdatedAt) TO [FieldVisitCoreRuntimeReview];
GRANT SELECT, INSERT ON dbo.Persons TO [FieldVisitCoreRuntimeReview];
GRANT SELECT, INSERT ON dbo.Employments TO [FieldVisitCoreRuntimeReview];
GRANT SELECT, INSERT ON dbo.EmploymentStatusPeriods TO [FieldVisitCoreRuntimeReview];
GRANT SELECT, INSERT ON dbo.UserIdentityProfiles TO [FieldVisitCoreRuntimeReview];
GRANT SELECT ON dbo.UserEmploymentPeriods TO [FieldVisitCoreRuntimeReview];
GRANT SELECT, INSERT, DELETE ON dbo.UserRoleAssignments TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON dbo.UserRoleAssignments (EffectiveTo) TO [FieldVisitCoreRuntimeReview];
GRANT SELECT, INSERT, DELETE ON dbo.UserRoles TO [FieldVisitCoreRuntimeReview];
GRANT SELECT, INSERT, DELETE ON dbo.UserTeamAssignments TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON dbo.UserTeamAssignments (EffectiveTo) TO [FieldVisitCoreRuntimeReview];
GRANT SELECT, INSERT, DELETE ON dbo.TeamMemberships TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON dbo.TeamMemberships (EffectiveTo) TO [FieldVisitCoreRuntimeReview];
GRANT SELECT, INSERT ON dbo.UserTeamScopes TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON dbo.UserTeamScopes (IsActive,IsPrimary,EndedAt,AssignedAt,AssignedByUserId) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON dbo.AuditLogs TO [FieldVisitCoreRuntimeReview];
GRANT SELECT ON dbo.AuditLogs (AuditLogId) TO [FieldVisitCoreRuntimeReview];

GRANT INSERT ON dbo.Teams TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON dbo.Teams (TeamCode,TeamName,IsActive,UpdatedAt) TO [FieldVisitCoreRuntimeReview];
