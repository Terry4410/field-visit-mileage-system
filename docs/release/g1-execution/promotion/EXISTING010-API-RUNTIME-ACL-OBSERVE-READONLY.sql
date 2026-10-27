-- NONDEPLOY / SELECT-ONLY METADATA EVIDENCE. NOT a GRANT, REVOKE, DENY or migration script.
-- Fixed target: personal Azure UAT only. Any unknown, incomplete rights, or missing private SID => HOLD.
-- Operator/WORK must set session-only FieldVisit.G1.PrivateApiSidHex to the already-verified
-- actual API database login SID (32 hex characters) privately. Never publish the value.
-- Query Editor uses its OWN identity; this reads CATALOG permissions for that explicit SID;
-- it does not impersonate the API and does not prove effective runtime permissions.
SET NOCOUNT ON;
IF COALESCE(CONVERT(int,SERVERPROPERTY(N'EngineEdition')),0)<>5
   OR COALESCE(CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')),N'')
        COLLATE Latin1_General_100_BIN2<>N'sql-fieldvisit-jpe-uat'
   OR DB_NAME() COLLATE Latin1_General_100_BIN2<>N'db-fieldvisit-uat'
    THROW 52001,N'API_ACL_TARGET_HOLD',1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION'),0)<>1
    THROW 52005,N'API_ACL_METADATA_VISIBILITY_HOLD',1;
DECLARE @sidHex nvarchar(32)=TRY_CONVERT(nvarchar(32),
    SESSION_CONTEXT(N'FieldVisit.G1.PrivateApiSidHex'));
IF @sidHex IS NULL OR LEN(@sidHex)<>32
   OR @sidHex COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9A-Fa-f]%'
    THROW 52006,N'API_ACL_PRIVATE_IDENTITY_HOLD',1;
DECLARE @sid varbinary(16)=TRY_CONVERT(varbinary(16),@sidHex,2);
DECLARE @principalId int;
IF @sid IS NULL
   OR (SELECT COUNT(*) FROM sys.database_principals
       WHERE sid=@sid AND type=N'E' AND authentication_type_desc=N'EXTERNAL')<>1
    THROW 52007,N'API_ACL_SID_BINDING_HOLD',1;
SELECT @principalId=principal_id FROM sys.database_principals
 WHERE sid=@sid AND type=N'E' AND authentication_type_desc=N'EXTERNAL';
IF @principalId IS NULL THROW 52007,N'API_ACL_SID_BINDING_HOLD',1;

-- Catalog roles can have nested memberships; always include the implicit public role.
-- Absence of metadata grants or DENYs is NOT a successful effective-permission test.
;WITH RoleClosure AS
(
    SELECT drm.role_principal_id AS principal_id,1 AS depth
    FROM sys.database_role_members drm
    WHERE drm.member_principal_id=@principalId
    UNION ALL
    SELECT drm.role_principal_id,c.depth+1
    FROM sys.database_role_members drm JOIN RoleClosure c
      ON drm.member_principal_id=c.principal_id
    WHERE c.depth<16
),
MemberIds AS
(
    SELECT @principalId AS principal_id
    UNION
    SELECT principal_id FROM RoleClosure
    UNION
    SELECT principal_id FROM sys.database_principals WHERE name=N'public'
),
Facts AS
(
 SELECT
  (SELECT COUNT(*) FROM MemberIds m JOIN sys.database_principals p
     ON p.principal_id=m.principal_id WHERE p.name=N'db_datawriter') AS broadWriterRoles,
  (SELECT COUNT(*) FROM MemberIds m JOIN sys.database_principals p
     ON p.principal_id=m.principal_id WHERE p.name=N'db_datareader') AS broadReaderRoles,
  (SELECT COUNT(*) FROM MemberIds m JOIN sys.database_principals p
     ON p.principal_id=m.principal_id
     WHERE p.name IN(N'db_owner',N'db_ddladmin',N'db_securityadmin')) AS privilegedRoles,
  (SELECT COUNT(*) FROM sys.database_permissions dp JOIN MemberIds m
      ON m.principal_id=dp.grantee_principal_id
     WHERE dp.class IN(0,3) AND (dp.class=0 OR dp.major_id=SCHEMA_ID(N'dbo'))
       AND dp.permission_name IN(N'INSERT',N'UPDATE',N'DELETE')
       AND dp.state IN(N'G',N'W')) AS broadDmlGrants,
  (SELECT COUNT(*) FROM sys.objects o WHERE o.principal_id=@principalId) +
  (SELECT COUNT(*) FROM sys.schemas s WHERE s.principal_id=@principalId) AS appOwnedObjects,
  (SELECT COUNT(*) FROM sys.database_permissions dp JOIN MemberIds m
      ON m.principal_id=dp.grantee_principal_id
     WHERE dp.class=1 AND dp.minor_id>0
       AND dp.permission_name IN(N'INSERT',N'UPDATE',N'DELETE')
       AND dp.state IN(N'G',N'W')
       AND dp.major_id IN(OBJECT_ID(N'dbo.SchemaVersions',N'U'),
                          OBJECT_ID(N'dbo.SchemaMigrationDataBaselines',N'U'),
                          OBJECT_ID(N'dbo.FieldVisitMigrationLedger',N'U')))
       AS protectedColumnGrants
),
Protected AS
(
 SELECT N'SchemaVersions' AS tableName,OBJECT_ID(N'dbo.SchemaVersions',N'U') AS id,0 AS optional
 UNION ALL
 SELECT N'SchemaMigrationDataBaselines',OBJECT_ID(N'dbo.SchemaMigrationDataBaselines',N'U'),0
 UNION ALL
 SELECT N'FieldVisitMigrationLedger',OBJECT_ID(N'dbo.FieldVisitMigrationLedger',N'U'),1
)
SELECT code,status FROM
(
 SELECT N'API_CATALOG_SID_MATCH' AS code,N'PASS_SID_ONLY' AS status
 UNION ALL
 SELECT N'BROAD_DB_DATAWRITER_ROLE',
    CASE WHEN broadWriterRoles>0 THEN N'HOLD_PRESENT'
         ELSE N'ABSENT_METADATA_ONLY' END FROM Facts
 UNION ALL
 SELECT N'BROAD_DB_DATAREADER_ROLE',
    CASE WHEN broadReaderRoles>0 THEN N'REVIEW_BROAD_SELECT'
         ELSE N'ABSENT_METADATA_ONLY' END FROM Facts
 UNION ALL
 SELECT N'PRIVILEGED_DATABASE_ROLE',
    CASE WHEN privilegedRoles>0 THEN N'HOLD_PRESENT'
         ELSE N'ABSENT_METADATA_ONLY' END FROM Facts
 UNION ALL
 SELECT N'BROAD_DATABASE_OR_DBO_DML_GRANT',
    CASE WHEN broadDmlGrants>0 THEN N'HOLD_PRESENT'
         ELSE N'ABSENT_METADATA_ONLY' END FROM Facts
 UNION ALL
 SELECT N'APPLICATION_OWNED_OBJECTS',
    CASE WHEN appOwnedObjects>0 THEN N'HOLD_OWNER_BYPASS'
         ELSE N'NONE_VISIBLE' END FROM Facts
 UNION ALL
 SELECT N'PROTECTED_HISTORY_COLUMN_GRANTS',
    CASE WHEN protectedColumnGrants>0 THEN N'HOLD_COLUMN_GRANT_EXCEPTION'
         ELSE N'NONE_VISIBLE' END FROM Facts
 UNION ALL
 SELECT N'FULL_EFFECTIVE_PERMISSION_AND_BUSINESS_DML',
    N'HOLD_REQUIRES_API_BOUND_NEGATIVE_AND_POSITIVE_TEST'
 UNION ALL
 SELECT N'HISTORY_DML_'+p.tableName,
    CASE WHEN p.id IS NULL AND p.optional=1 THEN N'NOT_PRESENT_UNTIL_APPROVED_UPGRADE'
         WHEN p.id IS NULL THEN N'HOLD_REQUIRED_OBJECT_MISSING'
         WHEN (SELECT COUNT(DISTINCT dp.permission_name)
               FROM sys.database_permissions dp JOIN MemberIds m
                  ON m.principal_id=dp.grantee_principal_id
               WHERE dp.class=1 AND dp.major_id=p.id AND dp.minor_id=0
                 AND dp.permission_name IN(N'INSERT',N'UPDATE',N'DELETE')
                 AND dp.state=N'D')=3
         THEN N'THREE_DENIES_VISIBLE_EFFECTIVE_TEST_PENDING'
         ELSE N'HOLD_INCOMPLETE_EXPLICIT_DENY' END
 FROM Protected p
) AS results ORDER BY code
OPTION (MAXRECURSION 32);
