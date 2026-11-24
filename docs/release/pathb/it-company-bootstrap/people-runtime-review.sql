-- REVIEW candidate for restricted people/login/team flows ONLY.
-- DBA supplies pre-created database role [FieldVisitPeopleRuntimeReview]; no login creation here.
-- Test harness uses a disposable SQL login, not Windows identity validation.
-- Never use as complete application ACL. No broad db_datareader/db_datawriter/db_owner.
GRANT SELECT ON dbo.Organizations TO [FieldVisitPeopleRuntimeReview];
GRANT SELECT ON dbo.Teams TO [FieldVisitPeopleRuntimeReview];
GRANT SELECT ON dbo.Roles TO [FieldVisitPeopleRuntimeReview];
GRANT SELECT, INSERT ON dbo.Users TO [FieldVisitPeopleRuntimeReview];
GRANT UPDATE ON dbo.Users (TeamId,UpdatedAt) TO [FieldVisitPeopleRuntimeReview];
GRANT SELECT, INSERT ON dbo.Persons TO [FieldVisitPeopleRuntimeReview];
GRANT SELECT, INSERT ON dbo.Employments TO [FieldVisitPeopleRuntimeReview];
GRANT SELECT, INSERT ON dbo.EmploymentStatusPeriods TO [FieldVisitPeopleRuntimeReview];
GRANT SELECT, INSERT ON dbo.UserIdentityProfiles TO [FieldVisitPeopleRuntimeReview];
GRANT SELECT ON dbo.UserEmploymentPeriods TO [FieldVisitPeopleRuntimeReview];
GRANT SELECT, INSERT, DELETE ON dbo.UserRoleAssignments TO [FieldVisitPeopleRuntimeReview];
GRANT UPDATE ON dbo.UserRoleAssignments (EffectiveTo) TO [FieldVisitPeopleRuntimeReview];
GRANT SELECT, INSERT, DELETE ON dbo.UserRoles TO [FieldVisitPeopleRuntimeReview];
GRANT SELECT, INSERT, DELETE ON dbo.UserTeamAssignments TO [FieldVisitPeopleRuntimeReview];
GRANT UPDATE ON dbo.UserTeamAssignments (EffectiveTo) TO [FieldVisitPeopleRuntimeReview];
GRANT SELECT, INSERT, DELETE ON dbo.TeamMemberships TO [FieldVisitPeopleRuntimeReview];
GRANT UPDATE ON dbo.TeamMemberships (EffectiveTo) TO [FieldVisitPeopleRuntimeReview];
GRANT SELECT, INSERT ON dbo.UserTeamScopes TO [FieldVisitPeopleRuntimeReview];
GRANT UPDATE ON dbo.UserTeamScopes (IsActive,IsPrimary,EndedAt,AssignedAt,AssignedByUserId) TO [FieldVisitPeopleRuntimeReview];
GRANT INSERT ON dbo.AuditLogs TO [FieldVisitPeopleRuntimeReview];
GRANT SELECT ON dbo.AuditLogs (AuditLogId) TO [FieldVisitPeopleRuntimeReview];
