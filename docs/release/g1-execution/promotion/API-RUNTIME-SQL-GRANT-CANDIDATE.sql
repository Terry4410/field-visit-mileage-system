-- SOURCE-BOUND CANDIDATE ONLY; NOT FOR LIVE EXECUTION.
-- Runtime grants are table-scoped and distinct from migration operator rights.
-- Review the companion CRUD matrix and prove all business call paths before any live cutover.
-- RETURN exits this batch before any grant statement. SQL2022 test composes grants only in disposable DB.
RETURN;
-- Candidate statements below are review data; remove no guard without a separate approved change.
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.SchemaVersions TO [api_runtime];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.SchemaMigrationDataBaselines TO [api_runtime];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.FieldVisitMigrationLedger TO [api_runtime];
