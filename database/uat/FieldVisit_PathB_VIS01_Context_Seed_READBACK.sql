-- Field Visit Path B VIS01 / Independent READ-ONLY one-result context chain
-- This query does not write or grant. A PASS is catalog evidence, not an app runtime UAT PASS.
SET NOCOUNT ON;
IF COALESCE(TRY_CONVERT(int,SERVERPROPERTY('EngineEdition')),0)<>5
 OR DB_NAME()<>N'db-fieldvisit-pathb-uat'
 OR CONVERT(nvarchar(256),SERVERPROPERTY('ServerName'))<>N'sql-fieldvisit-jpe-pathb-uat'
 THROW 59550,'VIS01_READBACK_EXACT_TARGET_HOLD',1;
IF NOT EXISTS(SELECT 1 FROM sys.dm_exec_connections WHERE session_id=@@SPID AND encrypt_option=N'TRUE')
 THROW 59551,'VIS01_READBACK_ENCRYPTION_HOLD',1;
DECLARE @today date=CONVERT(date,DATEADD(MINUTE,480,SYSUTCDATETIME()));
DECLARE @org int,@team int,@visitor int,@employment bigint,@center int,@site int,@location int;
SELECT @org=OrganizationId FROM dbo.Organizations WHERE OrganizationCode=N'UAT';
SELECT @team=TeamId FROM dbo.Teams WHERE OrganizationId=@org AND TeamCode=N'N01';
SELECT @visitor=UserId FROM dbo.Users WHERE OrganizationId=@org AND TeamId=@team AND EmployeeNo=N'pilotv01';
SELECT @employment=EmploymentId FROM dbo.UserIdentityProfiles WHERE UserId=@visitor;
SELECT @center=CenterId FROM dbo.Centers WHERE OrganizationId=@org AND CenterCode=N'PBV01-CENTER';
SELECT @site=DeploymentSiteId FROM dbo.DeploymentSites WHERE CenterId=@center AND SiteCode=N'PBV01-SITE';
SELECT @location=LocationId FROM dbo.Locations WHERE OrganizationId=@org AND LocationCode=N'PBV01-START';
DECLARE @linked int=(SELECT COUNT(*) FROM dbo.Employments WHERE EmploymentId=@employment AND OrganizationId=@org AND LegacyUserId=@visitor AND EmployeeNo=N'pilotv01');
DECLARE @active int=(SELECT COUNT(*) FROM dbo.EmploymentStatusPeriods WHERE EmploymentId=@employment AND EmploymentStatus=N'Active' AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today));
DECLARE @teamCount int=(SELECT COUNT(*) FROM dbo.TeamMemberships WHERE EmploymentId=@employment AND TeamId=@team AND IsPrimary=1 AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today));
DECLARE @centerCount int=(SELECT COUNT(*) FROM dbo.TeamCenterAssignments WHERE TeamId=@team AND CenterId=@center AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today));
DECLARE @siteCount int=(SELECT COUNT(*) FROM dbo.EmploymentDeploymentSiteAssignments WHERE EmploymentId=@employment AND DeploymentSiteId=@site AND IsPrimary=1 AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today));
DECLARE @teamSiteCount int=(SELECT COUNT(*) FROM dbo.TeamDeploymentSiteAssignments WHERE TeamId=@team AND DeploymentSiteId=@site AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today));
DECLARE @siteLocationCount int=(SELECT COUNT(*) FROM dbo.DeploymentSiteLocationAssignments WHERE DeploymentSiteId=@site AND LocationId=@location AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today));
DECLARE @locationCount int=(SELECT COUNT(*) FROM dbo.Locations WHERE OrganizationId=@org AND LocationCode IN(N'PBV01-START',N'PBV01-VISIT') AND ApprovalStatus=N'Approved' AND IsActive=1);
DECLARE @typeCount int=(SELECT COUNT(*) FROM dbo.VisitTypes WHERE VisitTypeCode=N'PBV01-VISIT' AND IsActive=1);
DECLARE @projectCount int=(SELECT COUNT(*) FROM dbo.Projects WHERE OrganizationId=@org AND TeamId=@team AND ProjectCode=N'PBV01-PROJECT' AND IsActive=1);
SELECT N'PATHB_VIS01_CONTEXT_INDEPENDENT_READBACK' AS Evidence,
       @linked AS VisitorEmploymentLink,@active AS ActiveStatusPeriods,@teamCount AS PrimaryTeamMembership,
       @centerCount AS TeamCenter,@siteCount AS EmploymentSite,@teamSiteCount AS TeamSite,
       @siteLocationCount AS SiteLocation,@locationCount AS ApprovedLocations,
       @typeCount AS VisitTypes,@projectCount AS Projects,
       CASE WHEN @linked=1 AND @active=1 AND @teamCount=1 AND @centerCount=1
        AND @siteCount=1 AND @teamSiteCount=1 AND @siteLocationCount=1 AND @locationCount=2
        AND @typeCount=1 AND @projectCount=1 THEN N'PASS_CATALOG_ONLY_NOT_BUSINESS_UAT'
        ELSE N'HOLD' END AS State;
