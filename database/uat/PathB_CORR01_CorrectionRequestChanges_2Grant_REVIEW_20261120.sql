-- Field Visit Path B CORR-01/02 P0 - two exact object grants, review candidate only.
-- NONDEPLOY / NOT AUTHORIZED FOR LIVE SQL. New exact-SHA Owner approval required.
-- Source of scope: observed partial pending correction and missing grants in prior LIVE ACL list.
-- Does not read or modify any business rows; does not fix an existing partial request.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT<>0 OR (2 & @@OPTIONS)=2
 THROW 59870,N'AMBIENT_TRANSACTION_HOLD',1;
IF COALESCE(TRY_CONVERT(int,SERVERPROPERTY(N'EngineEdition')),0)<>5
 OR CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')) COLLATE Latin1_General_100_BIN2<>N'sql-fieldvisit-jpe-pathb-uat'
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N'db-fieldvisit-pathb-uat'
 THROW 59871,N'EXACT_TARGET_HOLD',1;
IF NOT EXISTS(SELECT 1 FROM sys.dm_exec_connections
 WHERE session_id=@@SPID AND encrypt_option=N'TRUE')
 THROW 59872,N'TLS_HOLD',1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL'),0)<>1
 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION'),0)<>1
 THROW 59873,N'OWNER_AUTHORITY_HOLD',1;
DECLARE @pid int,@changeOid int,@headerOid int,@idCol int;
SELECT @pid=principal_id FROM sys.database_principals
 WHERE name COLLATE DATABASE_DEFAULT=N'api-fieldvisit-pathb-uat_pathb' COLLATE DATABASE_DEFAULT
 AND type=N'E' AND authentication_type_desc=N'EXTERNAL' AND DATALENGTH(sid)=16;
IF @pid IS NULL OR (SELECT COUNT(*) FROM sys.database_principals WHERE principal_id=@pid)<>1
 THROW 59874,N'EXACT_APP_PRINCIPAL_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.schemas WHERE principal_id=@pid)
 OR EXISTS(SELECT 1 FROM sys.objects WHERE principal_id=@pid)
 THROW 59875,N'APP_OWNERSHIP_HOLD',1;
SELECT @changeOid=OBJECT_ID(N'dbo.CorrectionRequestChanges',N'U'),
       @headerOid=OBJECT_ID(N'dbo.CorrectionRequests',N'U');
SELECT @idCol=column_id FROM sys.columns
 WHERE object_id=@changeOid AND name=N'CorrectionRequestChangeId' AND is_identity=1;
IF @changeOid IS NULL OR @headerOid IS NULL OR @idCol IS NULL
 OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=@changeOid AND name=N'CorrectionRequestId')
 OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=@changeOid AND name=N'FieldName')
 THROW 59876,N'EXACT_SCHEMA_SHAPE_HOLD',1;
DECLARE @members TABLE(pid int PRIMARY KEY);
INSERT @members VALUES(@pid);
INSERT @members SELECT principal_id FROM sys.database_principals
 WHERE name=N'public' AND principal_id<>@pid;
DECLARE @depth int=0,@added int=1;
WHILE @added>0 AND @depth<16
BEGIN
 INSERT @members(pid)
 SELECT DISTINCT m.role_principal_id FROM sys.database_role_members AS m
 JOIN @members AS p ON p.pid=m.member_principal_id
 WHERE NOT EXISTS(SELECT 1 FROM @members AS e WHERE e.pid=m.role_principal_id);
 SET @added=@@ROWCOUNT;
 SET @depth+=1;
END;
IF @added>0 THROW 59877,N'ROLE_DEPTH_HOLD',1;
IF EXISTS(SELECT 1 FROM @members m JOIN sys.database_principals p ON p.principal_id=m.pid
 WHERE p.name IN(N'db_owner',N'db_datareader',N'db_datawriter',N'db_securityadmin',N'db_ddladmin'))
 THROW 59878,N'BROAD_ROLE_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.database_permissions dp JOIN @members m ON m.pid=dp.grantee_principal_id
 WHERE (dp.class=0 OR (dp.class=3 AND dp.major_id=SCHEMA_ID(N'dbo')))
 AND dp.permission_name IN(N'CONTROL',N'ALTER',N'SELECT',N'INSERT',N'UPDATE',N'DELETE')
 AND dp.state IN(N'G',N'W',N'D'))
 THROW 59879,N'BROAD_PERMISSION_HOLD',1;
IF (SELECT COUNT(DISTINCT dp.permission_name) FROM sys.database_permissions dp
 WHERE dp.grantee_principal_id=@pid AND dp.class=1 AND dp.major_id=@headerOid
 AND dp.minor_id=0 AND dp.permission_name IN(N'SELECT',N'INSERT',N'UPDATE')
 AND dp.state IN(N'G',N'W'))<>3
 THROW 59880,N'HEADER_GRANTS_NOT_ESTABLISHED_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.database_permissions dp JOIN @members m ON m.pid=dp.grantee_principal_id
 WHERE dp.class=1 AND dp.major_id=@changeOid
 AND dp.permission_name IN(N'CONTROL',N'ALTER',N'SELECT',N'INSERT',N'UPDATE',N'DELETE')
 AND dp.state IN(N'G',N'W',N'D'))
 THROW 59881,N'EXISTING_CHANGE_PERMISSION_OR_DENY_HOLD',1;
BEGIN TRY
 BEGIN TRANSACTION;
 IF EXISTS(SELECT 1 FROM sys.database_permissions dp
  WHERE dp.grantee_principal_id=@pid AND dp.class=1 AND dp.major_id=@changeOid
  AND dp.permission_name IN(N'SELECT',N'INSERT') AND dp.state IN(N'G',N'W',N'D'))
  THROW 59882,N'CONCURRENT_PERMISSION_CHANGE_HOLD',1;
 GRANT INSERT ON OBJECT::dbo.[CorrectionRequestChanges] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[CorrectionRequestChanges] TO [api-fieldvisit-pathb-uat_pathb];
 IF (SELECT COUNT(*) FROM sys.database_permissions dp
  WHERE dp.grantee_principal_id=@pid AND dp.class=1 AND dp.major_id=@changeOid
  AND dp.minor_id=0 AND dp.permission_name IN(N'INSERT',N'SELECT')
  AND dp.state IN(N'G',N'W'))<>2
  THROW 59883,N'TWO_GRANT_READBACK_HOLD',1;
 COMMIT TRANSACTION;
 SELECT N'PATHB_CORRECTION_CHANGES_P0_ACL' AS Evidence,
 N'APPLIED_EXACTLY_TWO_OBJECT_GRANTS' AS State,
 N'dbo.CorrectionRequestChanges' AS TargetTable,
 N'INSERT,SELECT' AS NewPermissions,
 N'NO_BUSINESS_DATA_MODIFIED_PENDING_ROW_STILL_REQUIRES_INTEGRITY_REVIEW' AS Boundary;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
