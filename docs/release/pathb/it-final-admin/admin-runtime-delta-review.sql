-- Disposable SQL Server 2022 test role delta. NOT a corporate GRANT script.
-- Apply only on an empty isolated FieldVisitCompanyCoreTest after core-runtime-review.sql.
IF DB_NAME()<>N'FieldVisitCompanyCoreTest' OR USER_NAME()<>N'dbo'
    THROW 51000,'DISPOSABLE_ONLY',1;
GRANT INSERT, UPDATE ON OBJECT::dbo.MileageRateRules TO FieldVisitCoreRuntimeReview;
GRANT INSERT, UPDATE ON OBJECT::dbo.Projects TO FieldVisitCoreRuntimeReview;
GRANT INSERT, UPDATE ON OBJECT::dbo.VisitTypes TO FieldVisitCoreRuntimeReview;
GRANT INSERT, UPDATE ON OBJECT::dbo.Centers TO FieldVisitCoreRuntimeReview;
GRANT INSERT, UPDATE ON OBJECT::dbo.DeploymentSites TO FieldVisitCoreRuntimeReview;
GRANT INSERT, UPDATE ON OBJECT::dbo.DeploymentSiteLocationAssignments TO FieldVisitCoreRuntimeReview;
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.ImportBatches TO FieldVisitCoreRuntimeReview;
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.ImportBatchItems TO FieldVisitCoreRuntimeReview;
GRANT INSERT, UPDATE ON OBJECT::dbo.Users TO FieldVisitCoreRuntimeReview;
GRANT INSERT, UPDATE ON OBJECT::dbo.Employments TO FieldVisitCoreRuntimeReview;
GRANT INSERT, UPDATE ON OBJECT::dbo.EmploymentStatusPeriods TO FieldVisitCoreRuntimeReview;
GRANT INSERT ON OBJECT::dbo.TeamMemberships TO FieldVisitCoreRuntimeReview;
GRANT INSERT ON OBJECT::dbo.UserTeamAssignments TO FieldVisitCoreRuntimeReview;
-- No DELETE, DDL, schema-level rights, db_owner or broad datareader/datawriter membership.
