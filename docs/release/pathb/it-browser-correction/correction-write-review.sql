-- Disposable correction write scope only; snapshots remain append-only.
IF DB_NAME()<>N'FieldVisitCompanyCoreTest' OR USER_NAME()<>N'dbo' THROW 51000,'DISPOSABLE_ONLY',1;
GRANT INSERT,UPDATE ON dbo.CorrectionRequests TO FieldVisitCoreRuntimeReview;
GRANT SELECT,INSERT ON dbo.CorrectionRequestChanges TO FieldVisitCoreRuntimeReview;
