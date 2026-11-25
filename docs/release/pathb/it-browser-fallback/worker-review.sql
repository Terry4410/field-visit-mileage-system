-- Disposable worker subset; no broad role membership.
IF DB_NAME()<>N'FieldVisitCompanyCoreTest' OR USER_NAME()<>N'dbo' THROW 51000,'DISPOSABLE_ONLY',1;
GRANT SELECT,INSERT,UPDATE ON dbo.BackgroundJobs TO FieldVisitCoreRuntimeReview;
GRANT SELECT,INSERT,UPDATE ON dbo.BackgroundJobItems TO FieldVisitCoreRuntimeReview;
