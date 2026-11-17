-- CAUTION: SQL DDL candidate. NEVER run under existing G1/Recovery authorization.
-- A separately reviewed, exact-SHA, one-use Owner SQL execution contract MUST
-- privately set ALL context fields on this SAME session after verifying Azure ARM
-- system-assigned App MI objectId/clientId, directory groups, installed schema,
-- trusted Owner SQL connection and explicit Owner decision. No retry on UNKNOWN.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF TRY_CONVERT(nvarchar(128), SESSION_CONTEXT(N'FieldVisit.PathB.OwnerDecision'))
       <> N'EXPLICIT_NEW_PATHB_MI_CONNECT_ONLY_ONCE_V1'
   OR TRY_CONVERT(nvarchar(128), SESSION_CONTEXT(N'FieldVisit.PathB.GroupAclVerified'))
       <> N'INDEPENDENTLY_VERIFIED_NO_EFFECTIVE_GROUP_LOGIN'
   OR TRY_CONVERT(nvarchar(128), SESSION_CONTEXT(N'FieldVisit.PathB.OwnerSqlChannelVerified'))
       <> N'EXACT_OWNER_PRIVATE_TLS_VERIFIED'
    THROW 57100, N'PATHB_MI_NEW_AUTHORIZATION_REQUIRED', 1;
IF COALESCE(CONVERT(int, SERVERPROPERTY(N'EngineEdition')), 0) <> 5
   OR COALESCE(CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')), N'')
        COLLATE Latin1_General_100_BIN2 <> N'sql-fieldvisit-jpe-pathb-uat'
   OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> N'db-fieldvisit-pathb-uat'
    THROW 57101, N'PATHB_MI_GRANT_EXACT_TARGET_HOLD', 1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'VIEW DEFINITION'), 0) <> 1
    THROW 57102, N'PATHB_MI_GRANT_CATALOG_VISIBILITY_HOLD', 1;
DECLARE @objectId uniqueidentifier = TRY_CONVERT(uniqueidentifier,
    SESSION_CONTEXT(N'FieldVisit.PathB.ApiMiObjectId'));
DECLARE @clientId uniqueidentifier = TRY_CONVERT(uniqueidentifier,
    SESSION_CONTEXT(N'FieldVisit.PathB.ApiMiClientId'));
IF @objectId IS NULL OR @clientId IS NULL
   OR @objectId = '00000000-0000-0000-0000-000000000000'
   OR @clientId = '00000000-0000-0000-0000-000000000000'
    THROW 57103, N'PATHB_MI_GRANT_PRIVATE_IDENTITY_HOLD', 1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'ALTER ANY USER'), 0) <> 1
    THROW 57108, N'PATHB_SECURITY_PRINCIPAL_VISIBILITY_HOLD', 1;
IF EXISTS (SELECT 1 FROM sys.database_principals
           WHERE [type] IN (N'E', N'X')
             AND ([sid] IS NULL OR DATALENGTH([sid]) NOT IN (16, 18)
                  OR (DATALENGTH([sid]) = 18 AND SUBSTRING([sid], 17, 2) <> 0xAADE)
                  OR COALESCE(authentication_type_desc, N'') <> N'EXTERNAL'))
    THROW 57109, N'PATHB_EXTERNAL_SID_SHAPE_HOLD', 1;
IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
   OR (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo')) <> 69
   OR (SELECT COUNT(*) FROM dbo.SchemaVersions) <> 3
   OR (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN (
       N'1.8.0-CREATE-LOWRISK', N'1.8.0-LOWRISK-DIRECT',
       N'1.8.0-EXCEL-CANONICAL-STATUS')) <> 3
   OR EXISTS (SELECT 1 FROM sys.check_constraints WHERE is_disabled = 1 OR is_not_trusted = 1)
   OR EXISTS (SELECT 1 FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1)
   OR EXISTS (SELECT 1 FROM sys.triggers tr
              LEFT JOIN sys.sql_modules m ON m.object_id = tr.object_id
              WHERE tr.parent_class = 1 AND tr.is_ms_shipped = 0
                AND (tr.is_disabled = 1 OR m.definition IS NULL))
    THROW 57104, N'PATHB_MI_GRANT_SCHEMA_HOLD', 1;
-- A hidden mapping, direct user, alias, login-based mapping, group entitlement,
-- or collision must stop before this one-time write, not be renamed/dropped.
IF EXISTS (SELECT 1 FROM sys.database_principals p
           WHERE p.[type] IN (N'E', N'X') AND DATALENGTH(p.[sid]) IN (16, 18)
             AND TRY_CONVERT(uniqueidentifier, SUBSTRING(p.[sid], 1, 16))
                 IN (@objectId, @clientId))
   OR EXISTS (SELECT 1 FROM sys.database_principals
              WHERE name = N'api-fieldvisit-pathb-uat_pathb')
    THROW 57105, N'PATHB_MI_GRANT_ALREADY_EXISTS_OR_COLLISION_HOLD', 1;
-- A unique alias is used with Azure SQL's documented WITH OBJECT_ID syntax.
-- This cannot establish group membership; that is an independent admission gate.
DECLARE @alias sysname = N'api-fieldvisit-pathb-uat_pathb';
DECLARE @statement nvarchar(900) =
    N'CREATE USER ' + QUOTENAME(@alias) +
    N' FROM EXTERNAL PROVIDER WITH OBJECT_ID = ' +
    QUOTENAME(CONVERT(nvarchar(36), @objectId), N'''') + N';';
BEGIN TRY
    BEGIN TRANSACTION;
    EXEC sys.sp_executesql @statement;
    EXEC sys.sp_executesql N'GRANT CONNECT TO [api-fieldvisit-pathb-uat_pathb];';
    IF (SELECT COUNT(*) FROM sys.database_principals
        WHERE name = @alias AND [type] = N'E'
          AND DATALENGTH([sid]) IN (16, 18)
          AND TRY_CONVERT(uniqueidentifier, SUBSTRING([sid], 1, 16))
               IN (@objectId, @clientId)) <> 1
        THROW 57106, N'PATHB_MI_GRANT_POSTCHECK_HOLD', 1;
    IF (SELECT COUNT(*) FROM sys.database_principals p
        WHERE p.[type] = N'E'
          AND DATALENGTH(p.[sid]) IN (16, 18)
          AND TRY_CONVERT(uniqueidentifier, SUBSTRING(p.[sid], 1, 16))
              IN (@objectId, @clientId)) <> 1
        THROW 57110, N'PATHB_MI_GRANT_DUPLICATE_IDENTITY_HOLD', 1;
    COMMIT TRANSACTION;
    SELECT N'PATHB_MI_USER_AND_CONNECT_CANDIDATE_APPLIED' AS code,
           N'LIVE_API_LOGIN_STILL_UNVERIFIED' AS state;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW 57107, N'PATHB_MI_GRANT_FAILED_OUTCOME_NEEDS_INDEPENDENT_READBACK', 1;
END CATCH;
