-- Isolated candidate grants, based on actual location search source and verified earlier HR writer scope.
IF DB_NAME()<>N'FieldVisitCompanyCoreTest' OR USER_NAME()<>N'dbo' THROW 51000,'DISPOSABLE_ONLY',1;
GRANT SELECT ON OBJECT::dbo.TeamLocationNotes TO FieldVisitCoreRuntimeReview;
GRANT SELECT ON OBJECT::dbo.TeamLocationNoteHistories TO FieldVisitCoreRuntimeReview;
GRANT SELECT ON OBJECT::dbo.GovernmentLocationMasters TO FieldVisitCoreRuntimeReview;
GRANT SELECT ON OBJECT::dbo.UserFavoriteLocations TO FieldVisitCoreRuntimeReview;
GRANT INSERT,UPDATE ON OBJECT::dbo.Users TO FieldVisitCoreRuntimeReview;
GRANT INSERT ON OBJECT::dbo.Persons TO FieldVisitCoreRuntimeReview;
GRANT INSERT,UPDATE ON OBJECT::dbo.Employments TO FieldVisitCoreRuntimeReview;
GRANT INSERT ON OBJECT::dbo.EmploymentStatusPeriods TO FieldVisitCoreRuntimeReview;
GRANT INSERT ON OBJECT::dbo.UserIdentityProfiles TO FieldVisitCoreRuntimeReview;
