-- FIELD VISIT PATH B - SINGLE RESULTSET ACL CATALOG REVIEW CANDIDATE.
-- NONDEPLOY / REVIEW ONLY. NOT AUTHORIZED FOR LIVE EXECUTION.
-- This file is a NEW derivative; it is NOT covered by the prior one-shot SHA authorization.
-- No business-row access, impersonation, DCL, DDL or DML.
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
DECLARE @wanted TABLE(table_name sysname COLLATE DATABASE_DEFAULT NOT NULL,
 operation nvarchar(10) COLLATE DATABASE_DEFAULT NOT NULL,
 PRIMARY KEY(table_name,operation));
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
IF (SELECT COUNT(*) FROM @wanted) <> 20
    THROW 59804, N'WANTED_OPERATIONS_HOLD', 1;
;WITH role_tree AS (
 SELECT @principalId AS principal_id, 0 AS depth
 UNION ALL
 SELECT m.role_principal_id, t.depth+1
 FROM role_tree AS t
 JOIN sys.database_role_members AS m ON m.member_principal_id=t.principal_id
 WHERE t.depth<16
), members AS (
 SELECT DISTINCT principal_id FROM role_tree
 UNION
 SELECT principal_id FROM sys.database_principals WHERE name=N'public'
), wanted_permissions AS (
 SELECT w.table_name, w.operation, o.object_id,
        dp.state_desc, dp.class_desc, dp.minor_id,
        p.name AS grantee, dp.permission_name
 FROM @wanted AS w
 LEFT JOIN sys.tables AS o
   ON o.name COLLATE DATABASE_DEFAULT=w.table_name COLLATE DATABASE_DEFAULT
  AND o.schema_id=SCHEMA_ID(N'dbo')
 LEFT JOIN sys.database_permissions AS dp
   ON dp.permission_name COLLATE DATABASE_DEFAULT=w.operation COLLATE DATABASE_DEFAULT
  AND ((dp.class=1 AND dp.major_id=o.object_id)
       OR (dp.class=3 AND dp.major_id=SCHEMA_ID(N'dbo')) OR dp.class=0)
  AND dp.grantee_principal_id IN (SELECT principal_id FROM members)
 LEFT JOIN sys.database_principals AS p ON p.principal_id=dp.grantee_principal_id
), broad_permissions AS (
 SELECT p.name AS grantee, dp.class_desc, dp.permission_name, dp.state_desc
 FROM sys.database_permissions AS dp
 JOIN members AS m ON m.principal_id=dp.grantee_principal_id
 JOIN sys.database_principals AS p ON p.principal_id=m.principal_id
 WHERE (dp.class=0 OR (dp.class=3 AND dp.major_id=SCHEMA_ID(N'dbo')))
  AND dp.permission_name IN (N'CONTROL',N'ALTER',N'SELECT',N'INSERT',N'UPDATE',N'DELETE')
), all_rows AS (
 SELECT N'PERMISSION' AS record_type,
   CONVERT(nvarchar(128),table_name) AS table_name,
   CONVERT(nvarchar(128),operation) AS operation,
   CONVERT(nvarchar(128),CASE WHEN object_id IS NULL THEN N'OBJECT_NOT_VISIBLE_HOLD'
       WHEN state_desc IS NULL THEN N'NO_CATALOG_GRANT_EFFECTIVE_UNKNOWN' ELSE state_desc END) AS catalog_state,
   CONVERT(nvarchar(128),grantee) AS grantee,
   CONVERT(nvarchar(128),class_desc) AS scope,
   CONVERT(nvarchar(128),CASE WHEN minor_id>0 THEN COL_NAME(object_id,minor_id) END) AS detail
 FROM wanted_permissions
 UNION ALL
 SELECT N'ROLE',NULL,NULL,N'MEMBER',CONVERT(nvarchar(128),p.name),N'ROLE',NULL
 FROM role_tree AS t JOIN sys.database_principals AS p ON p.principal_id=t.principal_id
 WHERE t.depth>0
 UNION ALL
 SELECT N'BROAD_PERMISSION',NULL,CONVERT(nvarchar(128),permission_name),
   CONVERT(nvarchar(128),state_desc),CONVERT(nvarchar(128),grantee),
   CONVERT(nvarchar(128),class_desc),NULL
 FROM broad_permissions
 UNION ALL
 SELECT N'SUMMARY',NULL,N'WANTED_OPERATIONS',N'20',NULL,NULL,NULL
 UNION ALL
 SELECT N'SUMMARY',NULL,N'CATALOG_ONLY_NOT_EFFECTIVE_PROOF',N'TRUE',NULL,NULL,NULL
 UNION ALL
 SELECT N'SUMMARY',NULL,N'ROLE_COUNT',CONVERT(nvarchar(128),(SELECT COUNT(DISTINCT principal_id) FROM role_tree WHERE depth>0)),NULL,NULL,NULL
 UNION ALL
 SELECT N'SUMMARY',NULL,N'BROAD_PERMISSION_ROWS',CONVERT(nvarchar(128),(SELECT COUNT(*) FROM broad_permissions)),NULL,NULL,NULL
)
SELECT record_type,table_name,operation,catalog_state,grantee,scope,detail
FROM all_rows
ORDER BY record_type,table_name,operation,catalog_state,grantee
OPTION (MAXRECURSION 17);
