-- REVIEW ONLY: Field Visit Path B formal locations. Separate exact-file Owner approval required.
-- No schema changes, no deletes, no legacy seed replay, no new user/role, no trip/location data changes.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
-- BEGIN LIVE TARGET GUARD
IF COALESCE(TRY_CONVERT(int,SERVERPROPERTY(N'EngineEdition')),0)<>5
 OR DB_NAME()<>N'db-fieldvisit-pathb-uat'
 OR CONVERT(nvarchar(256),SERVERPROPERTY(N'ServerName'))<>N'sql-fieldvisit-jpe-pathb-uat'
 THROW 59800,N'EXACT_PATHB_TARGET_REQUIRED',1;
IF NOT EXISTS(SELECT 1 FROM sys.dm_exec_connections WHERE session_id=@@SPID AND encrypt_option=N'TRUE')
 THROW 59801,N'TLS_REQUIRED',1;
IF NOT EXISTS(SELECT 1 FROM sys.database_principals WHERE name=N'api-fieldvisit-pathb-uat_pathb'
 AND type=N'E' AND authentication_type_desc=N'EXTERNAL' AND DATALENGTH(sid)=16)
 THROW 59802,N'EXISTING_API_IDENTITY_REQUIRED',1;
-- END LIVE TARGET GUARD
IF @@TRANCOUNT<>0 OR (2 & @@OPTIONS)=2
 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL'),0)<>1
 THROW 59803,N'OWNER_AUTHORITY_REQUIRED',1;
DECLARE @pid int=DATABASE_PRINCIPAL_ID(N'api-fieldvisit-pathb-uat_pathb');
IF @pid IS NULL OR EXISTS(SELECT 1 FROM sys.database_role_members WHERE member_principal_id=@pid)
 THROW 59804,N'API_ROLE_BOUNDARY_CHANGED',1;
DECLARE @need TABLE(id int IDENTITY PRIMARY KEY,t sysname,p sysname,c sysname NULL,missing bit NOT NULL DEFAULT(0));
INSERT @need(t,p,c) VALUES
(N'Users',N'SELECT',NULL),
(N'UserIdentityProfiles',N'SELECT',NULL),
(N'Employments',N'SELECT',NULL),
(N'EmploymentStatusPeriods',N'SELECT',NULL),
(N'UserRoleAssignments',N'SELECT',NULL),
(N'Roles',N'SELECT',NULL),
(N'UserRoles',N'SELECT',NULL),
(N'UserTeamScopes',N'SELECT',NULL),
(N'UserTeamAssignments',N'SELECT',NULL),
(N'TeamMemberships',N'SELECT',NULL),
(N'TeamLeaderAssignments',N'SELECT',NULL),
(N'TeamLeaderDelegations',N'SELECT',NULL),
(N'Teams',N'SELECT',NULL),
(N'SchemaVersions',N'SELECT',NULL),
(N'Locations',N'SELECT',NULL),
(N'ChangeRequests',N'SELECT',NULL),
(N'ChangeRequestEvents',N'SELECT',NULL),
(N'ChangeRequestLeaderReviews',N'SELECT',NULL),
(N'LocationPublicationStages',N'SELECT',NULL),
(N'LocationPublishedVersions',N'SELECT',NULL),
(N'LocationCreateReservations',N'SELECT',NULL),
(N'Locations',N'INSERT',NULL),
(N'ChangeRequests',N'INSERT',NULL),
(N'ChangeRequestEvents',N'INSERT',NULL),
(N'ChangeRequestLeaderReviews',N'INSERT',NULL),
(N'LocationPublicationStages',N'INSERT',NULL),
(N'LocationPublishedVersions',N'INSERT',NULL),
(N'LocationCreateReservations',N'INSERT',NULL),
(N'AuditLogs',N'INSERT',NULL),
(N'Locations',N'UPDATE',N'LocationName'),
(N'Locations',N'UPDATE',N'City'),
(N'Locations',N'UPDATE',N'District'),
(N'Locations',N'UPDATE',N'Address'),
(N'Locations',N'UPDATE',N'PlusCode'),
(N'Locations',N'UPDATE',N'TaxId'),
(N'Locations',N'UPDATE',N'MasterNote'),
(N'Locations',N'UPDATE',N'ApprovalStatus'),
(N'Locations',N'UPDATE',N'IsActive'),
(N'Locations',N'UPDATE',N'Latitude'),
(N'Locations',N'UPDATE',N'Longitude'),
(N'Locations',N'UPDATE',N'GeocodingStatus'),
(N'Locations',N'UPDATE',N'GeocodedAt'),
(N'Locations',N'UPDATE',N'SelectedGeocodingAttemptId'),
(N'Locations',N'UPDATE',N'UpdatedAt'),
(N'ChangeRequests',N'UPDATE',N'Status'),
(N'ChangeRequests',N'UPDATE',N'ReviewedByUserId'),
(N'ChangeRequests',N'UPDATE',N'ReviewedAt'),
(N'ChangeRequests',N'UPDATE',N'AppliedAt'),
(N'ChangeRequests',N'UPDATE',N'ReviewReason'),
(N'ChangeRequestLeaderReviews',N'UPDATE',N'Status'),
(N'ChangeRequestLeaderReviews',N'UPDATE',N'ReviewerUserId'),
(N'ChangeRequestLeaderReviews',N'UPDATE',N'ReviewedAt'),
(N'ChangeRequestLeaderReviews',N'UPDATE',N'Reason'),
(N'ChangeRequestLeaderReviews',N'UPDATE',N'DecisionKey'),
(N'ChangeRequestLeaderReviews',N'UPDATE',N'EvidenceJson'),
(N'LocationPublicationStages',N'UPDATE',N'StageStatus'),
(N'LocationPublicationStages',N'UPDATE',N'GeocodeStatus'),
(N'LocationPublicationStages',N'UPDATE',N'GeocodeAddressSha256'),
(N'LocationPublicationStages',N'UPDATE',N'Latitude'),
(N'LocationPublicationStages',N'UPDATE',N'Longitude'),
(N'LocationPublicationStages',N'UPDATE',N'DuplicateReviewStatus'),
(N'LocationPublicationStages',N'UPDATE',N'DuplicateEvidenceSha256'),
(N'LocationPublicationStages',N'UPDATE',N'UpdatedAt'),
(N'LocationPublishedVersions',N'UPDATE',N'IsCurrent'),
(N'LocationCreateReservations',N'UPDATE',N'ReservationStatus'),
(N'LocationCreateReservations',N'UPDATE',N'UpdatedAt'),
(N'LocationMergeDetails',N'SELECT',N'ChangeRequestId'),
(N'LocationPublicationStages',N'VIEW DEFINITION',NULL),
(N'LocationPublishedVersions',N'VIEW DEFINITION',NULL);
IF EXISTS(SELECT 1 FROM @need n WHERE OBJECT_ID(N'dbo.'+n.t,N'U') IS NULL
 OR (n.c IS NOT NULL AND COL_LENGTH(N'dbo.'+n.t,n.c) IS NULL))
 THROW 59805,N'EXISTING_FORMAL_LOCATION_SCHEMA_REQUIRED',1;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-CREATE-LOWRISK')
 OR NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-LOWRISK-DIRECT')
 THROW 59806,N'EXISTING_CANONICAL_MARKERS_REQUIRED',1;
-- Never override a relevant DENY, including SQL Server's column-grant exception.
IF EXISTS(SELECT 1 FROM sys.database_permissions p
 JOIN @need n ON p.permission_name IN(n.p,N'CONTROL')
 WHERE p.grantee_principal_id IN(@pid,DATABASE_PRINCIPAL_ID(N'public')) AND p.state=N'D'
 AND (p.class=0 OR (p.class=3 AND p.major_id=SCHEMA_ID(N'dbo'))
 OR (p.class=1 AND p.major_id=OBJECT_ID(N'dbo.'+n.t))))
 THROW 59807,N'RELEVANT_DENY_PRESERVED',1;
DECLARE @org int,@team int,@v int,@l int,@a int,@today date=CONVERT(date,DATEADD(minute,480,SYSUTCDATETIME())),
 @now datetime2(3)=SYSUTCDATETIME(),@links int=0,@projections int=0,@appointments int=0,@grants int=0,@impersonating bit=0;
BEGIN TRY
 SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
 BEGIN TRANSACTION;
 SELECT @org=OrganizationId FROM dbo.Organizations WHERE OrganizationCode=N'UAT' AND OrganizationName=N'Field Visit Path B UAT' AND IsActive=1
 AND (EffectiveFrom IS NULL OR EffectiveFrom<=@today) AND (EffectiveTo IS NULL OR EffectiveTo>=@today);
 SELECT @team=TeamId FROM dbo.Teams WHERE OrganizationId=@org AND TeamCode=N'N01' AND TeamName=N'Path B Test Team' AND IsActive=1
 AND (EffectiveFrom IS NULL OR EffectiveFrom<=@today) AND (EffectiveTo IS NULL OR EffectiveTo>=@today);
 SELECT @v=UserId FROM dbo.Users WHERE OrganizationId=@org AND TeamId=@team AND EmployeeNo=N'pilotv01' AND DisplayName=N'Path B UAT Visitor' AND IsActive=1;
 SELECT @l=UserId FROM dbo.Users WHERE OrganizationId=@org AND TeamId=@team AND EmployeeNo=N'pilotl01' AND DisplayName=N'Path B UAT Leader' AND IsActive=1;
 SELECT @a=UserId FROM dbo.Users WHERE OrganizationId=@org AND TeamId=@team AND EmployeeNo=N'pilota01' AND DisplayName=N'Path B UAT Admin' AND IsActive=1;
 IF @org IS NULL OR @team IS NULL OR @v IS NULL OR @l IS NULL OR @a IS NULL
 OR (SELECT COUNT(*) FROM dbo.Users WHERE EmployeeNo IN(N'pilotv01',N'pilotl01',N'pilota01'))<>3
 THROW 59808,N'EXACT_EXISTING_SYNTHETIC_ACCOUNTS_REQUIRED',1;
 DECLARE @actors TABLE(id int PRIMARY KEY,code sysname,rolecode sysname,emp bigint NULL);
 INSERT @actors VALUES(@v,N'pilotv01',N'visitor',NULL),(@l,N'pilotl01',N'leader',NULL),(@a,N'pilota01',N'admin',NULL);
 DECLARE @uid int,@code sysname,@rolecode sysname,@rid int,@emp bigint,@person bigint,@since date;
 DECLARE actor CURSOR LOCAL FAST_FORWARD FOR SELECT id,code,rolecode FROM @actors ORDER BY id;
 OPEN actor; FETCH NEXT FROM actor INTO @uid,@code,@rolecode;
 WHILE @@FETCH_STATUS=0
 BEGIN
  SET @rid=NULL;SET @emp=NULL;SET @person=NULL;SET @since=NULL;
  SELECT @rid=r.RoleId FROM dbo.Roles r JOIN dbo.UserRoleAssignments a ON a.RoleId=r.RoleId
   WHERE a.UserId=@uid AND r.RoleCode=@rolecode AND r.IsActive=1 AND a.EffectiveFrom<=@today AND (a.EffectiveTo IS NULL OR a.EffectiveTo>=@today);
  IF @rid IS NULL OR EXISTS(SELECT 1 FROM dbo.UserRoleAssignments WHERE UserId=@uid AND RoleId<>@rid AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today))
   OR NOT EXISTS(SELECT 1 FROM dbo.UserTeamAssignments WHERE UserId=@uid AND TeamId=@team AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today))
   OR (SELECT COUNT(*) FROM dbo.UserIdentityProfiles WHERE UserId=@uid AND UserType=N'Internal' AND IdentityProvider=N'Demo' AND UserCode=@code)<>1
   THROW 59809,N'EXISTING_ROLE_TEAM_IDENTITY_REQUIRED',1;
  SELECT @emp=EmploymentId FROM dbo.UserIdentityProfiles WHERE UserId=@uid;
  IF @emp IS NULL
  BEGIN
   -- Visitor linkage previously passed; do not recreate it. Only the two
   -- still-unlinked synthetic reviewers may receive the existing HR relation.
   IF @uid=@v OR EXISTS(SELECT 1 FROM dbo.Persons WHERE LegacyUserId=@uid)
    OR EXISTS(SELECT 1 FROM dbo.Employments WHERE LegacyUserId=@uid OR (OrganizationId=@org AND EmployeeNo=@code))
    OR (SELECT COUNT(*) FROM dbo.UserEmploymentPeriods WHERE UserId=@uid AND EffectiveFrom<=@today
      AND (EffectiveTo IS NULL OR EffectiveTo>=@today))<>1
    THROW 59810,N'HR_LINK_CONFLICT_OR_VISITOR_LINK_MISSING',1;
   SELECT @since=EffectiveFrom FROM dbo.UserEmploymentPeriods WHERE UserId=@uid AND EmploymentStatus=N'Active'
    AND SourceType=N'Synthetic' AND SourceReference=N'PathB-UAT-ThreeRole-v1'
    AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today);
   IF @since IS NULL THROW 59811,N'EXISTING_ACTIVE_SYNTHETIC_HR_REQUIRED',1;
   INSERT dbo.Persons(DisplayName,LegacyUserId,CreatedAt) SELECT DisplayName,UserId,@now FROM dbo.Users WHERE UserId=@uid;
   SET @person=CONVERT(bigint,SCOPE_IDENTITY());
   INSERT dbo.Employments(PersonId,OrganizationId,EmployeeNo,HireDate,LegacyUserId,SourceType,SourceReference,CreatedAt)
    VALUES(@person,@org,@code,@since,@uid,N'Synthetic',N'PathB-FormalLocation-UAT',@now);
   SET @emp=CONVERT(bigint,SCOPE_IDENTITY());
   INSERT dbo.EmploymentStatusPeriods(EmploymentId,EmploymentStatus,EffectiveFrom,EffectiveTo,SourceType,SourceReference,CreatedAt)
    SELECT @emp,EmploymentStatus,EffectiveFrom,EffectiveTo,N'Synthetic',N'PathB-FormalLocation-UAT',@now
    FROM dbo.UserEmploymentPeriods WHERE UserId=@uid AND EffectiveFrom=@since AND EmploymentStatus=N'Active';
   IF @@ROWCOUNT<>1 THROW 59812,N'HR_STATUS_COPY_COUNT',1;
   UPDATE dbo.UserIdentityProfiles SET EmploymentId=@emp,UpdatedAt=@now WHERE UserId=@uid AND EmploymentId IS NULL;
   IF @@ROWCOUNT<>1 THROW 59813,N'HR_LINK_COUNT',1;
   SET @links+=1;
  END
  IF NOT EXISTS(SELECT 1 FROM dbo.Employments WHERE EmploymentId=@emp AND LegacyUserId=@uid AND OrganizationId=@org
   AND (HireDate IS NULL OR HireDate<=@today) AND (TerminationDate IS NULL OR TerminationDate>@today))
   OR (SELECT COUNT(*) FROM dbo.EmploymentStatusPeriods WHERE EmploymentId=@emp AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today))<>1
   OR NOT EXISTS(SELECT 1 FROM dbo.EmploymentStatusPeriods WHERE EmploymentId=@emp AND EmploymentStatus=N'Active'
   AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today))
   THROW 59814,N'CURRENT_EMPLOYMENT_REQUIRED',1;
  -- Only project a currently assigned role/team into its legacy compatibility
  -- record; never reactivate revoked scope or create an additional role.
  IF NOT EXISTS(SELECT 1 FROM dbo.UserRoles WHERE UserId=@uid AND RoleId=@rid)
  BEGIN
   IF EXISTS(SELECT 1 FROM dbo.UserRoles WHERE UserId=@uid) THROW 59815,N'ROLE_PROJECTION_CONFLICT',1;
   INSERT dbo.UserRoles(UserId,RoleId,AssignedAt) VALUES(@uid,@rid,@now);
   SET @projections+=1;
  END
  IF NOT EXISTS(SELECT 1 FROM dbo.UserTeamScopes WHERE UserId=@uid AND TeamId=@team AND IsActive=1)
  BEGIN
   IF EXISTS(SELECT 1 FROM dbo.UserTeamScopes WHERE UserId=@uid) THROW 59816,N'TEAM_PROJECTION_CONFLICT',1;
   INSERT dbo.UserTeamScopes(UserId,TeamId,IsPrimary,IsActive,AssignedAt) VALUES(@uid,@team,1,1,@now);
   SET @projections+=1;
  END
  IF @uid=@v AND NOT EXISTS(SELECT 1 FROM dbo.TeamMemberships WHERE EmploymentId=@emp AND TeamId=@team
    AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today))
   THROW 59817,N'EXISTING_VISITOR_MEMBERSHIP_REQUIRED',1;
  IF @uid=@l AND NOT EXISTS(SELECT 1 FROM dbo.TeamLeaderAssignments WHERE EmploymentId=@emp AND TeamId=@team
    AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today))
  BEGIN
   IF EXISTS(SELECT 1 FROM dbo.TeamLeaderAssignments WHERE (TeamId=@team AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today))
    OR (EmploymentId=@emp AND TeamId=@team)) THROW 59818,N'LEADER_APPOINTMENT_CONFLICT',1;
   INSERT dbo.TeamLeaderAssignments(TeamId,EmploymentId,EffectiveFrom,CreatedAt) VALUES(@team,@emp,@today,@now);
   SET @appointments+=1;
  END
  UPDATE @actors SET emp=@emp WHERE id=@uid;
  FETCH NEXT FROM actor INTO @uid,@code,@rolecode;
 END
 CLOSE actor; DEALLOCATE actor;
 -- Only missing effective privileges are granted; existing ones stay intact.
 EXECUTE AS USER=N'api-fieldvisit-pathb-uat_pathb';
 SET @impersonating=1;
 UPDATE @need SET missing=CASE WHEN c IS NULL THEN
   CASE WHEN COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+t,N'OBJECT',p),0)=1 THEN 0 ELSE 1 END
  ELSE CASE WHEN COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+t,N'OBJECT',p,c,N'COLUMN'),0)=1 THEN 0 ELSE 1 END END;
 REVERT; SET @impersonating=0;
 DECLARE @t sysname,@p sysname,@c sysname,@cmd nvarchar(600);
 DECLARE perms CURSOR LOCAL FAST_FORWARD FOR SELECT t,p,c FROM @need WHERE missing=1 ORDER BY id;
 OPEN perms; FETCH NEXT FROM perms INTO @t,@p,@c;
 WHILE @@FETCH_STATUS=0
 BEGIN
  SET @cmd=N'GRANT '+@p+N' ON OBJECT::dbo.'+QUOTENAME(@t)+CASE WHEN @c IS NULL THEN N'' ELSE N' ('+QUOTENAME(@c)+N')' END+N' TO [api-fieldvisit-pathb-uat_pathb];';
  EXEC sys.sp_executesql @cmd;
  SET @grants+=1;
  FETCH NEXT FROM perms INTO @t,@p,@c;
 END
 CLOSE perms; DEALLOCATE perms;
 EXECUTE AS USER=N'api-fieldvisit-pathb-uat_pathb';
 SET @impersonating=1;
 IF EXISTS(SELECT 1 FROM @need WHERE (c IS NULL AND COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+t,N'OBJECT',p),0)<>1)
 OR (c IS NOT NULL AND COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+t,N'OBJECT',p,c,N'COLUMN'),0)<>1))
  THROW 59819,N'EFFECTIVE_RIGHTS_NOT_COMPLETE',1;
 IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.LocationPublishedVersions',N'OBJECT',N'UPDATE',N'PublishedJson',N'COLUMN'),0)=1
 OR COALESCE(HAS_PERMS_BY_NAME(N'dbo.LocationPublishedVersions',N'OBJECT',N'DELETE'),0)=1
 OR COALESCE(HAS_PERMS_BY_NAME(N'dbo.ChangeRequestEvents',N'OBJECT',N'UPDATE'),0)=1
 OR COALESCE(HAS_PERMS_BY_NAME(N'dbo.ChangeRequestEvents',N'OBJECT',N'DELETE'),0)=1
 OR COALESCE(HAS_PERMS_BY_NAME(N'dbo.AuditLogs',N'OBJECT',N'SELECT'),0)=1
  THROW 59820,N'PROTECTED_HISTORY_PERMISSION_BOUNDARY',1;
 REVERT;SET @impersonating=0;
 COMMIT TRANSACTION;
 SELECT N'PATHB_FORMAL_LOCATION_PREREQUISITES' AS Evidence,N'PASS_SCOPED_PREREQUISITES_ONLY' AS State,
  @links AS ReviewerHrLinksAdded,@projections AS CompatibilityProjectionsAdded,@appointments AS LeaderAppointmentsAdded,@grants AS MissingGrantsAdded;
END TRY
BEGIN CATCH
 IF @impersonating=1 REVERT;
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
