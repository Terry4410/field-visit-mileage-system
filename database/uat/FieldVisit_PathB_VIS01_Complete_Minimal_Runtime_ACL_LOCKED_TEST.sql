-- VIS01 API context + basic draft-create ACL SQL; TEST-ONLY, intentionally locked. Both GUIDs replaced with synthetic values.
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
DECLARE @target TABLE(ObjectId int PRIMARY KEY, TableName sysname NOT NULL UNIQUE);
INSERT @target(ObjectId,TableName)
SELECT object_id,name FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo')
 AND name IN (N'Employments',N'EmploymentStatusPeriods',N'TeamMemberships',N'TeamCenterAssignments',N'EmploymentDeploymentSiteAssignments',N'TeamDeploymentSiteAssignments',N'DeploymentSites',N'Centers',N'DeploymentSiteLocationAssignments',N'Locations',N'VisitTrips',N'VisitTripStops',N'MileageCalculations',N'VisitTripStatusHistory',N'AuditLogs',N'Projects',N'VisitTypes',N'SchemaVersions');
IF (SELECT COUNT(*) FROM @target)<>18
 THROW 59596,'EIGHTEEN_EXACT_VIS01_TABLES_HOLD',1;
-- INSERT permission is restricted to exactly five immutable or append-path tables.
DECLARE @writable TABLE(ObjectId int PRIMARY KEY);
INSERT @writable(ObjectId)
SELECT ObjectId FROM @target WHERE TableName IN (N'VisitTrips',N'VisitTripStops',N'MileageCalculations',N'VisitTripStatusHistory',N'AuditLogs');
IF (SELECT COUNT(*) FROM @writable)<>5
 THROW 59596,'FIVE_EXACT_DRAFT_INSERT_TABLES_HOLD',1;
-- EF INSERT ... OUTPUT INSERTED.Identity needs SELECT on the returned identity column.
-- Do not expose entire audit/history records to the API principal.
DECLARE @outputColumns TABLE(ObjectId int NOT NULL PRIMARY KEY, ColumnId int NOT NULL);
INSERT @outputColumns(ObjectId,ColumnId)
SELECT t.ObjectId,c.column_id FROM @target t JOIN sys.columns c
 ON c.object_id=t.ObjectId
WHERE (t.TableName=N'VisitTripStatusHistory' AND c.name=N'VisitTripStatusHistoryId')
   OR (t.TableName=N'AuditLogs' AND c.name=N'AuditLogId');
IF (SELECT COUNT(*) FROM @outputColumns)<>2
 THROW 59596,'EXACT_TWO_OUTPUT_ID_COLUMNS_HOLD',1;
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
-- Count expected permission records, not distinct object IDs: 16 table SELECT + 5 INSERT + 2 identity-column SELECT = 23.
SELECT @existing=COUNT(*)
FROM sys.database_permissions p
JOIN @target t ON t.ObjectId=p.major_id
WHERE p.grantee_principal_id=@pid AND p.class=1
 AND p.state IN(N'G',N'W')
 AND ((p.minor_id=0 AND ((p.permission_name=N'SELECT' AND t.TableName NOT IN(N'VisitTripStatusHistory',N'AuditLogs'))
   OR (p.permission_name=N'INSERT' AND EXISTS(SELECT 1 FROM @writable w WHERE w.ObjectId=p.major_id))))
   OR (p.minor_id>0 AND p.permission_name=N'SELECT' AND EXISTS
     (SELECT 1 FROM @outputColumns o WHERE o.ObjectId=p.major_id AND o.ColumnId=p.minor_id)));
IF @existing=23
BEGIN
 SELECT N'PATHB_VIS01_MINIMAL_RUNTIME_ACL' AS Evidence,
  16 AS SelectObjects, 5 AS InsertObjects, 2 AS OutputIdColumnSelects, N'ALREADY_23_NO_WRITE_CATALOG_ONLY' AS State;
 RETURN;
END;
IF @existing<>0 THROW 59599,'PARTIAL_VIS01_GRANTS_HOLD_NO_WRITE',1;
BEGIN TRY
 BEGIN TRANSACTION;
 SELECT @existing=COUNT(*) FROM sys.database_permissions p
 JOIN @target t ON t.ObjectId=p.major_id
 WHERE p.grantee_principal_id=@pid AND p.class=1 AND p.state IN(N'G',N'W')
  AND ((p.minor_id=0 AND ((p.permission_name=N'SELECT' AND t.TableName NOT IN(N'VisitTripStatusHistory',N'AuditLogs'))
   OR (p.permission_name=N'INSERT' AND EXISTS(SELECT 1 FROM @writable w WHERE w.ObjectId=p.major_id))))
   OR (p.minor_id>0 AND p.permission_name=N'SELECT' AND EXISTS
     (SELECT 1 FROM @outputColumns o WHERE o.ObjectId=p.major_id AND o.ColumnId=p.minor_id)));
 IF @existing<>0 THROW 59600,'CONCURRENT_VIS01_GRANTS_HOLD',1;
 GRANT SELECT ON OBJECT::dbo.[Employments] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[EmploymentStatusPeriods] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[TeamMemberships] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[TeamCenterAssignments] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[EmploymentDeploymentSiteAssignments] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[TeamDeploymentSiteAssignments] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[DeploymentSites] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[Centers] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[DeploymentSiteLocationAssignments] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[Locations] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[VisitTrips] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[VisitTripStops] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[MileageCalculations] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[Projects] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[VisitTypes] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[SchemaVersions] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT INSERT ON OBJECT::dbo.[VisitTrips] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT INSERT ON OBJECT::dbo.[VisitTripStops] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT INSERT ON OBJECT::dbo.[MileageCalculations] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT INSERT ON OBJECT::dbo.[VisitTripStatusHistory] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT INSERT ON OBJECT::dbo.[AuditLogs] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[VisitTripStatusHistory] ([VisitTripStatusHistoryId]) TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[AuditLogs] ([AuditLogId]) TO [api-fieldvisit-pathb-uat_pathb];
 SELECT @existing=COUNT(*) FROM sys.database_permissions p
 JOIN @target t ON t.ObjectId=p.major_id
 WHERE p.grantee_principal_id=@pid AND p.class=1 AND p.state IN(N'G',N'W')
  AND ((p.minor_id=0 AND ((p.permission_name=N'SELECT' AND t.TableName NOT IN(N'VisitTripStatusHistory',N'AuditLogs'))
   OR (p.permission_name=N'INSERT' AND EXISTS(SELECT 1 FROM @writable w WHERE w.ObjectId=p.major_id))))
   OR (p.minor_id>0 AND p.permission_name=N'SELECT' AND EXISTS
     (SELECT 1 FROM @outputColumns o WHERE o.ObjectId=p.major_id AND o.ColumnId=p.minor_id)));
 IF @existing<>23 THROW 59601,'VIS01_GRANTS_READBACK_HOLD',1;
 COMMIT TRANSACTION;
 SELECT N'PATHB_VIS01_CONTEXT_SELECT' AS Evidence,16 AS SelectObjects,5 AS InsertObjects,2 AS OutputIdColumnSelects,N'APPLIED_CATALOG_ONLY_NOT_VIS01_PASS' AS State;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
