-- Path B only. SELECT-only catalog receipt; no business rows and no raw identities.
-- Input session context (set privately by independently authorized Owner SQL runner):
-- FieldVisit.PathB.ApiMiObjectId and FieldVisit.PathB.ApiMiClientId.
SET NOCOUNT ON;
IF COALESCE(CONVERT(int, SERVERPROPERTY(N'EngineEdition')), 0) <> 5
   OR COALESCE(CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')), N'')
      COLLATE Latin1_General_100_BIN2 <> N'sql-fieldvisit-jpe-pathb-uat'
   OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> N'db-fieldvisit-pathb-uat'
    THROW 57001, N'PATHB_EXACT_SQL_TARGET_HOLD', 1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'VIEW DEFINITION'), 0) <> 1
    THROW 57002, N'PATHB_CATALOG_METADATA_VISIBILITY_HOLD', 1;
DECLARE @objectId uniqueidentifier = TRY_CONVERT(uniqueidentifier,
    SESSION_CONTEXT(N'FieldVisit.PathB.ApiMiObjectId'));
DECLARE @clientId uniqueidentifier = TRY_CONVERT(uniqueidentifier,
    SESSION_CONTEXT(N'FieldVisit.PathB.ApiMiClientId'));
IF @objectId IS NULL OR @clientId IS NULL
   OR @objectId = '00000000-0000-0000-0000-000000000000'
   OR @clientId = '00000000-0000-0000-0000-000000000000'
    THROW 57003, N'PATHB_PRIVATE_MI_BINDING_HOLD', 1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'ALTER ANY USER'), 0) <> 1
    THROW 57005, N'PATHB_SECURITY_PRINCIPAL_VISIBILITY_HOLD', 1;
IF EXISTS (SELECT 1 FROM sys.database_principals
           WHERE [type] IN (N'E', N'X')
             AND ([sid] IS NULL OR DATALENGTH([sid]) NOT IN (16, 18)
                  OR (DATALENGTH([sid]) = 18 AND SUBSTRING([sid], 17, 2) <> 0xAADE)
                  OR COALESCE(authentication_type_desc, N'') <> N'EXTERNAL'))
    THROW 57006, N'PATHB_EXTERNAL_SID_SHAPE_HOLD', 1;
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
    THROW 57004, N'PATHB_INSTALLED_SCHEMA_HOLD', 1;
DECLARE @objectMatches int, @clientMatches int, @groups int, @directDenies int;
DECLARE @directGrants int, @roleConnectGrants int, @roleConnectDenies int;
;WITH ExternalPrincipals AS (
    SELECT principal_id, name, [type], [sid],
        TRY_CONVERT(uniqueidentifier, SUBSTRING([sid], 1, 16)) AS mappedGuid
    FROM sys.database_principals
    WHERE [type] IN (N'E', N'X') AND DATALENGTH([sid]) IN (16, 18)
      AND authentication_type_desc = N'EXTERNAL'
)
SELECT @objectMatches = COALESCE(SUM(CASE WHEN [type] = N'E' AND mappedGuid = @objectId THEN 1 ELSE 0 END), 0),
       @clientMatches = COALESCE(SUM(CASE WHEN [type] = N'E' AND mappedGuid = @clientId THEN 1 ELSE 0 END), 0),
       @groups = COALESCE(SUM(CASE WHEN [type] = N'X' THEN 1 ELSE 0 END), 0)
FROM ExternalPrincipals;
SELECT @directDenies = COUNT(*)
FROM sys.database_principals p JOIN sys.database_permissions dp
  ON dp.grantee_principal_id = p.principal_id
WHERE p.[type] = N'E' AND DATALENGTH(p.[sid]) IN (16, 18)
  AND TRY_CONVERT(uniqueidentifier, SUBSTRING(p.[sid], 1, 16)) IN (@objectId, @clientId)
  AND dp.permission_name = N'CONNECT' AND dp.[state] = N'D';
-- These are catalog observations, NOT effective-permission proof.
-- Only direct role memberships are counted; nested database roles, Entra
-- transitive groups, server-level policy and live token/login remain UNKNOWN.
SELECT @directGrants = COUNT(*)
FROM sys.database_principals p JOIN sys.database_permissions dp
  ON dp.grantee_principal_id = p.principal_id
WHERE p.[type] = N'E' AND DATALENGTH(p.[sid]) IN (16, 18)
  AND TRY_CONVERT(uniqueidentifier, SUBSTRING(p.[sid], 1, 16)) IN (@objectId, @clientId)
  AND dp.permission_name = N'CONNECT' AND dp.[state] IN (N'G', N'W');
SELECT @roleConnectGrants = COUNT(*),
       @roleConnectDenies = COALESCE(SUM(CASE WHEN dp.[state] = N'D' THEN 1 ELSE 0 END), 0)
FROM sys.database_principals p
JOIN sys.database_role_members drm ON drm.member_principal_id = p.principal_id
JOIN sys.database_principals rolep ON rolep.principal_id = drm.role_principal_id
JOIN sys.database_permissions dp ON dp.grantee_principal_id = rolep.principal_id
WHERE p.[type] = N'E' AND DATALENGTH(p.[sid]) IN (16, 18)
  AND TRY_CONVERT(uniqueidentifier, SUBSTRING(p.[sid], 1, 16)) IN (@objectId, @clientId)
  AND rolep.[type] = N'R' AND dp.permission_name = N'CONNECT';
SELECT N'PATHB_MI_CATALOG' AS code,
       CASE WHEN @objectMatches > 1 OR @clientMatches > 1 OR
                      (@objectMatches > 0 AND @clientMatches > 0 AND @objectId <> @clientId)
                THEN N'HOLD_AMBIGUOUS'
            WHEN @objectMatches + @clientMatches = 0 THEN N'NO_DIRECT_MATCH_VISIBLE'
            ELSE N'DIRECT_MATCH_VISIBLE' END AS directPrincipal,
       CASE WHEN @groups > 0 THEN N'GROUP_MEMBERSHIP_NOT_PROVEN'
            ELSE N'NO_GROUP_PRINCIPALS_VISIBLE' END AS groupEvidence,
       CASE WHEN @directDenies > 0 THEN N'EXPLICIT_CONNECT_DENY_VISIBLE'
            ELSE N'NO_DIRECT_CONNECT_DENY_VISIBLE_NOT_EFFECTIVE_PROOF' END AS directConnectEvidence,
       CASE WHEN @directGrants > 0 THEN N'DIRECT_CONNECT_GRANT_VISIBLE' ELSE N'NO_DIRECT_CONNECT_GRANT_VISIBLE' END AS directConnectGrant,
       CASE WHEN @roleConnectGrants > 0 THEN N'DIRECT_ROLE_CONNECT_ENTRY_VISIBLE' ELSE N'NO_DIRECT_ROLE_CONNECT_ENTRY_VISIBLE' END AS directRoleConnect,
       CASE WHEN @roleConnectDenies > 0 THEN N'DIRECT_ROLE_CONNECT_DENY_VISIBLE' ELSE N'NO_DIRECT_ROLE_CONNECT_DENY_VISIBLE' END AS directRoleDeny,
       N'UNKNOWN_TRANSITIVE_GROUP_ROLE_AND_LIVE_MI_LOGIN' AS effectivePermission;
