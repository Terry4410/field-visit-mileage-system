SET NOCOUNT ON;
IF @@TRANCOUNT <> 0 OR (2 & @@OPTIONS) = 2
    THROW 59001, N'AMBIENT_TRANSACTION_HOLD', 1;
SET XACT_ABORT ON;
IF COALESCE(TRY_CONVERT(int, SERVERPROPERTY(N'EngineEdition')), 0) <> 5
 OR COALESCE(CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')), N'')
    COLLATE Latin1_General_100_BIN2 <> N'sql-fieldvisit-jpe-pathb-uat'
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> N'db-fieldvisit-pathb-uat'
    THROW 59002, N'EXACT_PATHB_TARGET_HOLD', 1;
IF NOT EXISTS (SELECT 1 FROM sys.dm_exec_connections
               WHERE session_id=@@SPID AND encrypt_option=N'TRUE')
    THROW 59003, N'TLS_NOT_OBSERVED_HOLD', 1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION'),0)<>1
 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER ANY USER'),0)<>1
 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL'),0)<>1
    THROW 59004, N'OWNER_METADATA_OR_GRANT_AUTHORITY_HOLD', 1;
DECLARE @expectedName sysname=N'api-fieldvisit-pathb-uat_pathb';
DECLARE @pid int;
IF (SELECT COUNT(*) FROM sys.database_principals
    WHERE name=@expectedName AND type=N'E' AND authentication_type_desc=N'EXTERNAL')<>1
    THROW 59005, N'EXTERNAL_PRINCIPAL_ALIAS_HOLD', 1;
SELECT @pid=principal_id FROM sys.database_principals
 WHERE name=@expectedName AND type=N'E' AND authentication_type_desc=N'EXTERNAL';
DECLARE @expectedAppObjectId uniqueidentifier =
    CONVERT(uniqueidentifier,N'00000000-0000-0000-0000-000000000000');
IF EXISTS (
    SELECT 1
    FROM sys.database_principals p
    WHERE p.principal_id = @pid
      AND (
        DATALENGTH(p.sid) <> 16
        OR CAST(p.sid AS uniqueidentifier) <> @expectedAppObjectId
      )
)
    THROW 59012, N'PATHB_RUNTIME_MI_OBJECT_ID_SID_MISMATCH_HOLD', 1;
IF (SELECT COUNT(*) FROM sys.database_principals p
    WHERE DATALENGTH(p.sid)=16
      AND CAST(p.sid AS uniqueidentifier)=@expectedAppObjectId)<>1
    THROW 59013, N'PATHB_RUNTIME_MI_SID_DUPLICATE_OR_HIDDEN_HOLD', 1;
DECLARE @targets TABLE (TableName sysname NOT NULL PRIMARY KEY, ObjectId int NULL);
INSERT @targets(TableName,ObjectId) VALUES
 (N'Organizations',OBJECT_ID(N'dbo.Organizations',N'U')),
 (N'Roles',OBJECT_ID(N'dbo.Roles',N'U')),
 (N'Teams',OBJECT_ID(N'dbo.Teams',N'U')),
 (N'UserEmploymentPeriods',OBJECT_ID(N'dbo.UserEmploymentPeriods',N'U')),
 (N'UserIdentityProfiles',OBJECT_ID(N'dbo.UserIdentityProfiles',N'U')),
 (N'UserRoleAssignments',OBJECT_ID(N'dbo.UserRoleAssignments',N'U')),
 (N'Users',OBJECT_ID(N'dbo.Users',N'U')),
 (N'UserTeamAssignments',OBJECT_ID(N'dbo.UserTeamAssignments',N'U'));
IF (SELECT COUNT(*) FROM @targets)<>8 OR EXISTS(SELECT 1 FROM @targets WHERE ObjectId IS NULL)
    THROW 59006, N'LOGIN_TARGET_TABLE_SET_HOLD', 1;
DECLARE @members TABLE (PrincipalId int PRIMARY KEY);
INSERT @members(PrincipalId) VALUES(@pid);
INSERT @members(PrincipalId)
 SELECT principal_id FROM sys.database_principals WHERE name=N'public'
 AND principal_id NOT IN(SELECT PrincipalId FROM @members);
DECLARE @step int=0, @added int=1;
WHILE @added>0 AND @step<16
BEGIN
    INSERT @members(PrincipalId)
    SELECT DISTINCT rm.role_principal_id
    FROM sys.database_role_members rm
    JOIN @members m ON m.PrincipalId=rm.member_principal_id
    WHERE NOT EXISTS(SELECT 1 FROM @members existing WHERE existing.PrincipalId=rm.role_principal_id);
    SET @added=@@ROWCOUNT;
    SET @step=@step+1;
END;
IF @added>0 THROW 59007, N'ROLE_CLOSURE_DEPTH_HOLD', 1;
IF EXISTS (SELECT 1 FROM @members m JOIN sys.database_principals p ON p.principal_id=m.PrincipalId
           WHERE p.name IN(N'db_owner',N'db_datareader',N'db_datawriter',N'db_ddladmin',N'db_securityadmin'))
    THROW 59008, N'BROAD_RUNTIME_ROLE_HOLD', 1;
IF EXISTS (SELECT 1 FROM sys.schemas s WHERE s.principal_id=@pid)
 OR EXISTS (SELECT 1 FROM sys.objects o WHERE o.principal_id=@pid)
    THROW 59009, N'RUNTIME_OBJECT_OWNERSHIP_HOLD', 1;
IF EXISTS (
 SELECT 1 FROM sys.database_permissions p
 JOIN @members m ON m.PrincipalId=p.grantee_principal_id
 WHERE p.state=N'D' AND p.permission_name IN(N'SELECT',N'CONTROL')
   AND (p.class=0
        OR (p.class=3 AND p.major_id=SCHEMA_ID(N'dbo'))
        OR (p.class=1 AND EXISTS(SELECT 1 FROM @targets t WHERE t.ObjectId=p.major_id))))
    THROW 59010, N'DENY_OR_COLUMN_DENY_HOLD', 1;
IF EXISTS (
    SELECT 1 FROM sys.database_permissions p
    JOIN @targets t ON t.ObjectId=p.major_id
    WHERE p.class=1 AND p.minor_id=0 AND p.grantee_principal_id=@pid
      AND p.permission_name=N'SELECT' AND p.state IN (N'G',N'W')
)
    THROW 59014, N'EXISTING_OR_PARTIAL_LOGIN_SELECT_GRANTS_HOLD_NO_RETRY', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (
        SELECT 1 FROM sys.database_permissions p
        JOIN @targets t ON t.ObjectId=p.major_id
        WHERE p.class=1 AND p.minor_id=0 AND p.grantee_principal_id=@pid
          AND p.permission_name=N'SELECT' AND p.state IN (N'G',N'W')
    )
        THROW 59014, N'CONCURRENT_LOGIN_GRANT_CHANGE_HOLD_NO_RETRY', 1;
    GRANT SELECT ON OBJECT::dbo.[Organizations] TO [api-fieldvisit-pathb-uat_pathb];
    GRANT SELECT ON OBJECT::dbo.[Roles] TO [api-fieldvisit-pathb-uat_pathb];
    GRANT SELECT ON OBJECT::dbo.[Teams] TO [api-fieldvisit-pathb-uat_pathb];
    GRANT SELECT ON OBJECT::dbo.[UserEmploymentPeriods] TO [api-fieldvisit-pathb-uat_pathb];
    GRANT SELECT ON OBJECT::dbo.[UserIdentityProfiles] TO [api-fieldvisit-pathb-uat_pathb];
    GRANT SELECT ON OBJECT::dbo.[UserRoleAssignments] TO [api-fieldvisit-pathb-uat_pathb];
    GRANT SELECT ON OBJECT::dbo.[Users] TO [api-fieldvisit-pathb-uat_pathb];
    GRANT SELECT ON OBJECT::dbo.[UserTeamAssignments] TO [api-fieldvisit-pathb-uat_pathb];
    IF (SELECT COUNT(DISTINCT p.major_id)
        FROM sys.database_permissions p JOIN @targets t ON t.ObjectId=p.major_id
        WHERE p.class=1 AND p.minor_id=0 AND p.grantee_principal_id=@pid
          AND p.permission_name=N'SELECT' AND p.state IN (N'G',N'W'))<>8
        THROW 59011, N'EXACT_EIGHT_OBJECT_GRANTS_READBACK_HOLD', 1;
    COMMIT TRANSACTION;
    SELECT N'PATHB_LOGIN_EIGHT_SELECT_GRANTS_APPLIED' AS Evidence,
           8 AS ExpectedObjects,
           N'CATALOG_PASS_NOT_RUNTIME_LOGIN_PROOF' AS State;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;