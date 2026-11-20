-- Path B P0 Leader approval: EXACT THREE least-privilege catalog permissions.
-- REVIEW ONLY / NONDEPLOY: separate exact SHA-256 Owner approval required for LIVE DCL.
-- Based on single Owner 2026-11-20 LIVE read-only 15-operation ACL result.
-- Preserve all previous GRANTs and LIVE PASS. Do not rerun previous SQL.
-- Only: MileageRateRules SELECT; ApprovalRecords INSERT + ApprovalRecordId column SELECT.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT<>0 OR (2 & @@OPTIONS)=2
 THROW 59950,N'AMBIENT_TRANSACTION_HOLD',1;
IF COALESCE(TRY_CONVERT(int,SERVERPROPERTY(N'EngineEdition')),0)<>5
 OR CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')) COLLATE Latin1_General_100_BIN2<>N'sql-fieldvisit-jpe-pathb-uat'
 OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N'db-fieldvisit-pathb-uat'
 THROW 59951,N'EXACT_PATHB_TARGET_HOLD',1;
IF NOT EXISTS(SELECT 1 FROM sys.dm_exec_connections
 WHERE session_id=@@SPID AND encrypt_option=N'TRUE')
 THROW 59952,N'TLS_HOLD',1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL'),0)<>1
 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION'),0)<>1
 THROW 59953,N'OWNER_AUTHORITY_HOLD',1;
DECLARE @pid int,@rateOid int,@approvalOid int,@idCol int;
SELECT @pid=principal_id FROM sys.database_principals
 WHERE name COLLATE DATABASE_DEFAULT=N'api-fieldvisit-pathb-uat_pathb' COLLATE DATABASE_DEFAULT
 AND type=N'E' AND authentication_type_desc=N'EXTERNAL' AND DATALENGTH(sid)=16;
IF @pid IS NULL OR (SELECT COUNT(*) FROM sys.database_principals WHERE principal_id=@pid)<>1
 THROW 59954,N'EXACT_API_PRINCIPAL_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.schemas WHERE principal_id=@pid)
 OR EXISTS(SELECT 1 FROM sys.objects WHERE principal_id=@pid)
 THROW 59955,N'APP_OWNERSHIP_HOLD',1;
SELECT @rateOid=OBJECT_ID(N'dbo.MileageRateRules',N'U'),
       @approvalOid=OBJECT_ID(N'dbo.ApprovalRecords',N'U');
SELECT @idCol=column_id FROM sys.columns
 WHERE object_id=@approvalOid AND name=N'ApprovalRecordId' AND is_identity=1;
IF @rateOid IS NULL OR @approvalOid IS NULL OR @idCol IS NULL
 THROW 59956,N'EXACT_SCHEMA_SHAPE_HOLD',1;
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
IF @added>0 THROW 59957,N'ROLE_DEPTH_HOLD',1;
IF EXISTS(SELECT 1 FROM @members m JOIN sys.database_principals p ON p.principal_id=m.pid
 WHERE p.name IN(N'db_owner',N'db_datareader',N'db_datawriter',N'db_securityadmin',N'db_ddladmin'))
 THROW 59958,N'BROAD_ROLE_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.database_permissions dp JOIN @members m ON m.pid=dp.grantee_principal_id
 WHERE (dp.class=0 OR (dp.class=3 AND dp.major_id=SCHEMA_ID(N'dbo')))
 AND dp.permission_name IN(N'CONTROL',N'ALTER',N'SELECT',N'INSERT',N'UPDATE',N'DELETE')
 AND dp.state IN(N'G',N'W',N'D'))
 THROW 59959,N'BROAD_PERMISSION_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.database_permissions dp JOIN @members m ON m.pid=dp.grantee_principal_id
 WHERE dp.class=1 AND dp.major_id IN(@rateOid,@approvalOid)
 AND dp.state=N'D'
 AND (dp.permission_name IN(N'CONTROL',N'ALTER',N'SELECT',N'INSERT',N'UPDATE',N'DELETE')))
 THROW 59960,N'RELEVANT_DENY_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.database_permissions dp JOIN @members m ON m.pid=dp.grantee_principal_id
 WHERE dp.class=1 AND
   ((dp.major_id=@rateOid AND dp.permission_name IN(N'SELECT',N'CONTROL'))
   OR (dp.major_id=@approvalOid AND dp.permission_name IN(N'INSERT',N'SELECT',N'CONTROL')))
 AND dp.state IN(N'G',N'W'))
 THROW 59961,N'UNEXPECTED_EXISTING_GRANT_HOLD',1;
BEGIN TRY
 BEGIN TRANSACTION;
 IF EXISTS(SELECT 1 FROM sys.database_permissions dp JOIN @members m ON m.pid=dp.grantee_principal_id
  WHERE dp.class=1 AND
    ((dp.major_id=@rateOid AND dp.permission_name IN(N'SELECT',N'CONTROL'))
     OR (dp.major_id=@approvalOid AND dp.permission_name IN(N'INSERT',N'SELECT',N'CONTROL')))
  AND dp.state IN(N'G',N'W'))
  THROW 59962,N'CONCURRENT_PERMISSION_CHANGE_HOLD',1;
 GRANT SELECT ON OBJECT::dbo.[MileageRateRules] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT INSERT ON OBJECT::dbo.[ApprovalRecords] TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[ApprovalRecords] ([ApprovalRecordId])
 TO [api-fieldvisit-pathb-uat_pathb];
 IF (SELECT COUNT(*) FROM sys.database_permissions
  WHERE grantee_principal_id=@pid AND class=1 AND major_id=@rateOid
  AND minor_id=0 AND permission_name=N'SELECT' AND state IN(N'G',N'W'))<>1
  THROW 59963,N'RATE_SELECT_READBACK_HOLD',1;
 IF (SELECT COUNT(*) FROM sys.database_permissions
  WHERE grantee_principal_id=@pid AND class=1 AND major_id=@approvalOid
  AND minor_id=0 AND permission_name=N'INSERT' AND state IN(N'G',N'W'))<>1
  THROW 59964,N'APPROVAL_INSERT_READBACK_HOLD',1;
 IF (SELECT COUNT(*) FROM sys.database_permissions
  WHERE grantee_principal_id=@pid AND class=1 AND major_id=@approvalOid
  AND minor_id=@idCol AND permission_name=N'SELECT' AND state IN(N'G',N'W'))<>1
  THROW 59965,N'APPROVAL_ID_SELECT_READBACK_HOLD',1;
 IF EXISTS(SELECT 1 FROM sys.database_permissions
  WHERE grantee_principal_id=@pid AND class=1 AND major_id=@approvalOid
  AND minor_id=0 AND permission_name=N'SELECT' AND state IN(N'G',N'W'))
  THROW 59966,N'UNEXPECTED_WHOLE_APPROVAL_SELECT_HOLD',1;
 COMMIT TRANSACTION;
 SELECT N'PATHB_P0_LEADER_MINIMAL_ACL' AS Evidence,
  N'APPLIED_TWO_TABLE_AND_ONE_COLUMN_GRANTS' AS State,
  3 AS NewPermissions,N'BUSINESS_APPROVAL_RUNTIME_REVALIDATION_REQUIRED' AS Boundary;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
