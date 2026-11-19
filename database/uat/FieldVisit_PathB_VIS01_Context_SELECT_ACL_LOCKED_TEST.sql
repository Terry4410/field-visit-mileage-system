-- VIS01 API 10-table ACL SQL; TEST-ONLY, intentionally locked. Both GUIDs replaced with synthetic values.
THROW 59590, 'LOCKED_REVIEW_NO_AZURE_DCL_AUTHORIZATION', 1;
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT<>0 OR (2 & @@OPTIONS)=2 THROW 59591, 'TRANSACTION_MODE_HOLD',1;
IF COALESCE(TRY_CONVERT(int,SERVERPROPERTY(N'EngineEdition')),0)<>5
 OR DB_NAME()<>N'db-fieldvisit-pathb-uat'
 OR CONVERT(nvarchar(256),SERVERPROPERTY(N'ServerName'))<>N'sql-fieldvisit-jpe-pathb-uat'
 THROW 59592,'EXACT_PATHB_TARGET_HOLD',1;
IF NOT EXISTS(SELECT 1 FROM sys.dm_exec_connections WHERE session_id=@@SPID AND encrypt_option=N'TRUE')
 THROW 59593,'ENCRYPTION_HOLD',1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION'),0)<>1
 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER ANY USER'),0)<>1
 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL'),0)<>1
 THROW 59594,'OWNER_AUTHORITY_HOLD',1;
DECLARE @alias sysname=N'api-fieldvisit-pathb-uat_pathb',@pid int;
DECLARE @objectId uniqueidentifier=CONVERT(uniqueidentifier,N'00000000-0000-0000-0000-000000000000');
DECLARE @clientId uniqueidentifier=CONVERT(uniqueidentifier,N'11111111-1111-1111-1111-111111111111');
SELECT @pid=principal_id FROM sys.database_principals
 WHERE name=@alias AND type=N'E' AND authentication_type_desc=N'EXTERNAL';
IF @pid IS NULL OR NOT EXISTS(SELECT 1 FROM sys.database_principals WHERE principal_id=@pid
    AND DATALENGTH(sid)=16 AND CAST(sid AS uniqueidentifier) IN(@objectId,@clientId))
 THROW 59595,'API_MI_CLIENT_OR_OBJECT_SID_HOLD',1;
IF (SELECT COUNT(*) FROM sys.database_principals p WHERE DATALENGTH(p.sid)=16
  AND CAST(p.sid AS uniqueidentifier)=(SELECT CAST(x.sid AS uniqueidentifier)
      FROM sys.database_principals x WHERE x.principal_id=@pid))<>1
 THROW 59595,'API_MI_SID_AMBIGUITY_HOLD',1;
DECLARE @target TABLE(ObjectId int PRIMARY KEY);
INSERT @target(ObjectId) SELECT object_id FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo')
  AND name IN(N'Employments',N'EmploymentStatusPeriods',N'TeamMemberships',N'TeamCenterAssignments',
   N'EmploymentDeploymentSiteAssignments',N'TeamDeploymentSiteAssignments',N'DeploymentSites',N'Centers',
   N'DeploymentSiteLocationAssignments',N'Locations');
IF (SELECT COUNT(*) FROM @target)<>10 THROW 59596,'TEN_EXACT_CONTEXT_TABLES_HOLD',1;
DECLARE @members TABLE(PrincipalId int PRIMARY KEY);
INSERT @members VALUES(@pid);
INSERT @members SELECT principal_id FROM sys.database_principals WHERE name=N'public' AND principal_id<>@pid;
DECLARE @depth int=0,@added int=1;
WHILE @added>0 AND @depth<16
BEGIN
 INSERT @members
 SELECT DISTINCT rm.role_principal_id FROM sys.database_role_members rm
 JOIN @members m ON m.PrincipalId=rm.member_principal_id
 WHERE NOT EXISTS(SELECT 1 FROM @members x WHERE x.PrincipalId=rm.role_principal_id);
 SET @added=@@ROWCOUNT;
 SET @depth=@depth+1;
END;
IF @added>0 THROW 59597,'ROLE_CLOSURE_DEPTH_HOLD',1;
IF EXISTS(SELECT 1 FROM @members m JOIN sys.database_principals p ON p.principal_id=m.PrincipalId
  WHERE p.name IN(N'db_owner',N'db_datareader',N'db_datawriter',N'db_securityadmin',N'db_ddladmin'))
 THROW 59597,'BROAD_RUNTIME_ROLE_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.schemas WHERE principal_id=@pid)
 OR EXISTS(SELECT 1 FROM sys.objects WHERE principal_id=@pid)
 THROW 59597,'API_OBJECT_OWNERSHIP_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.database_permissions p JOIN @members m ON m.PrincipalId=p.grantee_principal_id
 WHERE p.state=N'D' AND p.permission_name IN(N'SELECT',N'CONTROL')
  AND (p.class=0 OR (p.class=3 AND p.major_id=SCHEMA_ID(N'dbo'))
       OR (p.class=1 AND EXISTS(SELECT 1 FROM @target t WHERE t.ObjectId=p.major_id))))
 THROW 59598,'RELEVANT_DENY_HOLD',1;
DECLARE @existing int;
SELECT @existing=COUNT(DISTINCT p.major_id) FROM sys.database_permissions p JOIN @target t ON t.ObjectId=p.major_id
 WHERE p.grantee_principal_id=@pid AND p.class=1 AND p.minor_id=0 AND p.permission_name=N'SELECT' AND p.state IN(N'G',N'W');
IF @existing=10
BEGIN
 SELECT N'PATHB_VIS01_CONTEXT_SELECT' AS Evidence,10 AS GrantedObjects,N'ALREADY_TEN_NO_WRITE_CATALOG_ONLY' AS State;
 RETURN;
END;
IF @existing<>0 THROW 59599,'PARTIAL_CONTEXT_GRANTS_HOLD_NO_WRITE',1;
BEGIN TRY
 BEGIN TRANSACTION;
 SELECT @existing=COUNT(DISTINCT p.major_id) FROM sys.database_permissions p JOIN @target t ON t.ObjectId=p.major_id
  WHERE p.grantee_principal_id=@pid AND p.class=1 AND p.minor_id=0 AND p.permission_name=N'SELECT' AND p.state IN(N'G',N'W');
 IF @existing<>0 THROW 59600,'CONCURRENT_CONTEXT_GRANTS_HOLD',1;
 GRANT SELECT ON OBJECT::dbo.Employments TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.EmploymentStatusPeriods TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.TeamMemberships TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.TeamCenterAssignments TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.EmploymentDeploymentSiteAssignments TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.TeamDeploymentSiteAssignments TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.DeploymentSites TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.Centers TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.DeploymentSiteLocationAssignments TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.Locations TO [api-fieldvisit-pathb-uat_pathb];
 SELECT @existing=COUNT(DISTINCT p.major_id) FROM sys.database_permissions p JOIN @target t ON t.ObjectId=p.major_id
  WHERE p.grantee_principal_id=@pid AND p.class=1 AND p.minor_id=0 AND p.permission_name=N'SELECT' AND p.state IN(N'G',N'W');
 IF @existing<>10 THROW 59601,'TEN_CONTEXT_GRANTS_READBACK_HOLD',1;
 COMMIT TRANSACTION;
 SELECT N'PATHB_VIS01_CONTEXT_SELECT' AS Evidence,10 AS GrantedObjects,N'APPLIED_CATALOG_ONLY_NOT_RUNTIME_CONTEXT_PROOF' AS State;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
