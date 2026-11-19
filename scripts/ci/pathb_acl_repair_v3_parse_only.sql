-- FIELD VISIT PATH B - ONE-WINDOW LOGIN SELECT REPAIR (NEW REVIEW VERSION)
-- NOT AUTHORIZED FOR EXECUTION until the Owner approves this exact NEW SHA-256.
-- Exact target: sql-fieldvisit-jpe-pathb-uat / db-fieldvisit-pathb-uat.
-- Outcome after previous SQL syntax error remains unverified; this is fail-closed.
-- This script grants SELECT on eight exact dbo tables ONLY, never database/schema wide.
-- Do not execute on the old once-only authorization. Do not retry on any error/UNKNOWN.
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Disallow client-created transactions; only our own transaction is permitted.
IF @@TRANCOUNT <> 0
    THROW 59201, 'AMBIENT_TRANSACTION_HOLD', 1;
IF (2 & @@OPTIONS) = 2
    THROW 59201, 'IMPLICIT_TRANSACTION_MODE_HOLD', 1;

-- One target, Azure SQL Database engine (not Managed Instance or on-premises).
IF CONVERT(int, SERVERPROPERTY('EngineEdition')) <> 5
    THROW 59202, 'ENGINE_HOLD', 1;
IF DB_NAME() <> N'db-fieldvisit-pathb-uat'
    THROW 59202, 'DATABASE_HOLD', 1;
IF CONVERT(nvarchar(256), SERVERPROPERTY('ServerName')) <> N'sql-fieldvisit-jpe-pathb-uat'
    THROW 59202, 'SERVER_HOLD', 1;
IF NOT EXISTS (SELECT 1 FROM sys.dm_exec_connections
               WHERE session_id = @@SPID AND encrypt_option = N'TRUE')
    THROW 59203, 'ENCRYPTION_HOLD', 1;

-- Metadata completeness / rights precondition.
IF ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION'),0) <> 1
    THROW 59204, 'METADATA_HOLD', 1;
IF ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER ANY USER'),0) <> 1
    THROW 59204, 'USER_METADATA_HOLD', 1;
IF ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL'),0) <> 1
    THROW 59204, 'GRANT_AUTHORITY_HOLD', 1;

-- Existing Path B API system-assigned Managed Identity, NOT a new database user.
DECLARE @principalId int;
DECLARE @expectedOid uniqueidentifier = '00000000-0000-0000-0000-000000000000';
DECLARE @expectedClientId uniqueidentifier = '11111111-1111-1111-1111-111111111111';
IF (SELECT COUNT(*) FROM sys.database_principals
    WHERE name=N'api-fieldvisit-pathb-uat_pathb'
      AND type=N'E' AND authentication_type_desc=N'EXTERNAL') <> 1
    THROW 59205, 'PRINCIPAL_HOLD', 1;
SELECT @principalId = principal_id
FROM sys.database_principals
WHERE name=N'api-fieldvisit-pathb-uat_pathb'
  AND type=N'E' AND authentication_type_desc=N'EXTERNAL';
IF NOT EXISTS (
    SELECT 1 FROM sys.database_principals
    WHERE principal_id=@principalId AND DATALENGTH(sid)=16
      AND CAST(sid AS uniqueidentifier) IN (@expectedOid, @expectedClientId))
    THROW 59206, 'MI_VERIFIED_OBJECT_OR_CLIENT_ID_SID_HOLD', 1;
IF (SELECT COUNT(*) FROM sys.database_principals
    WHERE DATALENGTH(sid)=16
      AND CAST(sid AS uniqueidentifier) =
          (SELECT CAST(sid AS uniqueidentifier)
           FROM sys.database_principals WHERE principal_id=@principalId)) <> 1
    THROW 59206, 'MI_SID_DUPLICATE_HOLD', 1;

DECLARE @objects TABLE (ObjectId int PRIMARY KEY);
INSERT INTO @objects (ObjectId)
SELECT object_id FROM sys.tables
WHERE schema_id=SCHEMA_ID(N'dbo')
  AND name IN (N'Organizations',N'Roles',N'Teams',N'UserEmploymentPeriods',
               N'UserIdentityProfiles',N'UserRoleAssignments',N'Users',N'UserTeamAssignments');
IF (SELECT COUNT(*) FROM @objects) <> 8
    THROW 59207, 'EXACT_EIGHT_TABLES_HOLD', 1;

-- Exclude broad data/DDL/security roles; include implicit public and nested roles.
DECLARE @roleIds TABLE (PrincipalId int PRIMARY KEY);
INSERT @roleIds (PrincipalId) VALUES (@principalId);
INSERT @roleIds (PrincipalId)
SELECT principal_id FROM sys.database_principals WHERE name=N'public';
DECLARE @n int = 1;
DECLARE @depth int = 0;
WHILE @n > 0 AND @depth < 16
BEGIN
    INSERT @roleIds (PrincipalId)
    SELECT DISTINCT rm.role_principal_id
    FROM sys.database_role_members rm
    JOIN @roleIds r ON r.PrincipalId=rm.member_principal_id
    WHERE NOT EXISTS (SELECT 1 FROM @roleIds x WHERE x.PrincipalId=rm.role_principal_id);
    SET @n=@@ROWCOUNT;
    SET @depth=@depth+1;
END;
IF @n > 0
    THROW 59208, 'ROLE_DEPTH_HOLD', 1;
IF EXISTS (SELECT 1 FROM @roleIds r
           JOIN sys.database_principals p ON p.principal_id=r.PrincipalId
           WHERE p.name IN (N'db_owner',N'db_datareader',N'db_datawriter',
                            N'db_ddladmin',N'db_securityadmin'))
    THROW 59208, 'BROAD_ROLE_HOLD', 1;
IF EXISTS (SELECT 1 FROM sys.schemas WHERE principal_id=@principalId)
    THROW 59209, 'SCHEMA_OWNERSHIP_HOLD', 1;
IF EXISTS (SELECT 1 FROM sys.objects WHERE principal_id=@principalId)
    THROW 59209, 'OBJECT_OWNERSHIP_HOLD', 1;
IF EXISTS (
    SELECT 1 FROM sys.database_permissions p
    JOIN @roleIds r ON r.PrincipalId=p.grantee_principal_id
    WHERE p.state='D' AND p.permission_name IN ('SELECT','CONTROL')
      AND (p.class=0
           OR (p.class=3 AND p.major_id=SCHEMA_ID(N'dbo'))
           OR (p.class=1 AND EXISTS(SELECT 1 FROM @objects o WHERE o.ObjectId=p.major_id))))
    THROW 59210, 'DENY_HOLD', 1;

-- Before any write, determine whether the earlier failed execution changed grants.
DECLARE @granted int;
SELECT @granted=COUNT(DISTINCT p.major_id)
FROM sys.database_permissions p
JOIN @objects o ON o.ObjectId=p.major_id
WHERE p.class=1 AND p.minor_id=0 AND p.grantee_principal_id=@principalId
  AND p.permission_name='SELECT' AND p.state IN ('G','W');
IF @granted=8
BEGIN
    SELECT N'PATHB_LOGIN_SELECT_REPAIR' AS Evidence,
           8 AS ObjectsWithExplicitSelectGrant,
           N'ALREADY_EIGHT_NO_WRITE_CATALOG_ONLY' AS State;
    RETURN;
END;
IF @granted<>0
    THROW 59211, 'PARTIAL_OR_AMBIGUOUS_GRANTS_HOLD_NO_WRITE', 1;

-- Only the zero-grant state can reach this bounded atomic permission repair.
BEGIN TRY
    BEGIN TRANSACTION;
    SELECT @granted=COUNT(DISTINCT p.major_id)
    FROM sys.database_permissions p
    JOIN @objects o ON o.ObjectId=p.major_id
    WHERE p.class=1 AND p.minor_id=0 AND p.grantee_principal_id=@principalId
      AND p.permission_name='SELECT' AND p.state IN ('G','W');
    IF @granted<>0
        THROW 59212, 'CONCURRENT_GRANT_CHANGE_HOLD', 1;
    GRANT SELECT ON OBJECT::dbo.Organizations TO [api-fieldvisit-pathb-uat_pathb];
    GRANT SELECT ON OBJECT::dbo.Roles TO [api-fieldvisit-pathb-uat_pathb];
    GRANT SELECT ON OBJECT::dbo.Teams TO [api-fieldvisit-pathb-uat_pathb];
    GRANT SELECT ON OBJECT::dbo.UserEmploymentPeriods TO [api-fieldvisit-pathb-uat_pathb];
    GRANT SELECT ON OBJECT::dbo.UserIdentityProfiles TO [api-fieldvisit-pathb-uat_pathb];
    GRANT SELECT ON OBJECT::dbo.UserRoleAssignments TO [api-fieldvisit-pathb-uat_pathb];
    GRANT SELECT ON OBJECT::dbo.Users TO [api-fieldvisit-pathb-uat_pathb];
    GRANT SELECT ON OBJECT::dbo.UserTeamAssignments TO [api-fieldvisit-pathb-uat_pathb];
    SELECT @granted=COUNT(DISTINCT p.major_id)
    FROM sys.database_permissions p
    JOIN @objects o ON o.ObjectId=p.major_id
    WHERE p.class=1 AND p.minor_id=0 AND p.grantee_principal_id=@principalId
      AND p.permission_name='SELECT' AND p.state IN ('G','W');
    IF @granted<>8
        THROW 59213, 'EIGHT_TABLE_READBACK_HOLD_ROLLBACK', 1;
    COMMIT TRANSACTION;
    SELECT N'PATHB_LOGIN_SELECT_REPAIR' AS Evidence,
           8 AS ObjectsWithExplicitSelectGrant,
           N'APPLIED_CATALOG_ONLY_NOT_RUNTIME_LOGIN_PROOF' AS State;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;