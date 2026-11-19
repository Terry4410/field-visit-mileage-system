-- REVIEW ONLY. NOT AUTHORIZED FOR AZURE EXECUTION.
-- Catalog evidence only; no impersonation, business-row SELECT, or permission change.
SET NOCOUNT ON;
IF COALESCE(CONVERT(int, SERVERPROPERTY(N'EngineEdition')), 0) <> 5
   OR COALESCE(CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')), N'') COLLATE Latin1_General_100_BIN2 <> N'sql-fieldvisit-jpe-pathb-uat'
   OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> N'db-fieldvisit-pathb-uat'
    THROW 59801, N'PATHB_TARGET_HOLD', 1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'VIEW DEFINITION'), 0) <> 1
    THROW 59802, N'CATALOG_VISIBILITY_HOLD', 1;

DECLARE @principalId int;
SELECT @principalId = principal_id
FROM sys.database_principals
WHERE name COLLATE DATABASE_DEFAULT = N'api-fieldvisit-pathb-uat_pathb' COLLATE DATABASE_DEFAULT
  AND type = N'E' AND authentication_type_desc = N'EXTERNAL'
  AND DATALENGTH(sid) = 16;
IF @principalId IS NULL OR
   (SELECT COUNT(*) FROM sys.database_principals WHERE principal_id = @principalId) <> 1
    THROW 59803, N'APP_PRINCIPAL_HOLD', 1;

DECLARE @wanted TABLE (table_name sysname COLLATE DATABASE_DEFAULT, operation nvarchar(10) COLLATE DATABASE_DEFAULT,
                       PRIMARY KEY (table_name, operation));
INSERT @wanted VALUES
 (N'VisitTripSnapshots', N'SELECT'), (N'VisitTripSnapshotStops', N'SELECT'),
 (N'CorrectionRequests', N'SELECT'), (N'UserRoles', N'SELECT'),
 (N'UserRoleAssignments', N'SELECT'), (N'Roles', N'SELECT'),
 (N'UserIdentityProfiles', N'SELECT'), (N'UserFavoriteLocations', N'SELECT'),
 (N'RouteCalculationAttempts', N'SELECT'), (N'RouteCalculationAttempts', N'INSERT'),
 (N'RouteCalculationAttempts', N'UPDATE'), (N'MileageGovernanceEvents', N'INSERT'),
 (N'VisitTripSnapshots', N'INSERT'), (N'VisitTripSnapshotStops', N'INSERT'),
 (N'CorrectionRequests', N'INSERT'), (N'CorrectionRequests', N'UPDATE'),
 (N'VisitTrips', N'UPDATE'), (N'VisitTripStops', N'UPDATE'),
 (N'VisitTripStops', N'DELETE'), (N'MileageCalculations', N'UPDATE');

;WITH role_tree AS (
 SELECT @principalId AS principal_id, 0 AS depth
 UNION ALL
 SELECT m.role_principal_id, t.depth + 1
 FROM role_tree t JOIN sys.database_role_members m ON m.member_principal_id = t.principal_id
 WHERE t.depth < 16
), member_ids AS (
 SELECT DISTINCT principal_id FROM role_tree
 UNION SELECT principal_id FROM sys.database_principals WHERE name = N'public'
), facts AS (
 SELECT w.table_name, w.operation, o.object_id,
        p.name AS grantee, p.type_desc AS grantee_type,
        dp.class_desc AS scope, dp.minor_id, dp.state_desc AS permission_state,
        dp.permission_name
 FROM @wanted w
 LEFT JOIN sys.tables o ON o.name COLLATE DATABASE_DEFAULT = w.table_name COLLATE DATABASE_DEFAULT
                          AND o.schema_id = SCHEMA_ID(N'dbo')
 LEFT JOIN sys.database_permissions dp ON dp.permission_name COLLATE DATABASE_DEFAULT = w.operation COLLATE DATABASE_DEFAULT
        AND ((dp.class = 1 AND dp.major_id = o.object_id)
          OR (dp.class = 3 AND dp.major_id = SCHEMA_ID(N'dbo')) OR dp.class = 0)
        AND dp.grantee_principal_id IN (SELECT principal_id FROM member_ids)
 LEFT JOIN sys.database_principals p ON p.principal_id = dp.grantee_principal_id
)
SELECT table_name, operation,
       CASE WHEN object_id IS NULL THEN N'OBJECT_NOT_VISIBLE_HOLD'
            WHEN permission_state IS NULL THEN N'NO_CATALOG_GRANT_EFFECTIVE_UNKNOWN'
            ELSE permission_state END AS catalog_state,
       grantee, grantee_type, scope, minor_id,
       CASE WHEN minor_id > 0 THEN COL_NAME(object_id, minor_id) END AS column_name
FROM facts
ORDER BY table_name, operation, catalog_state, grantee
OPTION (MAXRECURSION 17);

-- Separate role/ownership check: role membership is not proof of effective access.
;WITH role_tree AS (
 SELECT @principalId AS principal_id, 0 AS depth
 UNION ALL
 SELECT m.role_principal_id, t.depth + 1
 FROM role_tree t JOIN sys.database_role_members m ON m.member_principal_id = t.principal_id
 WHERE t.depth < 16
)
SELECT DISTINCT p.name AS inherited_role
FROM role_tree t JOIN sys.database_principals p ON p.principal_id = t.principal_id
WHERE t.depth > 0
ORDER BY inherited_role
OPTION (MAXRECURSION 17);

-- Broad permissions and DENY overrides must be reviewed before proposing any grant.
;WITH role_tree AS (
 SELECT @principalId AS principal_id, 0 AS depth
 UNION ALL
 SELECT m.role_principal_id, t.depth + 1
 FROM role_tree t JOIN sys.database_role_members m ON m.member_principal_id = t.principal_id
 WHERE t.depth < 16
), member_ids AS (
 SELECT DISTINCT principal_id FROM role_tree
 UNION SELECT principal_id FROM sys.database_principals WHERE name = N'public'
)
SELECT p.name AS grantee, dp.class_desc AS scope, dp.permission_name,
       dp.state_desc AS permission_state
FROM sys.database_permissions dp
JOIN member_ids m ON m.principal_id = dp.grantee_principal_id
JOIN sys.database_principals p ON p.principal_id = m.principal_id
WHERE (dp.class = 0 OR (dp.class = 3 AND dp.major_id = SCHEMA_ID(N'dbo')))
  AND dp.permission_name IN (N'CONTROL', N'ALTER', N'SELECT', N'INSERT', N'UPDATE', N'DELETE')
ORDER BY grantee, scope, permission_name
OPTION (MAXRECURSION 17);
