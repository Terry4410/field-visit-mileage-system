-- REVIEW ONLY. Live execution requires new explicit Owner approval and verified deployed API a0659574.
-- Only the exact isolated-tested grant inventory; no business DML, migration, role membership or infrastructure changes.
-- Receipt 7153934527; prior audit grant remains intact. Do not replay.
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- TARGET_GUARD_BEGIN
IF DB_NAME() COLLATE Latin1_General_100_BIN2 <> N'db-fieldvisit-pathb-uat'
 OR COALESCE(CONVERT(int,SERVERPROPERTY('EngineEdition')),0)<>5
 OR COALESCE(CONVERT(nvarchar(256),SERVERPROPERTY('ServerName')),N'') COLLATE Latin1_General_100_BIN2 NOT IN
 (N'sql-fieldvisit-jpe-pathb-uat',N'sql-fieldvisit-jpe-pathb-uat.database.windows.net')
 THROW 52001,N'WRONG_TARGET_STOP',1;
IF (SELECT COUNT(*) FROM sys.database_principals WHERE name=N'api-fieldvisit-pathb-uat_pathb'
 AND type='E' AND authentication_type_desc=N'EXTERNAL')<>1
 THROW 52003,N'EXPECTED_RUNTIME_PRINCIPAL_NOT_FOUND',1;
-- TARGET_GUARD_END
IF @@TRANCOUNT<>0 THROW 52002,N'EXISTING_TRANSACTION_STOP',1;
DECLARE @required TABLE(t sysname,p sysname,c sysname,PRIMARY KEY(t,p,c));
INSERT @required VALUES
 (N'VisitTypes',N'INSERT',N''),
 (N'VisitTypes',N'UPDATE',N'VisitTypeCode'),
 (N'VisitTypes',N'UPDATE',N'VisitTypeName'),
 (N'VisitTypes',N'UPDATE',N'Description'),
 (N'VisitTypes',N'UPDATE',N'SortOrder'),
 (N'VisitTypes',N'UPDATE',N'IsActive'),
 (N'VisitTypes',N'UPDATE',N'UpdatedAt'),
 (N'Projects',N'INSERT',N''),
 (N'Projects',N'UPDATE',N'TeamId'),
 (N'Projects',N'UPDATE',N'ProjectCode'),
 (N'Projects',N'UPDATE',N'ProjectName'),
 (N'Projects',N'UPDATE',N'Description'),
 (N'Projects',N'UPDATE',N'LocationMode'),
 (N'Projects',N'UPDATE',N'StartDate'),
 (N'Projects',N'UPDATE',N'EndDate'),
 (N'Projects',N'UPDATE',N'IsActive'),
 (N'Projects',N'UPDATE',N'UpdatedAt'),
 (N'ProjectLocations',N'INSERT',N''),
 (N'ProjectLocations',N'UPDATE',N'IsActive'),
 (N'ProjectLocations',N'UPDATE',N'IsPrimary'),
 (N'MileageRateRules',N'INSERT',N''),
 (N'MileageRateRules',N'UPDATE',N'RuleName'),
 (N'MileageRateRules',N'UPDATE',N'VehicleType'),
 (N'MileageRateRules',N'UPDATE',N'RatePerKm'),
 (N'MileageRateRules',N'UPDATE',N'EffectiveFrom'),
 (N'MileageRateRules',N'UPDATE',N'EffectiveTo'),
 (N'MileageRateRules',N'UPDATE',N'IsActive'),
 (N'MileageRateRules',N'UPDATE',N'UpdatedAt'),
 (N'Centers',N'INSERT',N''),
 (N'Centers',N'UPDATE',N'CenterCode'),
 (N'Centers',N'UPDATE',N'CenterName'),
 (N'Centers',N'UPDATE',N'EffectiveFrom'),
 (N'Centers',N'UPDATE',N'EffectiveTo'),
 (N'Centers',N'UPDATE',N'IsActive'),
 (N'DeploymentSites',N'INSERT',N''),
 (N'DeploymentSites',N'UPDATE',N'CenterId'),
 (N'DeploymentSites',N'UPDATE',N'SiteCode'),
 (N'DeploymentSites',N'UPDATE',N'SiteName'),
 (N'DeploymentSites',N'UPDATE',N'EffectiveFrom'),
 (N'DeploymentSites',N'UPDATE',N'EffectiveTo'),
 (N'DeploymentSites',N'UPDATE',N'IsActive'),
 (N'DeploymentSites',N'UPDATE',N'UpdatedAt'),
 (N'DeploymentSites',N'UPDATE',N'UpdatedByUserId'),
 (N'DeploymentSites',N'UPDATE',N'InactivatedAt'),
 (N'DeploymentSites',N'UPDATE',N'InactivatedByUserId'),
 (N'DeploymentSiteLocationAssignments',N'INSERT',N''),
 (N'DeploymentSiteLocationAssignments',N'UPDATE',N'EffectiveTo'),
 (N'TeamCenterAssignments',N'INSERT',N''),
 (N'TeamCenterAssignments',N'UPDATE',N'CenterId'),
 (N'TeamCenterAssignments',N'UPDATE',N'EffectiveFrom'),
 (N'TeamCenterAssignments',N'UPDATE',N'EffectiveTo'),
 (N'TeamDeploymentSiteAssignments',N'INSERT',N''),
 (N'TeamDeploymentSiteAssignments',N'UPDATE',N'DeploymentSiteId'),
 (N'TeamDeploymentSiteAssignments',N'UPDATE',N'EffectiveFrom'),
 (N'TeamDeploymentSiteAssignments',N'UPDATE',N'EffectiveTo'),
 (N'EmploymentDeploymentSiteAssignments',N'INSERT',N''),
 (N'EmploymentDeploymentSiteAssignments',N'UPDATE',N'DeploymentSiteId'),
 (N'EmploymentDeploymentSiteAssignments',N'UPDATE',N'IsPrimary'),
 (N'EmploymentDeploymentSiteAssignments',N'UPDATE',N'EffectiveFrom'),
 (N'EmploymentDeploymentSiteAssignments',N'UPDATE',N'EffectiveTo'),
 (N'Teams',N'INSERT',N''),
 (N'Teams',N'UPDATE',N'TeamCode'),
 (N'Teams',N'UPDATE',N'TeamName'),
 (N'Teams',N'UPDATE',N'IsActive'),
 (N'Teams',N'UPDATE',N'UpdatedAt'),
 (N'ImportBatches',N'SELECT',N''),
 (N'ImportBatches',N'INSERT',N''),
 (N'ImportBatches',N'UPDATE',N'Status'),
 (N'ImportBatches',N'UPDATE',N'ConfirmedAt'),
 (N'ImportBatchItems',N'SELECT',N''),
 (N'ImportBatchItems',N'INSERT',N''),
 (N'ImportBatchItems',N'UPDATE',N'Status'),
 (N'ImportBatchItems',N'UPDATE',N'ErrorMessage'),
 (N'Users',N'INSERT',N''),
 (N'Users',N'UPDATE',N'EmployeeNo'),
 (N'Users',N'UPDATE',N'DisplayName'),
 (N'Users',N'UPDATE',N'Email'),
 (N'Users',N'UPDATE',N'IsActive'),
 (N'Users',N'UPDATE',N'TeamId'),
 (N'Users',N'UPDATE',N'UpdatedAt'),
 (N'Employments',N'INSERT',N''),
 (N'Employments',N'UPDATE',N'EmployeeNo'),
 (N'Employments',N'UPDATE',N'Email'),
 (N'Employments',N'UPDATE',N'HireDate'),
 (N'Employments',N'UPDATE',N'TerminationDate'),
 (N'Persons',N'INSERT',N''),
 (N'EmploymentStatusPeriods',N'INSERT',N''),
 (N'EmploymentStatusPeriods',N'UPDATE',N'EmploymentStatus'),
 (N'EmploymentStatusPeriods',N'UPDATE',N'EffectiveFrom'),
 (N'EmploymentStatusPeriods',N'UPDATE',N'EffectiveTo'),
 (N'UserIdentityProfiles',N'INSERT',N''),
 (N'UserIdentityProfiles',N'UPDATE',N'ExternalOrganization'),
 (N'UserIdentityProfiles',N'UPDATE',N'ExternalTitle'),
 (N'UserIdentityProfiles',N'UPDATE',N'AuthorizationFrom'),
 (N'UserIdentityProfiles',N'UPDATE',N'AuthorizationTo'),
 (N'UserIdentityProfiles',N'UPDATE',N'IdentityProvider'),
 (N'UserIdentityProfiles',N'UPDATE',N'EntraTenantId'),
 (N'UserIdentityProfiles',N'UPDATE',N'EntraObjectId'),
 (N'UserIdentityProfiles',N'UPDATE',N'UpdatedAt'),
 (N'UserRoleAssignments',N'INSERT',N''),
 (N'UserRoleAssignments',N'DELETE',N''),
 (N'UserRoleAssignments',N'UPDATE',N'EffectiveFrom'),
 (N'UserRoleAssignments',N'UPDATE',N'EffectiveTo'),
 (N'UserRoles',N'INSERT',N''),
 (N'UserRoles',N'DELETE',N''),
 (N'UserTeamAssignments',N'INSERT',N''),
 (N'UserTeamAssignments',N'DELETE',N''),
 (N'UserTeamAssignments',N'UPDATE',N'EffectiveTo'),
 (N'TeamMemberships',N'INSERT',N''),
 (N'TeamMemberships',N'DELETE',N''),
 (N'TeamMemberships',N'UPDATE',N'EffectiveTo'),
 (N'UserTeamScopes',N'INSERT',N''),
 (N'UserTeamScopes',N'UPDATE',N'IsActive'),
 (N'UserTeamScopes',N'UPDATE',N'IsPrimary'),
 (N'UserTeamScopes',N'UPDATE',N'EndedAt'),
 (N'UserTeamScopes',N'UPDATE',N'AssignedAt'),
 (N'UserTeamScopes',N'UPDATE',N'AssignedByUserId'),
 (N'UserDataScopes',N'INSERT',N''),
 (N'UserDataScopes',N'DELETE',N''),
 (N'UserDataScopes',N'UPDATE',N'EffectiveTo'),
 (N'UserCapabilities',N'INSERT',N''),
 (N'UserCapabilities',N'DELETE',N''),
 (N'UserCapabilities',N'UPDATE',N'EffectiveTo'),
 (N'VisitTypes',N'DELETE',N''),
 (N'Projects',N'DELETE',N''),
 (N'ProjectLocations',N'DELETE',N''),
 (N'Centers',N'DELETE',N''),
 (N'DeploymentSites',N'DELETE',N''),
 (N'DeploymentSiteLocationAssignments',N'DELETE',N''),
 (N'Teams',N'DELETE',N''),
 (N'Users',N'DELETE',N''),
 (N'Employments',N'DELETE',N''),
 (N'Persons',N'DELETE',N''),
 (N'EmploymentStatusPeriods',N'DELETE',N''),
 (N'UserIdentityProfiles',N'DELETE',N''),
 (N'UserFavoriteLocations',N'DELETE',N''),
 (N'UserEmploymentPeriods',N'DELETE',N''),
 (N'UserTeamScopes',N'DELETE',N''),
 (N'EmploymentDeploymentSiteAssignments',N'DELETE',N''),
 (N'EmploymentRoleAssignments',N'DELETE',N''),
 (N'ApprovalRecords',N'SELECT',N'ApproverUserId'),
 (N'VisitTripStatusHistory',N'SELECT',N'ActionByUserId'),
 (N'CorrectionRequests',N'SELECT',N'RequestedByUserId'),
 (N'CorrectionRequests',N'SELECT',N'LeaderReviewedByUserId'),
 (N'CorrectionRequests',N'SELECT',N'AdminClosedByUserId'),
 (N'BackgroundJobs',N'SELECT',N'RequestedByUserId'),
 (N'GeocodingAttempts',N'SELECT',N'RequestedByUserId'),
 (N'MileageGovernanceEvents',N'SELECT',N'ActorUserId'),
 (N'AuditLogs',N'SELECT',N'UserId'),
 (N'TeamLocationNotes',N'SELECT',N'TeamId'),
 (N'TeamLocationNotes',N'SELECT',N'CreatedByUserId'),
 (N'TeamLocationNotes',N'SELECT',N'UpdatedByUserId'),
 (N'TeamLocationNoteHistory',N'SELECT',N'TeamId'),
 (N'TeamLocationNoteHistory',N'SELECT',N'ChangedByUserId'),
 (N'LocationApprovalHistory',N'SELECT',N'ReviewedByUserId'),
 (N'GovernmentLocationMasters',N'SELECT',N'ReviewedByUserId'),
 (N'UserFavoriteLocations',N'SELECT',N'UserId'),
 (N'EmploymentRoleAssignments',N'SELECT',N'EmploymentId');
DECLARE @protected TABLE(t sysname,p sysname,c sysname,PRIMARY KEY(t,p,c));
INSERT @protected VALUES
 (N'AuditLogs',N'DELETE',N''),
 (N'VisitTrips',N'DELETE',N''),
 (N'VisitTripSnapshots',N'DELETE',N''),
 (N'VisitTripSnapshotStops',N'DELETE',N''),
 (N'SchemaVersions',N'DELETE',N''),
 (N'ImportBatches',N'DELETE',N''),
 (N'ImportBatchItems',N'DELETE',N''),
 (N'ApprovalRecords',N'DELETE',N''),
 (N'MileageCalculations',N'DELETE',N''),
 (N'MileageGovernanceEvents',N'DELETE',N''),
 (N'RouteCalculationAttempts',N'DELETE',N''),
 (N'VisitTripStatusHistory',N'DELETE',N''),
 (N'TeamLocationNoteHistory',N'DELETE',N''),
 (N'Locations',N'DELETE',N''),
 (N'MileageRateRules',N'DELETE',N''),
 (N'AuditLogs',N'UPDATE',N''),
 (N'AuditLogs',N'SELECT',N'IpAddress'),
 (N'AuditLogs',N'SELECT',N'CorrelationId');
DECLARE @auditRequired TABLE(c sysname PRIMARY KEY);
INSERT @auditRequired VALUES(N'AuditLogId'),(N'UserId'),(N'EntityType'),(N'EntityId'),(N'Action'),(N'OldValues'),(N'NewValues'),(N'CreatedAt');
IF EXISTS(SELECT 1 FROM (SELECT t,c FROM @required UNION SELECT t,c FROM @protected) q
 WHERE OBJECT_ID(N'dbo.'+QUOTENAME(q.t),N'U') IS NULL
 OR (q.c<>N'' AND NOT EXISTS(SELECT 1 FROM sys.columns sc WHERE sc.object_id=OBJECT_ID(N'dbo.'+QUOTENAME(q.t),N'U') AND sc.name COLLATE DATABASE_DEFAULT=q.c COLLATE DATABASE_DEFAULT)))
 THROW 52004,N'OBJECT_OR_COLUMN_MISMATCH',1;
DECLARE @runtime int=DATABASE_PRINCIPAL_ID(N'api-fieldvisit-pathb-uat_pathb');
DECLARE @principals TABLE(id int PRIMARY KEY);
INSERT @principals VALUES(@runtime),(DATABASE_PRINCIPAL_ID(N'public'));
WHILE 1=1
BEGIN
 INSERT @principals SELECT DISTINCT m.role_principal_id FROM sys.database_role_members m
 JOIN @principals p ON p.id=m.member_principal_id
 WHERE NOT EXISTS(SELECT 1 FROM @principals x WHERE x.id=m.role_principal_id);
 IF @@ROWCOUNT=0 BREAK;
END;
-- Never overwrite a direct or inherited explicit DENY affecting candidate rights.
IF EXISTS(SELECT 1 FROM sys.database_permissions d JOIN @principals p ON p.id=d.grantee_principal_id
 JOIN @required q ON d.permission_name COLLATE DATABASE_DEFAULT IN(q.p COLLATE DATABASE_DEFAULT,N'CONTROL')
 WHERE d.state='D' AND (d.class=0 OR (d.class=3 AND d.major_id=SCHEMA_ID(N'dbo'))
 OR (d.class=1 AND d.major_id=OBJECT_ID(N'dbo.'+QUOTENAME(q.t),N'U')
 AND (d.minor_id=0 OR q.c=N'' OR d.minor_id=COLUMNPROPERTY(d.major_id,q.c,'ColumnId')))))
 THROW 52005,N'EXPLICIT_DENY_PRESENT_STOP',1;
DECLARE @missing TABLE(t sysname,p sysname,c sysname,PRIMARY KEY(t,p,c));
DECLARE @asRuntime bit=0,@count int=0,@statement nvarchar(max),@t sysname,@p sysname,@c sysname;
BEGIN TRY
 BEGIN TRANSACTION;
 EXECUTE AS USER=N'api-fieldvisit-pathb-uat_pathb'; SET @asRuntime=1;
 IF USER_NAME()<>N'api-fieldvisit-pathb-uat_pathb' THROW 52006,N'RUNTIME_CONTEXT_MISMATCH',1;
 IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.VisitTypes',N'OBJECT',N'INSERT'),-1)<>0
 OR COALESCE(HAS_PERMS_BY_NAME(N'dbo.VisitTypes',N'OBJECT',N'SELECT'),0)<>1
 OR EXISTS(SELECT 1 FROM dbo.VisitTypes WHERE VisitTypeCode=N'UAT-VT-1125B')
 THROW 52007,N'BASELINE_CHANGED_OR_REPLAY_STOP',1;
 IF EXISTS(SELECT 1 FROM @auditRequired q WHERE COALESCE(HAS_PERMS_BY_NAME(N'dbo.AuditLogs',N'OBJECT',N'SELECT',q.c,N'COLUMN'),0)<>1)
 THROW 52008,N'PRIOR_AUDIT_GRANT_NOT_PRESENT',1;
 IF EXISTS(SELECT 1 FROM @protected q WHERE CASE WHEN q.c=N'' THEN COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+QUOTENAME(q.t),N'OBJECT',q.p),0)
 ELSE COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+QUOTENAME(q.t),N'OBJECT',q.p,q.c,N'COLUMN'),0) END<>0)
 THROW 52009,N'PROTECTED_RIGHT_ALREADY_ALLOWED_STOP',1;
 IF EXISTS(SELECT 1 FROM (VALUES(N'db_owner'),(N'db_datareader'),(N'db_datawriter'),(N'db_ddladmin'),(N'db_securityadmin')) r(n) WHERE COALESCE(IS_ROLEMEMBER(r.n),-1)<>0)
 OR EXISTS(SELECT 1 FROM (VALUES(N'SELECT'),(N'INSERT'),(N'UPDATE'),(N'DELETE'),(N'CONTROL')) p(n)
 WHERE COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',p.n),-1)<>0 OR COALESCE(HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',p.n),-1)<>0)
 THROW 52010,N'BROAD_RUNTIME_RIGHT_STOP',1;
 INSERT @missing SELECT q.t,q.p,q.c FROM @required q
 WHERE CASE WHEN q.c=N'' THEN COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+QUOTENAME(q.t),N'OBJECT',q.p),0)
 ELSE COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+QUOTENAME(q.t),N'OBJECT',q.p,q.c,N'COLUMN'),0) END<>1;
 REVERT; SET @asRuntime=0;
 SELECT @count=COUNT(*) FROM @missing;
 IF @count=0 THROW 52011,N'NO_DELTA_STOP',1;
 DECLARE grants CURSOR LOCAL FAST_FORWARD FOR SELECT t,p,c FROM @missing ORDER BY t,p,c;
 OPEN grants; FETCH NEXT FROM grants INTO @t,@p,@c;
 WHILE @@FETCH_STATUS=0
 BEGIN
  SET @statement=N'GRANT '+@p+N' ON OBJECT::dbo.'+QUOTENAME(@t)
   +CASE WHEN @c=N'' THEN N'' ELSE N' ('+QUOTENAME(@c)+N')' END
   +N' TO [api-fieldvisit-pathb-uat_pathb];';
  EXEC sys.sp_executesql @statement;
  FETCH NEXT FROM grants INTO @t,@p,@c;
 END;
 CLOSE grants; DEALLOCATE grants;
 EXECUTE AS USER=N'api-fieldvisit-pathb-uat_pathb'; SET @asRuntime=1;
 IF EXISTS(SELECT 1 FROM @required q WHERE CASE WHEN q.c=N'' THEN COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+QUOTENAME(q.t),N'OBJECT',q.p),0)
 ELSE COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+QUOTENAME(q.t),N'OBJECT',q.p,q.c,N'COLUMN'),0) END<>1)
 THROW 52012,N'REQUIRED_EFFECTIVE_RIGHT_MISSING_ROLLBACK',1;
 IF EXISTS(SELECT 1 FROM @protected q WHERE CASE WHEN q.c=N'' THEN COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+QUOTENAME(q.t),N'OBJECT',q.p),0)
 ELSE COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+QUOTENAME(q.t),N'OBJECT',q.p,q.c,N'COLUMN'),0) END<>0)
 OR EXISTS(SELECT 1 FROM @auditRequired q WHERE COALESCE(HAS_PERMS_BY_NAME(N'dbo.AuditLogs',N'OBJECT',N'SELECT',q.c,N'COLUMN'),0)<>1)
 THROW 52013,N'PROTECTION_CHECK_FAILED_ROLLBACK',1;
 IF EXISTS(SELECT 1 FROM (VALUES(N'db_owner'),(N'db_datareader'),(N'db_datawriter'),(N'db_ddladmin'),(N'db_securityadmin')) r(n) WHERE COALESCE(IS_ROLEMEMBER(r.n),-1)<>0)
 OR EXISTS(SELECT 1 FROM (VALUES(N'SELECT'),(N'INSERT'),(N'UPDATE'),(N'DELETE'),(N'CONTROL')) p(n)
 WHERE COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',p.n),-1)<>0 OR COALESCE(HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',p.n),-1)<>0)
 THROW 52014,N'BROAD_RUNTIME_RIGHT_ROLLBACK',1;
 REVERT; SET @asRuntime=0;
 COMMIT TRANSACTION;
 SELECT N'PASS_ADMIN_RUNTIME_DELTA' AS result,DB_NAME() AS target_database,@count AS newly_granted_rights;
 SELECT t AS table_name,p AS permission_name,NULLIF(c,N'') AS column_name FROM @missing ORDER BY t,p,c;
END TRY
BEGIN CATCH
 IF @asRuntime=1 REVERT;
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
