-- REVIEW ONLY: P0 core Runtime ACL delta against Owner 2026-11-19 LIVE catalog evidence (20 operations).
-- NOT AUTHORIZED FOR AZURE EXECUTION. NONDEPLOY source; separate exact-SHA Owner approval mandatory.
-- Existing 23 VIS01 grants and two reservation column grants are preserved, never replayed.
-- No business-data SELECT/UPDATE/DELETE/INSERT; DCL below changes only identified API permissions.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT<>0 OR (2 & @@OPTIONS)=2
 THROW 59901,N'AMBIENT_TRANSACTION_HOLD',1;
IF COALESCE(TRY_CONVERT(int,SERVERPROPERTY(N'EngineEdition')),0)<>5
 OR CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')) COLLATE Latin1_General_100_BIN2<>N'sql-fieldvisit-jpe-pathb-uat'
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N'db-fieldvisit-pathb-uat'
 THROW 59902,N'EXACT_PATHB_TARGET_HOLD',1;
IF NOT EXISTS(SELECT 1 FROM sys.dm_exec_connections WHERE session_id=@@SPID AND encrypt_option=N'TRUE')
 THROW 59903,N'TLS_HOLD',1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL'),0)<>1
 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION'),0)<>1
 THROW 59904,N'OWNER_AUTHORITY_HOLD',1;
DECLARE @pid int;
SELECT @pid=principal_id FROM sys.database_principals
 WHERE name COLLATE DATABASE_DEFAULT=N'api-fieldvisit-pathb-uat_pathb' COLLATE DATABASE_DEFAULT
 AND type=N'E' AND authentication_type_desc=N'EXTERNAL' AND DATALENGTH(sid)=16;
IF @pid IS NULL
 OR (SELECT COUNT(*) FROM sys.database_principals WHERE principal_id=@pid)<>1
 THROW 59905,N'EXACT_API_PRINCIPAL_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.schemas WHERE principal_id=@pid)
 OR EXISTS(SELECT 1 FROM sys.objects WHERE principal_id=@pid)
 THROW 59906,N'API_OWNERSHIP_HOLD',1;
DECLARE @wanted TABLE (table_name sysname COLLATE DATABASE_DEFAULT NOT NULL,
 operation nvarchar(10) COLLATE DATABASE_DEFAULT NOT NULL,
 already_granted bit NOT NULL, PRIMARY KEY(table_name,operation));
INSERT @wanted VALUES
 (N'CorrectionRequests',N'INSERT',0),
 (N'CorrectionRequests',N'SELECT',0),
 (N'CorrectionRequests',N'UPDATE',0),
 (N'MileageCalculations',N'UPDATE',0),
 (N'MileageGovernanceEvents',N'INSERT',0),
 (N'Roles',N'SELECT',1),
 (N'RouteCalculationAttempts',N'INSERT',0),
 (N'RouteCalculationAttempts',N'SELECT',0),
 (N'RouteCalculationAttempts',N'UPDATE',0),
 (N'UserFavoriteLocations',N'SELECT',0),
 (N'UserIdentityProfiles',N'SELECT',1),
 (N'UserRoleAssignments',N'SELECT',1),
 (N'UserRoles',N'SELECT',0),
 (N'VisitTrips',N'UPDATE',0),
 (N'VisitTripSnapshots',N'INSERT',0),
 (N'VisitTripSnapshots',N'SELECT',0),
 (N'VisitTripSnapshotStops',N'INSERT',0),
 (N'VisitTripSnapshotStops',N'SELECT',0),
 (N'VisitTripStops',N'DELETE',0),
 (N'VisitTripStops',N'UPDATE',0);
IF (SELECT COUNT(*) FROM @wanted)<>20 OR (SELECT SUM(CONVERT(int,already_granted)) FROM @wanted)<>3
 THROW 59907,N'WANTED_CONTRACT_HOLD',1;
IF (SELECT COUNT(DISTINCT table_name) FROM @wanted)<>13
 THROW 59907,N'TABLE_SET_HOLD',1;
IF EXISTS(SELECT 1 FROM @wanted w WHERE OBJECT_ID(N'dbo.'+QUOTENAME(w.table_name),N'U') IS NULL)
 THROW 59908,N'REQUIRED_OBJECT_NOT_VISIBLE_HOLD',1;
DECLARE @members TABLE(pid int PRIMARY KEY);
INSERT @members VALUES(@pid);
INSERT @members SELECT principal_id FROM sys.database_principals
 WHERE name=N'public' AND principal_id<>@pid;
DECLARE @depth int=0,@added int=1;
WHILE @added>0 AND @depth<16
BEGIN
 INSERT @members(pid)
 SELECT DISTINCT rm.role_principal_id FROM sys.database_role_members rm
 JOIN @members m ON m.pid=rm.member_principal_id
 WHERE NOT EXISTS(SELECT 1 FROM @members e WHERE e.pid=rm.role_principal_id);
 SET @added=@@ROWCOUNT;
 SET @depth+=1;
END;
IF @added>0 THROW 59909,N'ROLE_CLOSURE_DEPTH_HOLD',1;
IF EXISTS(SELECT 1 FROM @members m JOIN sys.database_principals p ON p.principal_id=m.pid
 WHERE p.name IN(N'db_owner',N'db_datareader',N'db_datawriter',N'db_ddladmin',N'db_securityadmin'))
 THROW 59909,N'BROAD_ROLE_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.database_permissions dp JOIN @members m ON m.pid=dp.grantee_principal_id
 WHERE (dp.class=0 OR (dp.class=3 AND dp.major_id=SCHEMA_ID(N'dbo')))
 AND dp.permission_name IN(N'CONTROL',N'ALTER',N'SELECT',N'INSERT',N'UPDATE',N'DELETE')
 AND dp.state IN(N'G',N'W',N'D'))
 THROW 59910,N'BROAD_SCOPE_PERMISSION_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.database_permissions dp JOIN @members m ON m.pid=dp.grantee_principal_id
 JOIN @wanted w ON (dp.class=1 AND dp.major_id=OBJECT_ID(N'dbo.'+QUOTENAME(w.table_name),N'U'))
 WHERE dp.state=N'D' AND (dp.permission_name COLLATE DATABASE_DEFAULT=w.operation COLLATE DATABASE_DEFAULT OR dp.permission_name=N'CONTROL'))
 THROW 59911,N'RELEVANT_DENY_HOLD',1;
-- Direct object-level already-present permissions must match exactly 3; do not remove them.
IF (SELECT COUNT(*) FROM @wanted w
 JOIN sys.database_permissions dp ON dp.class=1
 AND dp.major_id=OBJECT_ID(N'dbo.'+QUOTENAME(w.table_name),N'U')
 AND dp.minor_id=0 AND dp.permission_name COLLATE DATABASE_DEFAULT=w.operation COLLATE DATABASE_DEFAULT
 AND dp.grantee_principal_id=@pid AND dp.state IN(N'G',N'W')
 WHERE w.already_granted=1)<>3
 THROW 59912,N'EXISTING_THREE_GRANTS_HOLD',1;
-- For the 17 missing catalog grants, refuse pre-existing object, column or inherited grants.
IF EXISTS(SELECT 1 FROM @wanted w JOIN sys.database_permissions dp
 ON dp.class=1 AND dp.major_id=OBJECT_ID(N'dbo.'+QUOTENAME(w.table_name),N'U')
 AND dp.permission_name COLLATE DATABASE_DEFAULT=w.operation COLLATE DATABASE_DEFAULT AND dp.grantee_principal_id IN(SELECT pid FROM @members)
 AND dp.state IN(N'G',N'W')
 WHERE w.already_granted=0)
 THROW 59913,N'UNEXPECTED_EXISTING_GRANT_HOLD',1;
BEGIN TRY
 BEGIN TRANSACTION;
 -- Permissions are fixed object-scoped DCL; source code and UAT Seed are untouched.
 GRANT INSERT ON OBJECT::dbo.[CorrectionRequests] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[CorrectionRequests] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT UPDATE ON OBJECT::dbo.[CorrectionRequests] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT UPDATE ON OBJECT::dbo.[MileageCalculations] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT INSERT ON OBJECT::dbo.[MileageGovernanceEvents] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT INSERT ON OBJECT::dbo.[RouteCalculationAttempts] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[RouteCalculationAttempts] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT UPDATE ON OBJECT::dbo.[RouteCalculationAttempts] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[UserFavoriteLocations] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[UserRoles] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT UPDATE ON OBJECT::dbo.[VisitTrips] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT INSERT ON OBJECT::dbo.[VisitTripSnapshots] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[VisitTripSnapshots] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT INSERT ON OBJECT::dbo.[VisitTripSnapshotStops] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[VisitTripSnapshotStops] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT DELETE ON OBJECT::dbo.[VisitTripStops] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT UPDATE ON OBJECT::dbo.[VisitTripStops] TO [api-fieldvisit-pathb-uat_pathb];
 IF (SELECT COUNT(*) FROM @wanted w JOIN sys.database_permissions dp ON dp.class=1
 AND dp.major_id=OBJECT_ID(N'dbo.'+QUOTENAME(w.table_name),N'U')
 AND dp.minor_id=0 AND dp.permission_name COLLATE DATABASE_DEFAULT=w.operation COLLATE DATABASE_DEFAULT
 AND dp.grantee_principal_id=@pid AND dp.state IN(N'G',N'W'))<>20
 THROW 59914,N'TWENTY_GRANT_READBACK_HOLD',1;
 COMMIT TRANSACTION;
 SELECT N'PATHB_P0_CORE_RUNTIME_ACL' AS Evidence,
        N'APPLIED_17_PLUS_EXISTING_3_CATALOG_ONLY' AS State,
        17 AS NewObjectGrants,3 AS PreservedObjectGrants,
        N'EFFECTIVE_API_BUSINESS_TEST_REQUIRED' AS Boundary;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
