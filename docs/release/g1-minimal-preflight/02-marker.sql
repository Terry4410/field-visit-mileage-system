-- Run only after all 01-target flags PASS, on the SAME guarded connection.
-- No aliases, checksum columns or proposed migration ledger are referenced.
SELECT 'MARKER_010' AS code, CASE
 WHEN (SELECT TOP(1) VersionNumber FROM dbo.SchemaVersions ORDER BY AppliedAt DESC,VersionNumber DESC)=N'1.8.0-010'
 AND (SELECT COUNT_BIG(*) FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-010')=1
 AND NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber LIKE N'1.8.0-01[1-9]' OR VersionNumber=N'1.8.0-020')
 THEN 'PASS' ELSE 'FAIL' END AS status;
