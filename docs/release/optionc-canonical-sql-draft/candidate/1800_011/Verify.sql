/* 1800_011 candidate postflight -- read-only. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC)<>N'1.8.0-011'
    THROW 51120,N'Latest SchemaVersion is not 1.8.0-011.',1;
IF OBJECT_ID(N'dbo.ChangeRequests',N'U') IS NULL OR OBJECT_ID(N'dbo.ChangeRequestEvents',N'U') IS NULL
    THROW 51121,N'B3 tables are missing.',1;
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id IN(OBJECT_ID(N'dbo.ChangeRequests'),OBJECT_ID(N'dbo.ChangeRequestEvents')) AND is_disabled=0 AND is_not_trusted=0 AND delete_referential_action=0)<>6
    THROW 51122,N'B3 trusted NO ACTION foreign-key contract drift.',1;
IF (SELECT COUNT(*) FROM sys.indexes WHERE is_unique=1 AND is_disabled=0 AND is_hypothetical=0 AND
    ((object_id=OBJECT_ID(N'dbo.ChangeRequests') AND name IN(N'UX_B3_ChangeRequests_RequestPublicId',N'UX_B3_ChangeRequests_Org_Entity_Pending'))
      OR (object_id=OBJECT_ID(N'dbo.ChangeRequestEvents') AND name=N'UX_B3_ChangeRequestEvents_DecisionKey')) )<>3
    THROW 51123,N'B3 unique-index contract drift.',1;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE is_disabled=0 AND parent_id IN(OBJECT_ID(N'dbo.ChangeRequests'),OBJECT_ID(N'dbo.ChangeRequestEvents')))
    THROW 51124,N'B3 table trigger is prohibited.',1;
SELECT N'1.8.0-011' AS VerifiedVersion, CAST(1 AS bit) AS VerifyPassed;
