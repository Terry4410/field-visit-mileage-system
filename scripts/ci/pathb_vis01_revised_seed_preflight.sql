-- Make all schema/permission failures happen before the first write.
-- Fail closed on a new mandatory column without a default, not only the known 1800_001..003 tables.
IF EXISTS(SELECT 1 FROM (VALUES (N'Persons'),(N'Employments'),(N'EmploymentStatusPeriods'),(N'TeamMemberships'),(N'Centers'),(N'TeamCenterAssignments'),(N'DeploymentSites'),(N'Locations'),(N'DeploymentSiteLocationAssignments'),(N'TeamDeploymentSiteAssignments'),(N'EmploymentDeploymentSiteAssignments'),(N'VisitTypes'),(N'Projects')) AS v(t)
 WHERE COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+v.t,N'OBJECT',N'INSERT'),0)<>1)
 OR COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles',N'OBJECT',N'UPDATE'),0)<>1
 THROW 59513, 'VIS01_OWNER_DML_PERMISSION_HOLD',1;
DECLARE @expectedColumns TABLE(TableName sysname NOT NULL,ColumnName sysname NOT NULL,PRIMARY KEY(TableName,ColumnName));
INSERT @expectedColumns(TableName,ColumnName) VALUES
(N'Persons',N'DisplayName'),
(N'Persons',N'LegacyUserId'),
(N'Employments',N'PersonId'),
(N'Employments',N'OrganizationId'),
(N'Employments',N'EmployeeNo'),
(N'Employments',N'HireDate'),
(N'Employments',N'LegacyUserId'),
(N'Employments',N'SourceType'),
(N'Employments',N'SourceReference'),
(N'EmploymentStatusPeriods',N'EmploymentId'),
(N'EmploymentStatusPeriods',N'EmploymentStatus'),
(N'EmploymentStatusPeriods',N'EffectiveFrom'),
(N'EmploymentStatusPeriods',N'SourceType'),
(N'EmploymentStatusPeriods',N'SourceReference'),
(N'TeamMemberships',N'EmploymentId'),
(N'TeamMemberships',N'TeamId'),
(N'TeamMemberships',N'IsPrimary'),
(N'TeamMemberships',N'EffectiveFrom'),
(N'TeamMemberships',N'ChangeReason'),
(N'Centers',N'OrganizationId'),
(N'Centers',N'CenterCode'),
(N'Centers',N'CenterName'),
(N'Centers',N'EffectiveFrom'),
(N'Centers',N'IsActive'),
(N'TeamCenterAssignments',N'TeamId'),
(N'TeamCenterAssignments',N'CenterId'),
(N'TeamCenterAssignments',N'EffectiveFrom'),
(N'TeamCenterAssignments',N'ChangeReason'),
(N'DeploymentSites',N'CenterId'),
(N'DeploymentSites',N'SiteCode'),
(N'DeploymentSites',N'SiteName'),
(N'DeploymentSites',N'EffectiveFrom'),
(N'DeploymentSites',N'IsActive'),
(N'Locations',N'OrganizationId'),
(N'Locations',N'TeamId'),
(N'Locations',N'LocationCode'),
(N'Locations',N'LocationName'),
(N'Locations',N'LocationType'),
(N'Locations',N'PostalCode'),
(N'Locations',N'City'),
(N'Locations',N'District'),
(N'Locations',N'Address'),
(N'Locations',N'PlusCode'),
(N'Locations',N'Latitude'),
(N'Locations',N'Longitude'),
(N'Locations',N'IsTemporary'),
(N'Locations',N'ApprovalStatus'),
(N'Locations',N'GeocodingStatus'),
(N'Locations',N'GeocodedAt'),
(N'Locations',N'CreatedByUserId'),
(N'Locations',N'IsActive'),
(N'Locations',N'CreatedAt'),
(N'DeploymentSiteLocationAssignments',N'DeploymentSiteId'),
(N'DeploymentSiteLocationAssignments',N'LocationId'),
(N'DeploymentSiteLocationAssignments',N'EffectiveFrom'),
(N'DeploymentSiteLocationAssignments',N'ChangeReason'),
(N'TeamDeploymentSiteAssignments',N'TeamId'),
(N'TeamDeploymentSiteAssignments',N'DeploymentSiteId'),
(N'TeamDeploymentSiteAssignments',N'EffectiveFrom'),
(N'EmploymentDeploymentSiteAssignments',N'EmploymentId'),
(N'EmploymentDeploymentSiteAssignments',N'DeploymentSiteId'),
(N'EmploymentDeploymentSiteAssignments',N'IsPrimary'),
(N'EmploymentDeploymentSiteAssignments',N'EffectiveFrom'),
(N'VisitTypes',N'VisitTypeCode'),
(N'VisitTypes',N'VisitTypeName'),
(N'VisitTypes',N'Description'),
(N'VisitTypes',N'SortOrder'),
(N'VisitTypes',N'IsActive'),
(N'VisitTypes',N'CreatedAt'),
(N'Projects',N'OrganizationId'),
(N'Projects',N'TeamId'),
(N'Projects',N'ProjectCode'),
(N'Projects',N'ProjectName'),
(N'Projects',N'Description'),
(N'Projects',N'LocationMode'),
(N'Projects',N'StartDate'),
(N'Projects',N'IsActive'),
(N'Projects',N'CreatedAt');
INSERT @expectedColumns(TableName,ColumnName) VALUES
(N'UserIdentityProfiles',N'EmploymentId'),(N'UserIdentityProfiles',N'UpdatedAt');
IF EXISTS(SELECT 1 FROM @expectedColumns x WHERE NOT EXISTS
 (SELECT 1 FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id
  WHERE SCHEMA_NAME(t.schema_id)=N'dbo' AND t.name=x.TableName AND c.name=x.ColumnName))
 THROW 59514,'VIS01_EXPECTED_COLUMN_MISSING_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id
 LEFT JOIN sys.default_constraints d ON d.object_id=c.default_object_id
 LEFT JOIN @expectedColumns x ON x.TableName=t.name AND x.ColumnName=c.name
 WHERE SCHEMA_NAME(t.schema_id)=N'dbo'
  AND t.name IN (N'Persons',N'Employments',N'EmploymentStatusPeriods',N'TeamMemberships',N'Centers',N'TeamCenterAssignments',N'DeploymentSites',N'Locations',N'DeploymentSiteLocationAssignments',N'TeamDeploymentSiteAssignments',N'EmploymentDeploymentSiteAssignments',N'VisitTypes',N'Projects')
  AND c.is_nullable=0 AND c.is_identity=0 AND c.is_computed=0
  AND c.system_type_id<>189 AND d.object_id IS NULL AND x.ColumnName IS NULL)
 THROW 59515,'VIS01_UNKNOWN_REQUIRED_COLUMN_HOLD',1;

