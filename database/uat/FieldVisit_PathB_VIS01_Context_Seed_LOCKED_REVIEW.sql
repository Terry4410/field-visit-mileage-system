-- FIELD VISIT PATH B: VIS01 SYNTHETIC TRIP-CONTEXT SEED — LOCKED REVIEW ONLY
-- NOT EXECUTABLE. The first statement below deliberately prevents any write.
-- DO NOT REMOVE THE LOCK OR RUN IN AZURE. Separate exact-file review and Owner approval required.
THROW 59500, 'LOCKED_REVIEW_ONLY_NOT_AUTHORIZED_FOR_SQL_WRITE', 1;
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Independent, additive synthetic UAT data only. Never replay 20-row ThreeRole Seed.
IF @@TRANCOUNT<>0 OR (2 & @@OPTIONS)=2 THROW 59501, 'TRANSACTION_MODE_HOLD', 1;
IF COALESCE(TRY_CONVERT(int, SERVERPROPERTY('EngineEdition')),0)<>5
 OR DB_NAME()<>N'db-fieldvisit-pathb-uat'
 OR CONVERT(nvarchar(256),SERVERPROPERTY('ServerName'))<>N'sql-fieldvisit-jpe-pathb-uat'
 THROW 59502, 'EXACT_TARGET_HOLD', 1;
IF NOT EXISTS(SELECT 1 FROM sys.dm_exec_connections WHERE session_id=@@SPID AND encrypt_option=N'TRUE')
 THROW 59503, 'TLS_HOLD', 1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION'),0)<>1
 THROW 59504, 'METADATA_HOLD', 1;
IF EXISTS(SELECT 1 FROM (VALUES
(N'Persons'),(N'Employments'),(N'EmploymentStatusPeriods'),(N'TeamMemberships'),
(N'Centers'),(N'TeamCenterAssignments'),(N'DeploymentSites'),(N'Locations'),
(N'DeploymentSiteLocationAssignments'),(N'TeamDeploymentSiteAssignments'),
(N'EmploymentDeploymentSiteAssignments'),(N'Projects'),(N'VisitTypes'),(N'UserIdentityProfiles')) AS t(n)
WHERE OBJECT_ID(N'dbo.'+t.n,N'U') IS NULL)
 THROW 59505, 'REQUIRED_OBJECT_MISSING_HOLD', 1;
IF EXISTS(SELECT 1 FROM (VALUES
(N'Persons'),(N'Employments'),(N'EmploymentStatusPeriods'),(N'TeamMemberships'),
(N'Centers'),(N'TeamCenterAssignments'),(N'DeploymentSites'),(N'Locations'),
(N'DeploymentSiteLocationAssignments'),(N'TeamDeploymentSiteAssignments'),
(N'EmploymentDeploymentSiteAssignments'),(N'Projects'),(N'VisitTypes'),(N'UserIdentityProfiles')) AS t(n)
WHERE COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+t.n,'OBJECT','SELECT'),0)<>1)
 THROW 59506, 'OWNER_SOURCE_VISIBILITY_HOLD', 1;

DECLARE @now datetime2(3)=SYSUTCDATETIME(), @since date=CONVERT(date,'20200101',112);
DECLARE @today date=CONVERT(date,DATEADD(MINUTE,480,SYSUTCDATETIME()));
DECLARE @org int, @team int, @visitor int, @admin int;
SELECT @org=OrganizationId FROM dbo.Organizations WHERE OrganizationCode=N'UAT';
SELECT @team=TeamId FROM dbo.Teams WHERE OrganizationId=@org AND TeamCode=N'N01';
SELECT @visitor=UserId FROM dbo.Users WHERE EmployeeNo=N'pilotv01' AND OrganizationId=@org AND TeamId=@team AND IsActive=1;
SELECT @admin=UserId FROM dbo.Users WHERE EmployeeNo=N'pilota01' AND OrganizationId=@org AND TeamId=@team AND IsActive=1;
IF @org IS NULL OR @team IS NULL OR @visitor IS NULL OR @admin IS NULL
 OR (SELECT COUNT(*) FROM dbo.Organizations WHERE OrganizationCode=N'UAT' AND IsActive=1)<>1
 OR (SELECT COUNT(*) FROM dbo.Teams WHERE OrganizationId=@org AND TeamCode=N'N01' AND IsActive=1)<>1
 OR (SELECT COUNT(*) FROM dbo.UserIdentityProfiles WHERE UserId=@visitor AND UserType=N'Internal' AND IdentityProvider=N'Demo' AND EmploymentId IS NULL)<>1
 OR (SELECT COUNT(*) FROM dbo.UserEmploymentPeriods WHERE UserId=@visitor AND EmploymentStatus=N'Active' AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today))<>1
 THROW 59507, 'EXACT_EXISTING_SEED_HOLD', 1;

-- Reject any conflicting pre-existing synthetic namespace; never overwrite human/master data.
IF EXISTS(SELECT 1 FROM dbo.Persons WHERE LegacyUserId=@visitor)
 OR EXISTS(SELECT 1 FROM dbo.Employments WHERE LegacyUserId=@visitor OR (OrganizationId=@org AND EmployeeNo=N'pilotv01'))
 OR EXISTS(SELECT 1 FROM dbo.Centers WHERE OrganizationId=@org AND CenterCode=N'PBV01-CENTER')
 OR EXISTS(SELECT 1 FROM dbo.Locations WHERE OrganizationId=@org AND LocationCode IN(N'PBV01-START',N'PBV01-VISIT'))
 OR EXISTS(SELECT 1 FROM dbo.VisitTypes WHERE VisitTypeCode=N'PBV01-VISIT')
 OR EXISTS(SELECT 1 FROM dbo.Projects WHERE OrganizationId=@org AND ProjectCode=N'PBV01-PROJECT')
 THROW 59508, 'VIS01_NAMESPACE_COLLISION_HOLD', 1;

DECLARE @person bigint,@employment bigint,@center int,@site int,@startLoc int,@visitLoc int;
DECLARE @lock int;
BEGIN TRY
 BEGIN TRANSACTION;
 EXEC @lock=sys.sp_getapplock @Resource=N'FieldVisit.PathB.UAT.VIS01ContextSeed.v1',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;
 IF @lock<0 THROW 59509,'VIS01_CONCURRENT_OPERATION_HOLD',1;
 -- Repeat exact binding and uniqueness inside the write transaction.
 IF NOT EXISTS(SELECT 1 FROM dbo.UserIdentityProfiles WITH(UPDLOCK,HOLDLOCK) WHERE UserId=@visitor AND EmploymentId IS NULL)
    OR EXISTS(SELECT 1 FROM dbo.Employments WITH(UPDLOCK,HOLDLOCK) WHERE LegacyUserId=@visitor OR (OrganizationId=@org AND EmployeeNo=N'pilotv01'))
    OR EXISTS(SELECT 1 FROM dbo.Centers WITH(UPDLOCK,HOLDLOCK) WHERE OrganizationId=@org AND CenterCode=N'PBV01-CENTER')
    OR EXISTS(SELECT 1 FROM dbo.Locations WITH(UPDLOCK,HOLDLOCK) WHERE OrganizationId=@org AND LocationCode IN(N'PBV01-START',N'PBV01-VISIT'))
    THROW 59510,'CONCURRENT_OR_PARTIAL_DATA_HOLD',1;

 INSERT dbo.Persons(DisplayName,LegacyUserId) VALUES(N'Path B VIS01 Synthetic Person',@visitor);
 SET @person=CONVERT(bigint,SCOPE_IDENTITY());
 INSERT dbo.Employments(PersonId,OrganizationId,EmployeeNo,HireDate,LegacyUserId,SourceType,SourceReference)
 VALUES(@person,@org,N'pilotv01',@since,@visitor,N'Synthetic',N'PathB-VIS01-Context-v1');
 SET @employment=CONVERT(bigint,SCOPE_IDENTITY());
 INSERT dbo.EmploymentStatusPeriods(EmploymentId,EmploymentStatus,EffectiveFrom,SourceType,SourceReference)
 VALUES(@employment,N'Active',@since,N'Synthetic',N'PathB-VIS01-Context-v1');
 INSERT dbo.TeamMemberships(EmploymentId,TeamId,IsPrimary,EffectiveFrom,ChangeReason)
 VALUES(@employment,@team,1,@since,N'Path B VIS01 synthetic UAT only');
 INSERT dbo.Centers(OrganizationId,CenterCode,CenterName,EffectiveFrom,IsActive)
 VALUES(@org,N'PBV01-CENTER',N'Path B VIS01 Synthetic Center',@since,1);
 SET @center=CONVERT(int,SCOPE_IDENTITY());
 INSERT dbo.TeamCenterAssignments(TeamId,CenterId,EffectiveFrom,ChangeReason)
 VALUES(@team,@center,@since,N'Path B VIS01 synthetic UAT only');
 INSERT dbo.DeploymentSites(CenterId,SiteCode,SiteName,EffectiveFrom,IsActive)
 VALUES(@center,N'PBV01-SITE',N'Path B VIS01 Synthetic Start Site',@since,1);
 SET @site=CONVERT(int,SCOPE_IDENTITY());
 -- These are explicitly synthetic demo addresses. No real business site is asserted.
 INSERT dbo.Locations(OrganizationId,TeamId,LocationCode,LocationName,LocationType,PostalCode,City,District,Address,
    PlusCode,Latitude,Longitude,IsTemporary,ApprovalStatus,GeocodingStatus,GeocodedAt,CreatedByUserId,IsActive,CreatedAt)
 VALUES(@org,@team,N'PBV01-START',N'Path B VIS01 UAT Start',N'Office',NULL,N'台北市',N'中正區',N'台北市中正區北平西路 3 號（UAT 測試）',
    NULL,25.0478000,121.5170000,0,N'Approved',N'Completed',@now,@admin,1,@now);
 SET @startLoc=CONVERT(int,SCOPE_IDENTITY());
 INSERT dbo.Locations(OrganizationId,TeamId,LocationCode,LocationName,LocationType,PostalCode,City,District,Address,
    PlusCode,Latitude,Longitude,IsTemporary,ApprovalStatus,GeocodingStatus,GeocodedAt,CreatedByUserId,IsActive,CreatedAt)
 VALUES(@org,@team,N'PBV01-VISIT',N'Path B VIS01 UAT Visit',N'Customer',NULL,N'台北市',N'信義區',N'台北市信義區市府路 1 號（UAT 測試）',
    NULL,25.0375000,121.5637000,0,N'Approved',N'Completed',@now,@admin,1,@now);
 SET @visitLoc=CONVERT(int,SCOPE_IDENTITY());
 INSERT dbo.DeploymentSiteLocationAssignments(DeploymentSiteId,LocationId,EffectiveFrom,ChangeReason)
 VALUES(@site,@startLoc,@since,N'Path B VIS01 synthetic UAT only');
 INSERT dbo.TeamDeploymentSiteAssignments(TeamId,DeploymentSiteId,EffectiveFrom)
 VALUES(@team,@site,@since);
 INSERT dbo.EmploymentDeploymentSiteAssignments(EmploymentId,DeploymentSiteId,IsPrimary,EffectiveFrom)
 VALUES(@employment,@site,1,@since);
 INSERT dbo.VisitTypes(VisitTypeCode,VisitTypeName,Description,SortOrder,IsActive,CreatedAt)
 VALUES(N'PBV01-VISIT',N'Path B VIS01 Test Visit',N'Synthetic UAT only',950,1,@now);
 INSERT dbo.Projects(OrganizationId,TeamId,ProjectCode,ProjectName,Description,LocationMode,StartDate,IsActive,CreatedAt)
 VALUES(@org,@team,N'PBV01-PROJECT',N'Path B VIS01 Synthetic Project',N'Synthetic UAT only',N'SelfMaintained',@since,1,@now);
 UPDATE dbo.UserIdentityProfiles SET EmploymentId=@employment,UpdatedAt=@now WHERE UserId=@visitor AND EmploymentId IS NULL;
 IF @@ROWCOUNT<>1 THROW 59511,'IDENTITY_LINK_UPDATE_HOLD',1;
 -- Readback proves this exact context chain for the current Taiwan business date.
 IF (SELECT COUNT(*) FROM dbo.UserIdentityProfiles p JOIN dbo.Employments e ON e.EmploymentId=p.EmploymentId
   JOIN dbo.EmploymentStatusPeriods ep ON ep.EmploymentId=e.EmploymentId AND ep.EmploymentStatus=N'Active' AND ep.EffectiveFrom<=@today AND (ep.EffectiveTo IS NULL OR ep.EffectiveTo>=@today)
   JOIN dbo.TeamMemberships tm ON tm.EmploymentId=e.EmploymentId AND tm.TeamId=@team AND tm.IsPrimary=1 AND tm.EffectiveFrom<=@today AND (tm.EffectiveTo IS NULL OR tm.EffectiveTo>=@today)
   JOIN dbo.TeamCenterAssignments tc ON tc.TeamId=tm.TeamId AND tc.CenterId=@center AND tc.EffectiveFrom<=@today AND (tc.EffectiveTo IS NULL OR tc.EffectiveTo>=@today)
   JOIN dbo.EmploymentDeploymentSiteAssignments ed ON ed.EmploymentId=e.EmploymentId AND ed.DeploymentSiteId=@site AND ed.IsPrimary=1 AND ed.EffectiveFrom<=@today AND (ed.EffectiveTo IS NULL OR ed.EffectiveTo>=@today)
   JOIN dbo.TeamDeploymentSiteAssignments td ON td.TeamId=@team AND td.DeploymentSiteId=@site AND td.EffectiveFrom<=@today AND (td.EffectiveTo IS NULL OR td.EffectiveTo>=@today)
   JOIN dbo.DeploymentSiteLocationAssignments dl ON dl.DeploymentSiteId=@site AND dl.LocationId=@startLoc AND dl.EffectiveFrom<=@today AND (dl.EffectiveTo IS NULL OR dl.EffectiveTo>=@today)
   JOIN dbo.Locations loc ON loc.LocationId=dl.LocationId AND loc.OrganizationId=@org AND loc.IsActive=1 AND loc.ApprovalStatus=N'Approved'
   WHERE p.UserId=@visitor AND e.OrganizationId=@org)<>1
   THROW 59512,'VIS01_CONTEXT_CHAIN_POSTCHECK_HOLD',1;
 COMMIT TRANSACTION;
 SELECT N'PATHB_VIS01_CONTEXT_SEED' AS Evidence, N'CATALOG_CONTEXT_CHAIN_ONLY' AS State, 1 AS VisitorEmploymentLinked,
   1 AS ActiveEmployment,1 AS PrimaryTeam,1 AS Center,1 AS EligibleDeploymentSite,2 AS ApprovedLocations;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
