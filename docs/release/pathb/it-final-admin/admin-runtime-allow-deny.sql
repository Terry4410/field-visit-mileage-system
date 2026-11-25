IF DB_NAME()<>N'FieldVisitCompanyCoreTest' THROW 51000,'DISPOSABLE_ONLY',1;
EXECUTE AS USER=N'fv_core_test';
IF HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'SELECT')<>1
 OR HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'INSERT')<>1
 OR HAS_PERMS_BY_NAME(N'dbo.Centers',N'OBJECT',N'UPDATE')<>1
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
