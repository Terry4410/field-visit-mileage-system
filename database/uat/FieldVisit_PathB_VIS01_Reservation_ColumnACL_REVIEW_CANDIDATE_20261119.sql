-- Field Visit Path B VIS01 optional Scheme A reservation: exact two-column ACL candidate.
-- REVIEW / NONDEPLOY UNTIL SEPARATE EXACT-SHA OWNER DCL APPROVAL.
-- No CREATE/ALTER/DROP, no data DML, no GRANT outside the two explicitly named columns.
-- Previously successful 23 runtime grants and all UAT Seed data are preserved.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT<>0 OR (2 & @@OPTIONS)=2 THROW 59700,'AMBIENT_TRANSACTION_HOLD',1;
IF COALESCE(TRY_CONVERT(int,SERVERPROPERTY(N'EngineEdition')),0)<>5
 OR DB_NAME()<>N'db-fieldvisit-pathb-uat'
 OR CONVERT(nvarchar(256),SERVERPROPERTY(N'ServerName'))<>N'sql-fieldvisit-jpe-pathb-uat'
 THROW 59701,'EXACT_PATHB_TARGET_HOLD',1;
IF NOT EXISTS(SELECT 1 FROM sys.dm_exec_connections
 WHERE session_id=@@SPID AND encrypt_option=N'TRUE')
 THROW 59702,'TLS_HOLD',1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL'),0)<>1
 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION'),0)<>1
 THROW 59703,'OWNER_AUTHORITY_HOLD',1;
DECLARE @pid int;
SELECT @pid=principal_id FROM sys.database_principals
 WHERE name=N'api-fieldvisit-pathb-uat_pathb'
 AND type=N'E' AND authentication_type_desc=N'EXTERNAL' AND DATALENGTH(sid)=16;
IF @pid IS NULL THROW 59704,'EXACT_EXISTING_APP_MI_HOLD',1;
IF (SELECT COUNT(*) FROM sys.database_principals p
 WHERE DATALENGTH(p.sid)=16 AND p.sid=(SELECT sid FROM sys.database_principals WHERE principal_id=@pid))<>1
 THROW 59705,'MI_SID_UNIQUENESS_HOLD',1;
IF OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NULL
 OR OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
 OR COL_LENGTH(N'dbo.LocationCreateReservations',N'LocationId') IS NULL
 OR COL_LENGTH(N'dbo.LocationCreateReservations',N'ReservationStatus') IS NULL
 THROW 59706,'OPTIONAL_SCHEMA_OBJECTS_HOLD',1;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions
 WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK')
 THROW 59707,'SCHEMEA_MARKER_HOLD',1;
-- Bind to the already-applied exact 16 table SELECT + 5 INSERT + 2 identity SELECT.
DECLARE @required TABLE(t sysname NOT NULL,p sysname NOT NULL,c sysname NULL);
INSERT @required(t,p,c) VALUES
(N'Employments',N'SELECT',NULL),
(N'EmploymentStatusPeriods',N'SELECT',NULL),
(N'TeamMemberships',N'SELECT',NULL),
(N'TeamCenterAssignments',N'SELECT',NULL),
(N'EmploymentDeploymentSiteAssignments',N'SELECT',NULL),
(N'TeamDeploymentSiteAssignments',N'SELECT',NULL),
(N'DeploymentSites',N'SELECT',NULL),
(N'Centers',N'SELECT',NULL),
(N'DeploymentSiteLocationAssignments',N'SELECT',NULL),
(N'Locations',N'SELECT',NULL),
(N'VisitTrips',N'SELECT',NULL),
(N'VisitTripStops',N'SELECT',NULL),
(N'MileageCalculations',N'SELECT',NULL),
(N'Projects',N'SELECT',NULL),
(N'VisitTypes',N'SELECT',NULL),
(N'SchemaVersions',N'SELECT',NULL),
(N'VisitTrips',N'INSERT',NULL),
(N'VisitTripStops',N'INSERT',NULL),
(N'MileageCalculations',N'INSERT',NULL),
(N'VisitTripStatusHistory',N'INSERT',NULL),
(N'AuditLogs',N'INSERT',NULL),
(N'VisitTripStatusHistory',N'SELECT',N'VisitTripStatusHistoryId'),
(N'AuditLogs',N'SELECT',N'AuditLogId');
IF (SELECT COUNT(*) FROM @required)<>23 THROW 59708,'REQUIRED_ACL_COUNT_HOLD',1;
IF EXISTS(
 SELECT 1 FROM @required r
 LEFT JOIN sys.tables t ON t.name=r.t AND t.schema_id=SCHEMA_ID(N'dbo')
 LEFT JOIN sys.columns c ON c.object_id=t.object_id AND c.name=r.c
 WHERE t.object_id IS NULL OR (r.c IS NOT NULL AND c.column_id IS NULL)
) THROW 59709,'REQUIRED_ACL_OBJECT_HOLD',1;
IF EXISTS(
 SELECT 1 FROM @required r JOIN sys.tables t ON t.name=r.t AND t.schema_id=SCHEMA_ID(N'dbo')
 LEFT JOIN sys.columns c ON c.object_id=t.object_id AND c.name=r.c
 WHERE NOT EXISTS (SELECT 1 FROM sys.database_permissions p
  WHERE p.class=1 AND p.major_id=t.object_id AND p.minor_id=COALESCE(c.column_id,0)
   AND p.permission_name=r.p AND p.grantee_principal_id=@pid AND p.state IN(N'G',N'W'))
) THROW 59710,'PREVIOUS_23_GRANTS_NOT_PRESENT_HOLD',1;
IF EXISTS(
 SELECT 1 FROM sys.database_role_members rm
 JOIN sys.database_principals p ON p.principal_id=rm.role_principal_id
 WHERE rm.member_principal_id=@pid AND p.name IN
 (N'db_owner',N'db_datareader',N'db_datawriter',N'db_securityadmin',N'db_ddladmin')
) THROW 59711,'BROAD_MI_ROLE_HOLD',1;
-- Reject unexpected existing table/column permissions; no retry/overwrite.
IF EXISTS(SELECT 1 FROM sys.database_permissions p
 WHERE p.grantee_principal_id=@pid AND p.class=1
 AND p.major_id=OBJECT_ID(N'dbo.LocationCreateReservations'))
 THROW 59712,'EXISTING_RESERVATION_GRANT_HOLD',1;
IF EXISTS(SELECT 1 FROM sys.database_permissions p
 WHERE p.grantee_principal_id IN
 (@pid,(SELECT principal_id FROM sys.database_principals WHERE name=N'public'))
 AND p.state=N'D' AND p.permission_name IN(N'SELECT',N'CONTROL')
 AND (p.class=0 OR (p.class=3 AND p.major_id=SCHEMA_ID(N'dbo'))
 OR (p.class=1 AND p.major_id=OBJECT_ID(N'dbo.LocationCreateReservations'))))
 THROW 59713,'RELEVANT_DENY_HOLD',1;
BEGIN TRY
 BEGIN TRANSACTION;
 IF EXISTS(SELECT 1 FROM sys.database_permissions p
  WHERE p.grantee_principal_id=@pid AND p.class=1
   AND p.major_id=OBJECT_ID(N'dbo.LocationCreateReservations'))
  THROW 59714,'CONCURRENT_RESERVATION_ACL_HOLD',1;
 GRANT SELECT ON OBJECT::dbo.[LocationCreateReservations] ([LocationId])
  TO [api-fieldvisit-pathb-uat_pathb];
 GRANT SELECT ON OBJECT::dbo.[LocationCreateReservations] ([ReservationStatus])
  TO [api-fieldvisit-pathb-uat_pathb];
 IF (SELECT COUNT(*) FROM sys.database_permissions p
  JOIN sys.columns c ON c.object_id=p.major_id AND c.column_id=p.minor_id
  WHERE p.class=1 AND p.grantee_principal_id=@pid
   AND p.major_id=OBJECT_ID(N'dbo.LocationCreateReservations')
   AND p.state IN(N'G',N'W') AND p.permission_name=N'SELECT'
   AND c.name IN(N'LocationId',N'ReservationStatus'))<>2
  THROW 59715,'TWO_COLUMN_READBACK_HOLD',1;
 COMMIT TRANSACTION;
 SELECT N'PATHB_VIS01_OPTIONAL_RESERVATION_ACL' AS Evidence,
 N'APPLIED_2_COLUMN_SELECT_CATALOG_ONLY' AS State,2 AS ColumnSelectGrants;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
