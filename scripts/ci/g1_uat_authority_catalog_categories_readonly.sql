-- G1 UAT 010 bounded classification only; never project rows, counts, identifiers, or metadata text.
-- State=PRESENT indicates the specified condition exists, not necessarily data corruption.
-- All flags are scoped to the same fixed UAT diagnostic principal and must remain read-only.
SET NOCOUNT ON;
SELECT code,state FROM (VALUES
 (N'014_ORG_NULL',CASE WHEN EXISTS(
  SELECT 1 FROM dbo.Locations l WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1 AND l.OrganizationId IS NULL
 ) THEN N'PRESENT' ELSE N'ABSENT' END),
 (N'014_ORG_DANGLING',CASE WHEN EXISTS(
  SELECT 1 FROM dbo.Locations l LEFT JOIN dbo.Organizations o ON o.OrganizationId=l.OrganizationId
  WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1 AND l.OrganizationId IS NOT NULL AND o.OrganizationId IS NULL
 ) THEN N'PRESENT' ELSE N'ABSENT' END),
 (N'014_TEAM_NULL',CASE WHEN EXISTS(
  SELECT 1 FROM dbo.Locations l WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1 AND l.TeamId IS NULL
 ) THEN N'PRESENT' ELSE N'ABSENT' END),
 (N'014_TEAM_DANGLING',CASE WHEN EXISTS(
  SELECT 1 FROM dbo.Locations l LEFT JOIN dbo.Teams t ON t.TeamId=l.TeamId
  WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1 AND l.TeamId IS NOT NULL AND t.TeamId IS NULL
 ) THEN N'PRESENT' ELSE N'ABSENT' END),
 (N'014_CREATOR_NULL',CASE WHEN EXISTS(
  SELECT 1 FROM dbo.Locations l WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1 AND l.CreatedByUserId IS NULL
 ) THEN N'PRESENT' ELSE N'ABSENT' END),
 (N'014_CREATOR_DANGLING',CASE WHEN EXISTS(
  SELECT 1 FROM dbo.Locations l LEFT JOIN dbo.Users u ON u.UserId=l.CreatedByUserId
  WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1 AND l.CreatedByUserId IS NOT NULL AND u.UserId IS NULL
 ) THEN N'PRESENT' ELSE N'ABSENT' END),
 (N'014_TEAM_ORG_MISMATCH',CASE WHEN EXISTS(
  SELECT 1 FROM dbo.Locations l JOIN dbo.Teams t ON t.TeamId=l.TeamId
  WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1 AND l.OrganizationId IS NOT NULL
   AND t.OrganizationId<>l.OrganizationId
 ) THEN N'PRESENT' ELSE N'ABSENT' END),
 (N'014_CREATOR_ORG_MISMATCH',CASE WHEN EXISTS(
  SELECT 1 FROM dbo.Locations l JOIN dbo.Users u ON u.UserId=l.CreatedByUserId
  WHERE l.ApprovalStatus=N'Approved' AND l.IsActive=1 AND l.OrganizationId IS NOT NULL
   AND u.OrganizationId<>l.OrganizationId
 ) THEN N'PRESENT' ELSE N'ABSENT' END),
 (N'CATALOG_LOCATION_DEPLOYMENT_TRIGGER',CASE WHEN
  OBJECT_ID(N'dbo.TR_Locations_ProtectCurrentDeploymentSiteLocations',N'TR') IS NOT NULL
  THEN N'PRESENT' ELSE N'ABSENT' END),
 (N'CATALOG_LOCATION_GEOCODE_TRIGGER',CASE WHEN
  OBJECT_ID(N'dbo.TR_Locations_SelectedGeocodingAttempt',N'TR') IS NOT NULL
  THEN N'PRESENT' ELSE N'ABSENT' END),
 (N'CATALOG_IMPORT_TRIGGERS',CASE WHEN EXISTS(
  SELECT 1 FROM sys.triggers tr
  WHERE OBJECT_NAME(tr.parent_id) IN(N'ImportBatches',N'ImportBatchItems')
  ) THEN N'PRESENT' ELSE N'ABSENT' END),
 (N'CATALOG_IMPORT_CHECK_MISSING',CASE WHEN
  OBJECT_ID(N'dbo.CK_ImportBatches_Status',N'C') IS NULL
  OR OBJECT_ID(N'dbo.CK_ImportBatchItems_Status',N'C') IS NULL
  THEN N'PRESENT' ELSE N'ABSENT' END),
 (N'CATALOG_IMPORT_CHECK_UNTRUSTED',CASE WHEN EXISTS(
  SELECT 1 FROM sys.check_constraints c WHERE
  c.name IN(N'CK_ImportBatches_Status',N'CK_ImportBatchItems_Status')
  AND OBJECT_NAME(c.parent_object_id) IN(N'ImportBatches',N'ImportBatchItems')
  AND (c.is_not_trusted=1 OR c.is_disabled=1)
  ) THEN N'PRESENT' ELSE N'ABSENT' END),
 (N'CATALOG_MERGE_DETAILS_OBJECT',CASE WHEN
  OBJECT_ID(N'dbo.LocationMergeDetails',N'U') IS NOT NULL
  THEN N'PRESENT' ELSE N'ABSENT' END)
) f(code,state) ORDER BY code;
