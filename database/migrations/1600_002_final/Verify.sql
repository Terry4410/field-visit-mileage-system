SET NOCOUNT ON;

SELECT N'ImportBatches' AS CheckItem, CASE WHEN OBJECT_ID(N'dbo.ImportBatches',N'U') IS NOT NULL THEN N'PASS' ELSE N'FAIL' END AS Result
UNION ALL SELECT N'ImportBatchItems',CASE WHEN OBJECT_ID(N'dbo.ImportBatchItems',N'U') IS NOT NULL THEN N'PASS' ELSE N'FAIL' END
UNION ALL SELECT N'LocationCode unique index',CASE WHEN EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_Locations_Organization_LocationCode' AND object_id=OBJECT_ID(N'dbo.Locations')) THEN N'PASS' ELSE N'FAIL' END
UNION ALL SELECT N'One primary team index',CASE WHEN EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_UserTeamScopes_OneActivePrimary' AND object_id=OBJECT_ID(N'dbo.UserTeamScopes')) THEN N'PASS' ELSE N'FAIL' END;

SELECT N'Missing LocationCode' AS CheckItem, COUNT(*) AS ErrorCount FROM dbo.Locations WHERE LocationCode IS NULL OR LTRIM(RTRIM(LocationCode))=N''
UNION ALL
SELECT N'Duplicate LocationCode', COUNT(*) FROM (
  SELECT OrganizationId,LocationCode FROM dbo.Locations WHERE LocationCode IS NOT NULL GROUP BY OrganizationId,LocationCode HAVING COUNT(*)>1
) d
UNION ALL
SELECT N'Multiple active primary teams', COUNT(*) FROM (
  SELECT UserId FROM dbo.UserTeamScopes WHERE IsActive=1 AND IsPrimary=1 GROUP BY UserId HAVING COUNT(*)>1
) d
UNION ALL
SELECT N'Duplicate active rate start dates', COUNT(*) FROM (
  SELECT ISNULL(OrganizationId,-1) OrgKey,VehicleType,EffectiveFrom FROM dbo.MileageRateRules WHERE IsActive=1 GROUP BY ISNULL(OrganizationId,-1),VehicleType,EffectiveFrom HAVING COUNT(*)>1
) d
UNION ALL
SELECT N'Approved trips missing Snapshot', COUNT(*)
FROM dbo.VisitTrips t
WHERE t.Status=N'Approved' AND NOT EXISTS(SELECT 1 FROM dbo.VisitTripSnapshots s WHERE s.VisitTripId=t.VisitTripId)
UNION ALL
SELECT N'Approved Snapshot missing Stop rows', COUNT(*)
FROM dbo.VisitTripSnapshots s
WHERE s.SnapshotVersion=(SELECT MAX(s2.SnapshotVersion) FROM dbo.VisitTripSnapshots s2 WHERE s2.VisitTripId=s.VisitTripId)
  AND EXISTS(SELECT 1 FROM dbo.VisitTripStops t WHERE t.VisitTripId=s.VisitTripId)
  AND NOT EXISTS(SELECT 1 FROM dbo.VisitTripSnapshotStops ss WHERE ss.VisitTripSnapshotId=s.VisitTripSnapshotId)
UNION ALL
SELECT N'Overlapping active rates', COUNT(*) FROM (
  SELECT a.MileageRateRuleId
  FROM dbo.MileageRateRules a JOIN dbo.MileageRateRules b
    ON ISNULL(a.OrganizationId,-1)=ISNULL(b.OrganizationId,-1) AND a.VehicleType=b.VehicleType AND a.MileageRateRuleId<b.MileageRateRuleId
   AND a.IsActive=1 AND b.IsActive=1
   AND a.EffectiveFrom<=ISNULL(b.EffectiveTo,'9999-12-31') AND b.EffectiveFrom<=ISNULL(a.EffectiveTo,'9999-12-31')
) d;

IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NOT NULL
  SELECT TOP 5 VersionNumber,Description,AppliedAt,AppliedBy FROM dbo.SchemaVersions ORDER BY AppliedAt DESC;

PRINT N'All ErrorCount values must be 0 and all Result values must be PASS.';
