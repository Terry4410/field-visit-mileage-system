-- REVIEW ONLY: disposable SQL Server 2022; never run on Azure or corporate SQL.
-- Source c6c68685; live observation 279 rows, receipt 7153934527.
-- Existing core-runtime-review.sql is the base. Include only source-driven routine DML.
-- Physical master/person permanent deletion is a separate pending safety review,
-- not authorized here; only explicit role/scope/version replacement DELETEs included.
IF DB_NAME()<>N'FieldVisitCompanyCoreTest' OR USER_NAME()<>N'dbo'
    THROW 51000,'DISPOSABLE_ONLY',1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
GRANT INSERT ON OBJECT::dbo.[VisitTypes] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[VisitTypes] ([VisitTypeCode],[VisitTypeName],[Description],[SortOrder],[IsActive],[UpdatedAt]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[Projects] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[Projects] ([TeamId],[ProjectCode],[ProjectName],[Description],[LocationMode],[StartDate],[EndDate],[IsActive],[UpdatedAt]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[ProjectLocations] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[ProjectLocations] ([IsActive],[IsPrimary]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[MileageRateRules] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[MileageRateRules] ([RuleName],[VehicleType],[RatePerKm],[EffectiveFrom],[EffectiveTo],[IsActive],[UpdatedAt]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[Centers] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[Centers] ([CenterCode],[CenterName],[EffectiveFrom],[EffectiveTo],[IsActive]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[DeploymentSites] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[DeploymentSites] ([CenterId],[SiteCode],[SiteName],[EffectiveFrom],[EffectiveTo],[IsActive],[UpdatedAt],[UpdatedByUserId],[InactivatedAt],[InactivatedByUserId]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[DeploymentSiteLocationAssignments] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[DeploymentSiteLocationAssignments] ([EffectiveTo]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[TeamCenterAssignments] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[TeamCenterAssignments] ([CenterId],[EffectiveFrom],[EffectiveTo]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[TeamDeploymentSiteAssignments] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[TeamDeploymentSiteAssignments] ([DeploymentSiteId],[EffectiveFrom],[EffectiveTo]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[EmploymentDeploymentSiteAssignments] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[EmploymentDeploymentSiteAssignments] ([DeploymentSiteId],[IsPrimary],[EffectiveFrom],[EffectiveTo]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[Teams] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[Teams] ([TeamCode],[TeamName],[IsActive],[UpdatedAt]) TO [FieldVisitCoreRuntimeReview];
GRANT SELECT, INSERT ON OBJECT::dbo.[ImportBatches] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[ImportBatches] ([Status],[ConfirmedAt]) TO [FieldVisitCoreRuntimeReview];
GRANT SELECT, INSERT ON OBJECT::dbo.[ImportBatchItems] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[ImportBatchItems] ([Status],[ErrorMessage]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[Users] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[Users] ([EmployeeNo],[DisplayName],[Email],[IsActive],[TeamId],[UpdatedAt]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[Employments] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[Employments] ([EmployeeNo],[Email],[HireDate],[TerminationDate]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[Persons] TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[EmploymentStatusPeriods] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[EmploymentStatusPeriods] ([EmploymentStatus],[EffectiveFrom],[EffectiveTo]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[UserIdentityProfiles] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[UserIdentityProfiles] ([ExternalOrganization],[ExternalTitle],[AuthorizationFrom],[AuthorizationTo],[IdentityProvider],[EntraTenantId],[EntraObjectId],[UpdatedAt]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT, DELETE ON OBJECT::dbo.[UserRoleAssignments] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[UserRoleAssignments] ([EffectiveFrom],[EffectiveTo]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT, DELETE ON OBJECT::dbo.[UserRoles] TO [FieldVisitCoreRuntimeReview];
GRANT INSERT, DELETE ON OBJECT::dbo.[UserTeamAssignments] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[UserTeamAssignments] ([EffectiveTo]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT, DELETE ON OBJECT::dbo.[TeamMemberships] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[TeamMemberships] ([EffectiveTo]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT ON OBJECT::dbo.[UserTeamScopes] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[UserTeamScopes] ([IsActive],[IsPrimary],[EndedAt],[AssignedAt],[AssignedByUserId]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT, DELETE ON OBJECT::dbo.[UserDataScopes] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[UserDataScopes] ([EffectiveTo]) TO [FieldVisitCoreRuntimeReview];
GRANT INSERT, DELETE ON OBJECT::dbo.[UserCapabilities] TO [FieldVisitCoreRuntimeReview];
GRANT UPDATE ON OBJECT::dbo.[UserCapabilities] ([EffectiveTo]) TO [FieldVisitCoreRuntimeReview];
COMMIT;
