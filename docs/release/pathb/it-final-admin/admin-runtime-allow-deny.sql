IF DB_NAME()<>N'FieldVisitCompanyCoreTest' THROW 51000,'DISPOSABLE_ONLY',1;
EXECUTE AS USER=N'fv_core_test';
IF HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'SELECT')<>1
 OR HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'INSERT')<>1
 OR HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'UPDATE',N'CenterName',N'COLUMN')<>1
 OR HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'DELETE')<>0
 OR HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'INSERT')<>1
 OR HAS_PERMS_BY_NAME(N'dbo.ImportBatches',N'OBJECT',N'INSERT')<>1
 THROW 51000,'ACL_MATRIX_FAILED',1;
IF IS_MEMBER(N'db_owner')<>0 OR IS_MEMBER(N'db_datareader')<>0 OR IS_MEMBER(N'db_datawriter')<>0
 THROW 51000,'BROAD_ROLE_DENIED',1;
INSERT dbo.Centers(OrganizationId,CenterCode,CenterName,EffectiveFrom,EffectiveTo,IsActive)
 VALUES (1,N'ACL-C',N'ACL Create','2026-11-01',NULL,1);
UPDATE dbo.Centers SET CenterName=N'ACL Update' WHERE CenterCode=N'ACL-C';
IF (SELECT COUNT(*) FROM dbo.Centers WHERE CenterCode=N'ACL-C' AND CenterName=N'ACL Update')<>1
 THROW 51000,'ACL_POSITIVE_DML_FAILED',1;
PRINT 'ADMIN_RUNTIME_SQL_POSITIVE=PASS Center INSERT/UPDATE/SELECT';
DECLARE @deleteDenied bit=0;
BEGIN TRY
 EXEC(N'DELETE FROM dbo.Centers WHERE CenterCode=N''ACL-C''');
END TRY
BEGIN CATCH
 IF ERROR_NUMBER()=229 SET @deleteDenied=1;
 ELSE THROW;
END CATCH;
IF @deleteDenied<>1 OR (SELECT COUNT(*) FROM dbo.Centers WHERE CenterCode=N'ACL-C')<>1
 THROW 51000,'ACL_DELETE_DENIAL_FAILED',1;
PRINT 'ADMIN_RUNTIME_SQL_NEGATIVE=PASS DELETE denied and row unchanged';
REVERT;

-- Extend the existing disposable-only proof to the remaining basic master DML.
-- No new grants; do not run against Azure or company databases.
DECLARE @orgId int=(SELECT OrganizationId FROM dbo.Organizations WHERE OrganizationCode=N'ACL');
IF @orgId IS NULL THROW 51000,'SYNTHETIC_ORG_REQUIRED',1;
INSERT dbo.Teams(OrganizationId,TeamCode,TeamName,IsActive,CreatedAt)
 VALUES(@orgId,N'ACL-T',N'Synthetic ACL Team',1,SYSUTCDATETIME());
DECLARE @teamId int=CONVERT(int,SCOPE_IDENTITY());
EXECUTE AS USER=N'fv_core_test';
BEGIN TRY
    INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive,CreatedAt)
     VALUES(@orgId,N'ACL rate',N'MOTORCYCLE',2.50,'2026-11-01',NULL,1,SYSUTCDATETIME());
    UPDATE dbo.MileageRateRules SET RatePerKm=3.25,IsActive=0
     WHERE OrganizationId=@orgId AND RuleName=N'ACL rate';
    IF (SELECT COUNT(*) FROM dbo.MileageRateRules WHERE OrganizationId=@orgId AND RuleName=N'ACL rate' AND RatePerKm=3.25 AND IsActive=0)<>1
      THROW 51000,'RATE_READBACK_FAILED',1;

    INSERT dbo.Projects(OrganizationId,TeamId,ProjectCode,ProjectName,Description,LocationMode,StartDate,EndDate,IsActive,CreatedAt)
     VALUES(@orgId,@teamId,N'ACL-P',N'Synthetic ACL Project',N'Synthetic',N'List','2026-11-01',NULL,1,SYSUTCDATETIME());
    UPDATE dbo.Projects SET ProjectName=N'ACL project updated',IsActive=0 WHERE OrganizationId=@orgId AND ProjectCode=N'ACL-P';
    IF (SELECT COUNT(*) FROM dbo.Projects WHERE OrganizationId=@orgId AND ProjectCode=N'ACL-P' AND ProjectName=N'ACL project updated' AND IsActive=0)<>1
      THROW 51000,'PROJECT_READBACK_FAILED',1;

    INSERT dbo.VisitTypes(VisitTypeCode,VisitTypeName,Description,SortOrder,IsActive,CreatedAt)
     VALUES(N'ACL-V',N'Synthetic ACL Visit',N'Synthetic',123,1,SYSUTCDATETIME());
    UPDATE dbo.VisitTypes SET VisitTypeName=N'ACL visit updated',IsActive=0 WHERE VisitTypeCode=N'ACL-V';
    IF (SELECT COUNT(*) FROM dbo.VisitTypes WHERE VisitTypeCode=N'ACL-V' AND VisitTypeName=N'ACL visit updated' AND IsActive=0)<>1
      THROW 51000,'VISIT_TYPE_READBACK_FAILED',1;

    PRINT 'ADMIN_RUNTIME_MASTER_DML=PASS rate/project/visit-type insert update deactivate readback';

    DECLARE @targets TABLE (TableName sysname, Predicate nvarchar(300));
    INSERT @targets VALUES
      (N'MileageRateRules',N'RuleName=N''ACL rate'''),
      (N'Projects',N'ProjectCode=N''ACL-P'''),
      (N'VisitTypes',N'VisitTypeCode=N''ACL-V''');
    DECLARE @table sysname, @predicate nvarchar(300), @stmt nvarchar(max), @denied bit, @remaining int;
    DECLARE deny_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName,Predicate FROM @targets;
    OPEN deny_cursor;
    FETCH NEXT FROM deny_cursor INTO @table,@predicate;
    WHILE @@FETCH_STATUS=0
    BEGIN
      SET @denied=0;
      SET @stmt=N'DELETE FROM dbo.'+QUOTENAME(@table)+N' WHERE '+@predicate;
      BEGIN TRY
        EXEC sys.sp_executesql @stmt;
      END TRY
      BEGIN CATCH
        IF ERROR_NUMBER()=229 SET @denied=1;
        ELSE THROW;
      END CATCH;
      SET @stmt=N'SELECT @n=COUNT(*) FROM dbo.'+QUOTENAME(@table)+N' WHERE '+@predicate;
      EXEC sys.sp_executesql @stmt,N'@n int OUTPUT',@n=@remaining OUTPUT;
      IF @denied<>1 OR @remaining<>1 THROW 51000,'MASTER_DELETE_DENIAL_FAILED',1;
      PRINT N'ADMIN_RUNTIME_MASTER_DELETE_DENIED=PASS '+@table;
      FETCH NEXT FROM deny_cursor INTO @table,@predicate;
    END;
    CLOSE deny_cursor;
    DEALLOCATE deny_cursor;
    REVERT;
END TRY
BEGIN CATCH
    REVERT;
    THROW;
END CATCH;

-- Validate exact source-driven grants under the restricted account and preserve history boundaries.
EXECUTE AS USER=N'fv_core_test';
BEGIN TRY
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.VisitTypes',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_VisitTypes_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.VisitTypes',N'OBJECT',N'UPDATE',N'VisitTypeCode',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_VisitTypes_VisitTypeCode',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.VisitTypes',N'OBJECT',N'UPDATE',N'VisitTypeName',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_VisitTypes_VisitTypeName',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.VisitTypes',N'OBJECT',N'UPDATE',N'Description',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_VisitTypes_Description',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.VisitTypes',N'OBJECT',N'UPDATE',N'SortOrder',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_VisitTypes_SortOrder',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.VisitTypes',N'OBJECT',N'UPDATE',N'IsActive',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_VisitTypes_IsActive',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.VisitTypes',N'OBJECT',N'UPDATE',N'UpdatedAt',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_VisitTypes_UpdatedAt',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Projects',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_Projects_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Projects',N'OBJECT',N'UPDATE',N'TeamId',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Projects_TeamId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Projects',N'OBJECT',N'UPDATE',N'ProjectCode',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Projects_ProjectCode',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Projects',N'OBJECT',N'UPDATE',N'ProjectName',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Projects_ProjectName',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Projects',N'OBJECT',N'UPDATE',N'Description',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Projects_Description',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Projects',N'OBJECT',N'UPDATE',N'LocationMode',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Projects_LocationMode',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Projects',N'OBJECT',N'UPDATE',N'StartDate',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Projects_StartDate',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Projects',N'OBJECT',N'UPDATE',N'EndDate',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Projects_EndDate',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Projects',N'OBJECT',N'UPDATE',N'IsActive',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Projects_IsActive',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Projects',N'OBJECT',N'UPDATE',N'UpdatedAt',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Projects_UpdatedAt',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ProjectLocations',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_ProjectLocations_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ProjectLocations',N'OBJECT',N'UPDATE',N'IsActive',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_ProjectLocations_IsActive',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ProjectLocations',N'OBJECT',N'UPDATE',N'IsPrimary',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_ProjectLocations_IsPrimary',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_MileageRateRules_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'UPDATE',N'RuleName',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_MileageRateRules_RuleName',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'UPDATE',N'VehicleType',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_MileageRateRules_VehicleType',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'UPDATE',N'RatePerKm',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_MileageRateRules_RatePerKm',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'UPDATE',N'EffectiveFrom',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_MileageRateRules_EffectiveFrom',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'UPDATE',N'EffectiveTo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_MileageRateRules_EffectiveTo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'UPDATE',N'IsActive',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_MileageRateRules_IsActive',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'UPDATE',N'UpdatedAt',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_MileageRateRules_UpdatedAt',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_Centers_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'UPDATE',N'CenterCode',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Centers_CenterCode',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'UPDATE',N'CenterName',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Centers_CenterName',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'UPDATE',N'EffectiveFrom',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Centers_EffectiveFrom',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'UPDATE',N'EffectiveTo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Centers_EffectiveTo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'UPDATE',N'IsActive',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Centers_IsActive',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.DeploymentSites',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_DeploymentSites_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.DeploymentSites',N'OBJECT',N'UPDATE',N'CenterId',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_DeploymentSites_CenterId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.DeploymentSites',N'OBJECT',N'UPDATE',N'SiteCode',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_DeploymentSites_SiteCode',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.DeploymentSites',N'OBJECT',N'UPDATE',N'SiteName',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_DeploymentSites_SiteName',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.DeploymentSites',N'OBJECT',N'UPDATE',N'EffectiveFrom',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_DeploymentSites_EffectiveFrom',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.DeploymentSites',N'OBJECT',N'UPDATE',N'EffectiveTo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_DeploymentSites_EffectiveTo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.DeploymentSites',N'OBJECT',N'UPDATE',N'IsActive',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_DeploymentSites_IsActive',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.DeploymentSites',N'OBJECT',N'UPDATE',N'UpdatedAt',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_DeploymentSites_UpdatedAt',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.DeploymentSites',N'OBJECT',N'UPDATE',N'UpdatedByUserId',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_DeploymentSites_UpdatedByUserId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.DeploymentSites',N'OBJECT',N'UPDATE',N'InactivatedAt',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_DeploymentSites_InactivatedAt',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.DeploymentSites',N'OBJECT',N'UPDATE',N'InactivatedByUserId',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_DeploymentSites_InactivatedByUserId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.DeploymentSiteLocationAssignments',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_DeploymentSiteLocationAssignments_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.DeploymentSiteLocationAssignments',N'OBJECT',N'UPDATE',N'EffectiveTo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_DeploymentSiteLocationAssignments_EffectiveTo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.TeamCenterAssignments',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_TeamCenterAssignments_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.TeamCenterAssignments',N'OBJECT',N'UPDATE',N'CenterId',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_TeamCenterAssignments_CenterId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.TeamCenterAssignments',N'OBJECT',N'UPDATE',N'EffectiveFrom',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_TeamCenterAssignments_EffectiveFrom',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.TeamCenterAssignments',N'OBJECT',N'UPDATE',N'EffectiveTo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_TeamCenterAssignments_EffectiveTo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.TeamDeploymentSiteAssignments',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_TeamDeploymentSiteAssignments_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.TeamDeploymentSiteAssignments',N'OBJECT',N'UPDATE',N'DeploymentSiteId',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_TeamDeploymentSiteAssignments_DeploymentSiteId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.TeamDeploymentSiteAssignments',N'OBJECT',N'UPDATE',N'EffectiveFrom',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_TeamDeploymentSiteAssignments_EffectiveFrom',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.TeamDeploymentSiteAssignments',N'OBJECT',N'UPDATE',N'EffectiveTo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_TeamDeploymentSiteAssignments_EffectiveTo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.EmploymentDeploymentSiteAssignments',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_EmploymentDeploymentSiteAssignments_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.EmploymentDeploymentSiteAssignments',N'OBJECT',N'UPDATE',N'DeploymentSiteId',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_EmploymentDeploymentSiteAssignments_DeploymentSiteId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.EmploymentDeploymentSiteAssignments',N'OBJECT',N'UPDATE',N'IsPrimary',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_EmploymentDeploymentSiteAssignments_IsPrimary',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.EmploymentDeploymentSiteAssignments',N'OBJECT',N'UPDATE',N'EffectiveFrom',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_EmploymentDeploymentSiteAssignments_EffectiveFrom',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.EmploymentDeploymentSiteAssignments',N'OBJECT',N'UPDATE',N'EffectiveTo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_EmploymentDeploymentSiteAssignments_EffectiveTo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Teams',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_Teams_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Teams',N'OBJECT',N'UPDATE',N'TeamCode',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Teams_TeamCode',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Teams',N'OBJECT',N'UPDATE',N'TeamName',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Teams_TeamName',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Teams',N'OBJECT',N'UPDATE',N'IsActive',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Teams_IsActive',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Teams',N'OBJECT',N'UPDATE',N'UpdatedAt',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Teams_UpdatedAt',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ImportBatches',N'OBJECT',N'SELECT'),0)<>1 THROW 51000,'ADMIN_ACL_ImportBatches_SELECT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ImportBatches',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_ImportBatches_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ImportBatches',N'OBJECT',N'UPDATE',N'Status',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_ImportBatches_Status',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ImportBatches',N'OBJECT',N'UPDATE',N'ConfirmedAt',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_ImportBatches_ConfirmedAt',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ImportBatchItems',N'OBJECT',N'SELECT'),0)<>1 THROW 51000,'ADMIN_ACL_ImportBatchItems_SELECT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ImportBatchItems',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_ImportBatchItems_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ImportBatchItems',N'OBJECT',N'UPDATE',N'Status',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_ImportBatchItems_Status',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ImportBatchItems',N'OBJECT',N'UPDATE',N'ErrorMessage',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_ImportBatchItems_ErrorMessage',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Users',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_Users_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Users',N'OBJECT',N'UPDATE',N'EmployeeNo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Users_EmployeeNo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Users',N'OBJECT',N'UPDATE',N'DisplayName',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Users_DisplayName',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Users',N'OBJECT',N'UPDATE',N'Email',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Users_Email',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Users',N'OBJECT',N'UPDATE',N'IsActive',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Users_IsActive',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Users',N'OBJECT',N'UPDATE',N'TeamId',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Users_TeamId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Users',N'OBJECT',N'UPDATE',N'UpdatedAt',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Users_UpdatedAt',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Employments',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_Employments_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Employments',N'OBJECT',N'UPDATE',N'EmployeeNo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Employments_EmployeeNo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Employments',N'OBJECT',N'UPDATE',N'Email',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Employments_Email',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Employments',N'OBJECT',N'UPDATE',N'HireDate',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Employments_HireDate',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Employments',N'OBJECT',N'UPDATE',N'TerminationDate',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_Employments_TerminationDate',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Persons',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_Persons_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.EmploymentStatusPeriods',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_EmploymentStatusPeriods_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.EmploymentStatusPeriods',N'OBJECT',N'UPDATE',N'EmploymentStatus',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_EmploymentStatusPeriods_EmploymentStatus',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.EmploymentStatusPeriods',N'OBJECT',N'UPDATE',N'EffectiveFrom',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_EmploymentStatusPeriods_EffectiveFrom',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.EmploymentStatusPeriods',N'OBJECT',N'UPDATE',N'EffectiveTo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_EmploymentStatusPeriods_EffectiveTo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_UserIdentityProfiles_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles',N'OBJECT',N'UPDATE',N'ExternalOrganization',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserIdentityProfiles_ExternalOrganization',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles',N'OBJECT',N'UPDATE',N'ExternalTitle',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserIdentityProfiles_ExternalTitle',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles',N'OBJECT',N'UPDATE',N'AuthorizationFrom',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserIdentityProfiles_AuthorizationFrom',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles',N'OBJECT',N'UPDATE',N'AuthorizationTo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserIdentityProfiles_AuthorizationTo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles',N'OBJECT',N'UPDATE',N'IdentityProvider',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserIdentityProfiles_IdentityProvider',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles',N'OBJECT',N'UPDATE',N'EntraTenantId',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserIdentityProfiles_EntraTenantId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles',N'OBJECT',N'UPDATE',N'EntraObjectId',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserIdentityProfiles_EntraObjectId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles',N'OBJECT',N'UPDATE',N'UpdatedAt',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserIdentityProfiles_UpdatedAt',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserRoleAssignments',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_UserRoleAssignments_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserRoleAssignments',N'OBJECT',N'DELETE'),0)<>1 THROW 51000,'ADMIN_ACL_UserRoleAssignments_DELETE',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserRoleAssignments',N'OBJECT',N'UPDATE',N'EffectiveFrom',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserRoleAssignments_EffectiveFrom',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserRoleAssignments',N'OBJECT',N'UPDATE',N'EffectiveTo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserRoleAssignments_EffectiveTo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserRoles',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_UserRoles_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserRoles',N'OBJECT',N'DELETE'),0)<>1 THROW 51000,'ADMIN_ACL_UserRoles_DELETE',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserTeamAssignments',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_UserTeamAssignments_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserTeamAssignments',N'OBJECT',N'DELETE'),0)<>1 THROW 51000,'ADMIN_ACL_UserTeamAssignments_DELETE',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserTeamAssignments',N'OBJECT',N'UPDATE',N'EffectiveTo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserTeamAssignments_EffectiveTo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.TeamMemberships',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_TeamMemberships_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.TeamMemberships',N'OBJECT',N'DELETE'),0)<>1 THROW 51000,'ADMIN_ACL_TeamMemberships_DELETE',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.TeamMemberships',N'OBJECT',N'UPDATE',N'EffectiveTo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_TeamMemberships_EffectiveTo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserTeamScopes',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_UserTeamScopes_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserTeamScopes',N'OBJECT',N'UPDATE',N'IsActive',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserTeamScopes_IsActive',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserTeamScopes',N'OBJECT',N'UPDATE',N'IsPrimary',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserTeamScopes_IsPrimary',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserTeamScopes',N'OBJECT',N'UPDATE',N'EndedAt',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserTeamScopes_EndedAt',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserTeamScopes',N'OBJECT',N'UPDATE',N'AssignedAt',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserTeamScopes_AssignedAt',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserTeamScopes',N'OBJECT',N'UPDATE',N'AssignedByUserId',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserTeamScopes_AssignedByUserId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserDataScopes',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_UserDataScopes_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserDataScopes',N'OBJECT',N'DELETE'),0)<>1 THROW 51000,'ADMIN_ACL_UserDataScopes_DELETE',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserDataScopes',N'OBJECT',N'UPDATE',N'EffectiveTo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserDataScopes_EffectiveTo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserCapabilities',N'OBJECT',N'INSERT'),0)<>1 THROW 51000,'ADMIN_ACL_UserCapabilities_INSERT',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserCapabilities',N'OBJECT',N'DELETE'),0)<>1 THROW 51000,'ADMIN_ACL_UserCapabilities_DELETE',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserCapabilities',N'OBJECT',N'UPDATE',N'EffectiveTo',N'COLUMN'),0)<>1 THROW 51000,'ADMIN_ACL_UserCapabilities_EffectiveTo',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.VisitTypes',N'OBJECT',N'UPDATE',N'VisitTypeId',N'COLUMN'),0)<>0 THROW 51000,'UNEXPECTED_UPDATE_VisitTypes_VisitTypeId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Projects',N'OBJECT',N'UPDATE',N'OrganizationId',N'COLUMN'),0)<>0 THROW 51000,'UNEXPECTED_UPDATE_Projects_OrganizationId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'UPDATE',N'OrganizationId',N'COLUMN'),0)<>0 THROW 51000,'UNEXPECTED_UPDATE_MileageRateRules_OrganizationId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'UPDATE',N'OrganizationId',N'COLUMN'),0)<>0 THROW 51000,'UNEXPECTED_UPDATE_Centers_OrganizationId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.EmploymentStatusPeriods',N'OBJECT',N'UPDATE',N'EmploymentId',N'COLUMN'),0)<>0 THROW 51000,'UNEXPECTED_UPDATE_EmploymentStatusPeriods_EmploymentId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserRoleAssignments',N'OBJECT',N'UPDATE',N'UserId',N'COLUMN'),0)<>0 THROW 51000,'UNEXPECTED_UPDATE_UserRoleAssignments_UserId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.TeamMemberships',N'OBJECT',N'UPDATE',N'EmploymentId',N'COLUMN'),0)<>0 THROW 51000,'UNEXPECTED_UPDATE_TeamMemberships_EmploymentId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserIdentityProfiles',N'OBJECT',N'UPDATE',N'UserId',N'COLUMN'),0)<>0 THROW 51000,'UNEXPECTED_UPDATE_UserIdentityProfiles_UserId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ImportBatches',N'OBJECT',N'UPDATE',N'OrganizationId',N'COLUMN'),0)<>0 THROW 51000,'UNEXPECTED_UPDATE_ImportBatches_OrganizationId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ImportBatchItems',N'OBJECT',N'UPDATE',N'DataJson',N'COLUMN'),0)<>0 THROW 51000,'UNEXPECTED_UPDATE_ImportBatchItems_DataJson',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserTeamScopes',N'OBJECT',N'UPDATE',N'UserId',N'COLUMN'),0)<>0 THROW 51000,'UNEXPECTED_UPDATE_UserTeamScopes_UserId',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.AuditLogs',N'OBJECT',N'UPDATE',N'Action',N'COLUMN'),0)<>0 THROW 51000,'UNEXPECTED_UPDATE_AuditLogs_Action',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.AuditLogs',N'OBJECT',N'DELETE'),0)<>0 THROW 51000,'UNEXPECTED_DELETE_AuditLogs',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.VisitTripSnapshots',N'OBJECT',N'DELETE'),0)<>0 THROW 51000,'UNEXPECTED_DELETE_VisitTripSnapshots',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.VisitTripSnapshotStops',N'OBJECT',N'DELETE'),0)<>0 THROW 51000,'UNEXPECTED_DELETE_VisitTripSnapshotStops',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'DELETE'),0)<>0 THROW 51000,'UNEXPECTED_DELETE_SchemaVersions',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ImportBatches',N'OBJECT',N'DELETE'),0)<>0 THROW 51000,'UNEXPECTED_DELETE_ImportBatches',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.ImportBatchItems',N'OBJECT',N'DELETE'),0)<>0 THROW 51000,'UNEXPECTED_DELETE_ImportBatchItems',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Users',N'OBJECT',N'DELETE'),0)<>0 THROW 51000,'UNEXPECTED_DELETE_Users',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Employments',N'OBJECT',N'DELETE'),0)<>0 THROW 51000,'UNEXPECTED_DELETE_Employments',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Persons',N'OBJECT',N'DELETE'),0)<>0 THROW 51000,'UNEXPECTED_DELETE_Persons',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'DELETE'),0)<>0 THROW 51000,'UNEXPECTED_DELETE_Centers',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.DeploymentSites',N'OBJECT',N'DELETE'),0)<>0 THROW 51000,'UNEXPECTED_DELETE_DeploymentSites',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.VisitTypes',N'OBJECT',N'DELETE'),0)<>0 THROW 51000,'UNEXPECTED_DELETE_VisitTypes',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.Projects',N'OBJECT',N'DELETE'),0)<>0 THROW 51000,'UNEXPECTED_DELETE_Projects',1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.MileageRateRules',N'OBJECT',N'DELETE'),0)<>0 THROW 51000,'UNEXPECTED_DELETE_MileageRateRules',1;
    PRINT 'ADMIN_ROUTINE_COLUMN_SCOPE_MATRIX=PASS';
    REVERT;
END TRY
BEGIN CATCH
    REVERT;
    THROW;
END CATCH;
