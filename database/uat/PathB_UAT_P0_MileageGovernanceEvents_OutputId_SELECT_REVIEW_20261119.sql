-- FIELD VISIT PATH B P0 -- mileage governance EF OUTPUT identity column.
-- NONDEPLOY REVIEW ONLY. NEW EXACT-SHA OWNER AUTHORIZATION REQUIRED FOR AZURE.
-- No business data reads/writes. One object-column SELECT grant to API MI only.
-- Preserve the successful 17 core grants, all prior VIS01, Seed and reservation rights.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT<>0 OR (2 & @@OPTIONS)=2
 THROW 59930,N'AMBIENT_TRANSACTION_HOLD',1;
IF COALESCE(TRY_CONVERT(int,SERVERPROPERTY(N'EngineEdition')),0)<>5
 OR CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')) COLLATE Latin1_General_100_BIN2<>N'sql-fieldvisit-jpe-pathb-uat'
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N'db-fieldvisit-pathb-uat'
 THROW 59931,N'EXACT_PATHB_TARGET_HOLD',1;
IF NOT EXISTS(SELECT 1 FROM sys.dm_exec_connections
 WHERE session_id=@@SPID AND encrypt_option=N'TRUE')
 THROW 59932,N'TLS_HOLD',1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL'),0)<>1
 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION'),0)<>1
 THROW 59933,N'OWNER_AUTHORITY_HOLD',1;
DECLARE @pid int, @oid int, @cid int;
SELECT @pid=principal_id FROM sys.database_principals
 WHERE name COLLATE DATABASE_DEFAULT=N'api-fieldvisit-pathb-uat_pathb' COLLATE DATABASE_DEFAULT
 AND type=N'E' AND authentication_type_desc=N'EXTERNAL' AND DATALENGTH(sid)=16;
IF @pid IS NULL OR (SELECT COUNT(*) FROM sys.database_principals WHERE principal_id=@pid)<>1
 THROW 59934,N'EXACT_API_PRINCIPAL_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.schemas WHERE principal_id=@pid)
 OR EXISTS(SELECT 1 FROM sys.objects WHERE principal_id=@pid)
 THROW 59935,N'API_OWNERSHIP_HOLD',1;
SELECT @oid=t.object_id,@cid=c.column_id
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
WHERE t.schema_id=SCHEMA_ID(N'dbo')
 AND t.name COLLATE DATABASE_DEFAULT=N'MileageGovernanceEvents' COLLATE DATABASE_DEFAULT
 AND c.name COLLATE DATABASE_DEFAULT=N'MileageGovernanceEventId' COLLATE DATABASE_DEFAULT
 AND c.is_identity=1;
IF @oid IS NULL OR @cid IS NULL
 THROW 59936,N'EXACT_IDENTITY_COLUMN_HOLD',1;
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
IF @added>0 THROW 59937,N'ROLE_DEPTH_HOLD',1;
IF EXISTS(SELECT 1 FROM @members m JOIN sys.database_principals p ON p.principal_id=m.pid
 WHERE p.name IN(N'db_owner',N'db_datareader',N'db_datawriter',N'db_securityadmin',N'db_ddladmin'))
 THROW 59937,N'BROAD_ROLE_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.database_permissions dp
 JOIN @members m ON m.pid=dp.grantee_principal_id
 WHERE (dp.class=0 OR (dp.class=3 AND dp.major_id=SCHEMA_ID(N'dbo')))
 AND dp.permission_name IN(N'CONTROL',N'ALTER',N'SELECT',N'INSERT',N'UPDATE',N'DELETE')
 AND dp.state IN(N'G',N'W',N'D'))
 THROW 59938,N'BROAD_PERMISSION_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.database_permissions dp
 JOIN @members m ON m.pid=dp.grantee_principal_id
 WHERE dp.class=1 AND dp.major_id=@oid
 AND dp.permission_name IN(N'CONTROL',N'SELECT')
 AND dp.state=N'D')
 THROW 59939,N'RELEVANT_DENY_HOLD',1;
IF (SELECT COUNT(*) FROM sys.database_permissions dp
 WHERE dp.grantee_principal_id=@pid AND dp.class=1
 AND dp.major_id=@oid AND dp.minor_id=0 AND dp.permission_name=N'INSERT' AND dp.state IN(N'G',N'W'))<>1
 THROW 59940,N'PREVIOUS_INSERT_GRANT_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.database_permissions dp JOIN @members m ON m.pid=dp.grantee_principal_id
 WHERE dp.class=1 AND dp.major_id=@oid AND dp.permission_name=N'SELECT' AND dp.state IN(N'G',N'W'))
 THROW 59941,N'EXISTING_SELECT_HOLD',1;
BEGIN TRY
 BEGIN TRANSACTION;
 IF EXISTS(SELECT 1 FROM sys.database_permissions dp JOIN @members m ON m.pid=dp.grantee_principal_id
  WHERE dp.class=1 AND dp.major_id=@oid AND dp.permission_name=N'SELECT' AND dp.state IN(N'G',N'W'))
  THROW 59942,N'CONCURRENT_SELECT_HOLD',1;
 GRANT SELECT ON OBJECT::dbo.[MileageGovernanceEvents] ([MileageGovernanceEventId])
  TO [api-fieldvisit-pathb-uat_pathb];
 IF (SELECT COUNT(*) FROM sys.database_permissions dp
  WHERE dp.grantee_principal_id=@pid AND dp.class=1
  AND dp.major_id=@oid AND dp.minor_id=@cid AND dp.permission_name=N'SELECT'
  AND dp.state IN(N'G',N'W'))<>1
  THROW 59943,N'COLUMN_GRANT_READBACK_HOLD',1;
 IF EXISTS(SELECT 1 FROM sys.database_permissions dp
  WHERE dp.grantee_principal_id=@pid AND dp.class=1
  AND dp.major_id=@oid AND dp.minor_id=0 AND dp.permission_name=N'SELECT'
  AND dp.state IN(N'G',N'W'))
  THROW 59944,N'UNEXPECTED_TABLE_SELECT_HOLD',1;
 COMMIT TRANSACTION;
 SELECT N'PATHB_P0_MILEAGE_GOVERNANCE_OUTPUT_ID_ACL' AS Evidence,
 N'APPLIED_ONE_COLUMN_SELECT_CATALOG_ONLY' AS State,
 N'MileageGovernanceEvents' AS ObjectName,
 N'MileageGovernanceEventId' AS ColumnName,
 1 AS NewColumnSelectGrants,
 N'GOOGLE_ROUTE_AND_BUSINESS_UAT_RETEST_REQUIRED' AS Boundary;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
