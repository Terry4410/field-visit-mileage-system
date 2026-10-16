/* 1800_012 candidate postflight -- read-only. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF (SELECT TOP (1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC)<>N'1.8.0-012'
    THROW 51220,N'Latest SchemaVersion is not 1.8.0-012.',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ChangeRequests') AND name=N'CK_B3_ChangeRequests_KnownStatus' AND is_disabled=0 AND is_not_trusted=0 AND LOWER(definition) LIKE N'%applied%')
    THROW 51221,N'Applied request-state constraint is missing or untrusted.',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.ChangeRequestEvents') AND name=N'CK_B3_ChangeRequestEvents_EventType' AND is_disabled=0 AND is_not_trusted=0 AND LOWER(definition) LIKE N'%approved%' AND LOWER(definition) LIKE N'%applied%')
    THROW 51222,N'Approved/Applied event constraint is missing or untrusted.',1;
IF EXISTS(SELECT 1 FROM sys.triggers WHERE is_disabled=0 AND parent_id IN(OBJECT_ID(N'dbo.ChangeRequests'),OBJECT_ID(N'dbo.ChangeRequestEvents')))
    THROW 51223,N'Enabled B3 table trigger is prohibited.',1;
SELECT N'1.8.0-012' AS VerifiedVersion,CAST(1 AS bit) AS VerifyPassed;
