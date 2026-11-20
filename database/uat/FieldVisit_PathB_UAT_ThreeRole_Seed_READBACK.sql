-- Read-only, sanitized exact-scope readback. It does not establish browser login.
SET NOCOUNT ON;
DECLARE @isDisposableCi bit = CASE
    WHEN COALESCE(TRY_CONVERT(int, SERVERPROPERTY(N'EngineEdition')), 0) = 3
     AND DB_NAME() LIKE N'PathBSeedCI[_]%'
     AND TRY_CONVERT(nvarchar(80), SESSION_CONTEXT(N'PATHB_DISPOSABLE_CI')) =
         N'd6c220877b9976b6478d85b5171d4e06293595ae'
    THEN 1 ELSE 0 END;

IF @isDisposableCi = 0
BEGIN
    IF COALESCE(TRY_CONVERT(int, SERVERPROPERTY(N'EngineEdition')), 0) <> 5
       OR COALESCE(CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')), N'')
          COLLATE Latin1_General_100_BIN2 <> N'sql-fieldvisit-jpe-pathb-uat'
       OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> N'db-fieldvisit-pathb-uat'
        THROW 58200, N'PATHB_SEED_READBACK_EXACT_TARGET_HOLD', 1;
    IF NOT EXISTS (SELECT 1 FROM sys.dm_exec_connections
                   WHERE session_id = @@SPID AND encrypt_option = N'TRUE')
        THROW 58201, N'PATHB_SEED_READBACK_TLS_NOT_OBSERVED_HOLD', 1;
END;
DECLARE @today date=CONVERT(date,DATEADD(MINUTE,480,SYSUTCDATETIME()));
;WITH A(Account,RoleCode) AS (
 SELECT * FROM (VALUES(N'pilotv01',N'visitor'),(N'pilotl01',N'leader'),(N'pilota01',N'admin')) v(a,r)
)
SELECT N'PATHB_UAT_THREE_ROLE_SEED_READBACK' Evidence,
 COUNT(DISTINCT o.OrganizationId) Organizations,
 COUNT(DISTINCT t.TeamId) Teams,
 COUNT(DISTINCT r.RoleId) Roles,
 COUNT(DISTINCT u.UserId) Users,
 COUNT(DISTINCT ip.UserId) IdentityProfiles,
 COUNT(DISTINCT ep.UserId) EffectiveEmployment,
 COUNT(DISTINCT CASE WHEN r.RoleId IS NOT NULL THEN u.UserId END) EffectiveRoles,
 COUNT(DISTINCT ta.UserId) EffectiveTeams,
 CASE WHEN COUNT(DISTINCT o.OrganizationId)=1 AND COUNT(DISTINCT t.TeamId)=1
        AND COUNT(DISTINCT r.RoleId)=3 AND COUNT(DISTINCT u.UserId)=3 AND COUNT(DISTINCT ip.UserId)=3
        AND COUNT(DISTINCT ep.UserId)=3 AND COUNT(DISTINCT ra.UserId)=3
        AND COUNT(DISTINCT ta.UserId)=3
        AND COUNT(DISTINCT ra.UserRoleAssignmentId)=3
        AND COUNT(DISTINCT ta.UserTeamAssignmentId)=3 THEN N'PASS' ELSE N'HOLD' END State
FROM A
LEFT JOIN dbo.Users u ON u.EmployeeNo=A.Account AND u.IsActive=1
LEFT JOIN dbo.Organizations o ON o.OrganizationId=u.OrganizationId AND o.OrganizationCode=N'UAT'
 AND o.IsActive=1 AND o.EffectiveFrom<=@today AND (o.EffectiveTo IS NULL OR o.EffectiveTo>=@today)
LEFT JOIN dbo.Teams t ON t.TeamId=u.TeamId AND t.OrganizationId=o.OrganizationId
 AND t.TeamCode=N'N01' AND t.IsActive=1 AND t.EffectiveFrom<=@today
 AND (t.EffectiveTo IS NULL OR t.EffectiveTo>=@today)
LEFT JOIN dbo.UserIdentityProfiles ip ON ip.UserId=u.UserId AND ip.UserCode=A.Account
 AND ip.UserType=N'Internal' AND ip.IdentityProvider=N'Demo' AND ip.EmploymentId IS NULL
LEFT JOIN dbo.UserEmploymentPeriods ep ON ep.UserId=u.UserId AND ep.EmploymentStatus=N'Active'
 AND ep.SourceReference=N'PathB-UAT-ThreeRole-v1'
 AND ep.EffectiveFrom<=@today AND (ep.EffectiveTo IS NULL OR ep.EffectiveTo>=@today)
LEFT JOIN dbo.UserRoleAssignments ra ON ra.UserId=u.UserId AND ra.EffectiveFrom<=@today
 AND (ra.EffectiveTo IS NULL OR ra.EffectiveTo>=@today)
LEFT JOIN dbo.Roles r ON r.RoleId=ra.RoleId AND r.RoleCode=A.RoleCode AND r.IsActive=1
LEFT JOIN dbo.UserTeamAssignments ta ON ta.UserId=u.UserId AND ta.TeamId=t.TeamId
 AND ta.IsPrimary=1 AND ta.EffectiveFrom<=@today AND (ta.EffectiveTo IS NULL OR ta.EffectiveTo>=@today);
