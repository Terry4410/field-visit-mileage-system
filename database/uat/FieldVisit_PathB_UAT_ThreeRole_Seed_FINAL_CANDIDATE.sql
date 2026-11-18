-- FIELD VISIT PATH B / UAT ONLY / EXACT THREE-ROLE SEED CANDIDATE
-- Accepted application source: d6c220877b9976b6478d85b5171d4e06293595ae
-- Synthetic accounts only: pilotv01, pilotl01, pilota01.
-- Azure execution requires separate Owner approval of this exact file SHA-256.
-- Never retry after an ambiguous client outcome; use the companion readback.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

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
        THROW 58100, N'PATHB_SEED_EXACT_TARGET_HOLD', 1;
    IF NOT EXISTS (SELECT 1 FROM sys.dm_exec_connections
                   WHERE session_id = @@SPID AND encrypt_option = N'TRUE')
        THROW 58101, N'PATHB_SEED_TLS_NOT_OBSERVED_HOLD', 1;
END;

IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'VIEW DEFINITION'), 0) <> 1
    THROW 58102, N'PATHB_SEED_SCHEMA_VISIBILITY_HOLD', 1;

IF EXISTS (SELECT 1 FROM (VALUES
    (N'Organizations'),(N'Teams'),(N'Roles'),(N'Users'),
    (N'UserIdentityProfiles'),(N'UserEmploymentPeriods'),
    (N'UserRoleAssignments'),(N'UserTeamAssignments')) t(n)
    WHERE OBJECT_ID(N'dbo.' + t.n, N'U') IS NULL)
    THROW 58103, N'PATHB_SEED_REQUIRED_TABLE_HOLD', 1;

IF EXISTS (SELECT 1 FROM (VALUES
    (N'Organizations',N'OrganizationId'),(N'Organizations',N'OrganizationCode'),
    (N'Organizations',N'OrganizationName'),(N'Organizations',N'IsActive'),
    (N'Organizations',N'EffectiveFrom'),(N'Organizations',N'EffectiveTo'),
    (N'Organizations',N'CreatedAt'),
    (N'Teams',N'TeamId'),(N'Teams',N'OrganizationId'),(N'Teams',N'TeamCode'),
    (N'Teams',N'TeamName'),(N'Teams',N'IsActive'),
    (N'Teams',N'EffectiveFrom'),(N'Teams',N'EffectiveTo'),(N'Teams',N'CreatedAt'),
    (N'Roles',N'RoleId'),(N'Roles',N'RoleCode'),(N'Roles',N'RoleName'),
    (N'Roles',N'IsActive'),(N'Roles',N'CreatedAt'),
    (N'Users',N'UserId'),(N'Users',N'OrganizationId'),(N'Users',N'TeamId'),
    (N'Users',N'EmployeeNo'),(N'Users',N'DisplayName'),(N'Users',N'Email'),
    (N'Users',N'IsActive'),(N'Users',N'CreatedAt'),
    (N'UserIdentityProfiles',N'UserId'),(N'UserIdentityProfiles',N'UserType'),
    (N'UserIdentityProfiles',N'UserCode'),(N'UserIdentityProfiles',N'IdentityProvider'),
    (N'UserIdentityProfiles',N'EmploymentId'),(N'UserIdentityProfiles',N'CreatedAt'),
    (N'UserEmploymentPeriods',N'UserEmploymentPeriodId'),
    (N'UserEmploymentPeriods',N'UserId'),(N'UserEmploymentPeriods',N'EmploymentStatus'),
    (N'UserEmploymentPeriods',N'EffectiveFrom'),(N'UserEmploymentPeriods',N'EffectiveTo'),
    (N'UserEmploymentPeriods',N'SourceType'),(N'UserEmploymentPeriods',N'CreatedAt'),
    (N'UserRoleAssignments',N'UserRoleAssignmentId'),
    (N'UserRoleAssignments',N'UserId'),(N'UserRoleAssignments',N'RoleId'),
    (N'UserRoleAssignments',N'EffectiveFrom'),(N'UserRoleAssignments',N'EffectiveTo'),
    (N'UserRoleAssignments',N'CreatedAt'),
    (N'UserTeamAssignments',N'UserTeamAssignmentId'),
    (N'UserTeamAssignments',N'UserId'),(N'UserTeamAssignments',N'TeamId'),
    (N'UserTeamAssignments',N'IsPrimary'),(N'UserTeamAssignments',N'EffectiveFrom'),
    (N'UserTeamAssignments',N'EffectiveTo'),(N'UserTeamAssignments',N'CreatedAt')) req(t,c)
    WHERE NOT EXISTS (SELECT 1 FROM sys.columns s
                      WHERE s.object_id = OBJECT_ID(N'dbo.' + req.t)
                        AND s.name = req.c))
    THROW 58104, N'PATHB_SEED_REQUIRED_COLUMN_HOLD', 1;

-- Fail if a newly required, non-generated column is absent from this exact insert contract.
DECLARE @allowed TABLE(TableName sysname NOT NULL, ColumnName sysname NOT NULL,
                       PRIMARY KEY(TableName, ColumnName));
INSERT @allowed VALUES
 (N'Organizations',N'OrganizationId'),(N'Organizations',N'OrganizationCode'),
 (N'Organizations',N'OrganizationName'),(N'Organizations',N'IsActive'),
 (N'Organizations',N'EffectiveFrom'),(N'Organizations',N'EffectiveTo'),
 (N'Organizations',N'CreatedAt'),(N'Organizations',N'UpdatedAt'),
 (N'Teams',N'TeamId'),(N'Teams',N'OrganizationId'),(N'Teams',N'TeamCode'),
 (N'Teams',N'TeamName'),(N'Teams',N'IsActive'),(N'Teams',N'EffectiveFrom'),
 (N'Teams',N'EffectiveTo'),(N'Teams',N'CreatedAt'),(N'Teams',N'UpdatedAt'),
 (N'Roles',N'RoleId'),(N'Roles',N'RoleCode'),(N'Roles',N'RoleName'),
 (N'Roles',N'Description'),(N'Roles',N'IsActive'),(N'Roles',N'CreatedAt'),
 (N'Users',N'UserId'),(N'Users',N'OrganizationId'),(N'Users',N'TeamId'),
 (N'Users',N'EmployeeNo'),(N'Users',N'DisplayName'),(N'Users',N'Email'),
 (N'Users',N'EntraObjectId'),(N'Users',N'IsActive'),(N'Users',N'CreatedAt'),
 (N'Users',N'UpdatedAt'),
 (N'UserIdentityProfiles',N'UserId'),(N'UserIdentityProfiles',N'UserType'),
 (N'UserIdentityProfiles',N'UserCode'),(N'UserIdentityProfiles',N'IdentityProvider'),
 (N'UserIdentityProfiles',N'ExternalOrganization'),
 (N'UserIdentityProfiles',N'ExternalTitle'),
 (N'UserIdentityProfiles',N'AuthorizationFrom'),
 (N'UserIdentityProfiles',N'AuthorizationTo'),
 (N'UserIdentityProfiles',N'EntraTenantId'),
 (N'UserIdentityProfiles',N'EntraObjectId'),
 (N'UserIdentityProfiles',N'EmploymentId'),
 (N'UserIdentityProfiles',N'CreatedAt'),(N'UserIdentityProfiles',N'UpdatedAt'),
 (N'UserEmploymentPeriods',N'UserEmploymentPeriodId'),
 (N'UserEmploymentPeriods',N'UserId'),(N'UserEmploymentPeriods',N'EmploymentStatus'),
 (N'UserEmploymentPeriods',N'EffectiveFrom'),(N'UserEmploymentPeriods',N'EffectiveTo'),
 (N'UserEmploymentPeriods',N'SourceType'),
 (N'UserEmploymentPeriods',N'SourceReference'),
 (N'UserEmploymentPeriods',N'CreatedAt'),(N'UserEmploymentPeriods',N'UpdatedAt'),
 (N'UserRoleAssignments',N'UserRoleAssignmentId'),
 (N'UserRoleAssignments',N'UserId'),(N'UserRoleAssignments',N'RoleId'),
 (N'UserRoleAssignments',N'EffectiveFrom'),(N'UserRoleAssignments',N'EffectiveTo'),
 (N'UserRoleAssignments',N'AssignedByUserId'),(N'UserRoleAssignments',N'CreatedAt'),
 (N'UserTeamAssignments',N'UserTeamAssignmentId'),
 (N'UserTeamAssignments',N'UserId'),(N'UserTeamAssignments',N'TeamId'),
 (N'UserTeamAssignments',N'IsPrimary'),(N'UserTeamAssignments',N'EffectiveFrom'),
 (N'UserTeamAssignments',N'EffectiveTo'),(N'UserTeamAssignments',N'AssignedByUserId'),
 (N'UserTeamAssignments',N'CreatedAt');
IF EXISTS (SELECT 1 FROM sys.columns c
           JOIN sys.tables t ON t.object_id = c.object_id
           LEFT JOIN sys.default_constraints d ON d.object_id = c.default_object_id
           LEFT JOIN @allowed a ON a.TableName=t.name AND a.ColumnName=c.name
           WHERE SCHEMA_NAME(t.schema_id)=N'dbo'
             AND t.name IN (N'Organizations',N'Teams',N'Roles',N'Users',
               N'UserIdentityProfiles',N'UserEmploymentPeriods',
               N'UserRoleAssignments',N'UserTeamAssignments')
             AND c.is_nullable=0 AND c.is_identity=0 AND c.is_computed=0
             AND c.system_type_id<>189 AND d.object_id IS NULL AND a.TableName IS NULL)
    THROW 58105, N'PATHB_SEED_UNEXPECTED_REQUIRED_COLUMN_HOLD', 1;

IF EXISTS (SELECT 1 FROM sys.triggers tr
           WHERE tr.parent_id IN (OBJECT_ID(N'dbo.Organizations'),OBJECT_ID(N'dbo.Teams'),
             OBJECT_ID(N'dbo.Roles'),OBJECT_ID(N'dbo.Users'),
             OBJECT_ID(N'dbo.UserIdentityProfiles'),OBJECT_ID(N'dbo.UserEmploymentPeriods'),
             OBJECT_ID(N'dbo.UserRoleAssignments'),OBJECT_ID(N'dbo.UserTeamAssignments'))
             AND tr.is_ms_shipped=0)
    THROW 58106, N'PATHB_SEED_TARGET_TRIGGER_HOLD', 1;

IF EXISTS (SELECT 1 FROM (VALUES
    (N'Organizations'),(N'Teams'),(N'Roles'),(N'Users'),
    (N'UserIdentityProfiles'),(N'UserEmploymentPeriods'),
    (N'UserRoleAssignments'),(N'UserTeamAssignments')) t(n)
    WHERE COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+t.n,N'OBJECT',N'SELECT'),0)<>1
       OR COALESCE(HAS_PERMS_BY_NAME(N'dbo.'+t.n,N'OBJECT',N'INSERT'),0)<>1)
    THROW 58107, N'PATHB_SEED_LEAST_REQUIRED_PERMISSION_HOLD', 1;

DECLARE @now datetime2(3)=SYSUTCDATETIME();
DECLARE @since date=CONVERT(date,'20200101',112);
DECLARE @today date=CONVERT(date,DATEADD(MINUTE,480,SYSUTCDATETIME()));
DECLARE @org int,@team int,@vr int,@lr int,@ar int,@vu int,@lu int,@au int;
DECLARE @lockResult int;

BEGIN TRY
    BEGIN TRANSACTION;
    EXEC @lockResult=sys.sp_getapplock
        @Resource=N'FieldVisit.PathB.UAT.ThreeRoleSeed.v1',
        @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;
    IF @lockResult < 0
        THROW 58108, N'PATHB_SEED_CONCURRENT_EXECUTION_HOLD', 1;

    -- Collision checks are inside the same serializable transaction as every write.
    IF EXISTS(SELECT 1 FROM dbo.Organizations WITH(UPDLOCK,HOLDLOCK)
              WHERE UPPER(LTRIM(RTRIM(OrganizationCode)))=N'UAT')
       OR EXISTS(SELECT 1 FROM dbo.Teams WITH(UPDLOCK,HOLDLOCK)
                 WHERE UPPER(LTRIM(RTRIM(TeamCode)))=N'N01')
       OR EXISTS(SELECT 1 FROM dbo.Roles WITH(UPDLOCK,HOLDLOCK)
                 WHERE UPPER(LTRIM(RTRIM(RoleCode))) IN(N'VISITOR',N'LEADER',N'ADMIN'))
       OR EXISTS(SELECT 1 FROM dbo.Users WITH(UPDLOCK,HOLDLOCK)
                 WHERE UPPER(LTRIM(RTRIM(COALESCE(EmployeeNo,N'')))) IN(N'PILOTV01',N'PILOTL01',N'PILOTA01')
                    OR UPPER(LTRIM(RTRIM(COALESCE(Email,N'')))) IN(N'PILOTV01',N'PILOTL01',N'PILOTA01'))
       OR EXISTS(SELECT 1 FROM dbo.UserIdentityProfiles WITH(UPDLOCK,HOLDLOCK)
                 WHERE UPPER(LTRIM(RTRIM(UserCode))) IN(N'PILOTV01',N'PILOTL01',N'PILOTA01'))
        THROW 58109, N'PATHB_SEED_NAMESPACE_COLLISION_HOLD', 1;

    INSERT dbo.Organizations
        (OrganizationCode,OrganizationName,IsActive,EffectiveFrom,EffectiveTo,CreatedAt)
    VALUES(N'UAT',N'Field Visit Path B UAT',1,@since,NULL,@now);
    SET @org=CONVERT(int,SCOPE_IDENTITY());

    INSERT dbo.Teams
        (OrganizationId,TeamCode,TeamName,IsActive,EffectiveFrom,EffectiveTo,CreatedAt)
    VALUES(@org,N'N01',N'Path B Test Team',1,@since,NULL,@now);
    SET @team=CONVERT(int,SCOPE_IDENTITY());

    INSERT dbo.Roles(RoleCode,RoleName,IsActive,CreatedAt)
    VALUES(N'visitor',N'UAT Visitor',1,@now),(N'leader',N'UAT Leader',1,@now),
          (N'admin',N'UAT Admin',1,@now);
    SELECT @vr=MAX(CASE WHEN RoleCode=N'visitor' THEN RoleId END),
           @lr=MAX(CASE WHEN RoleCode=N'leader' THEN RoleId END),
           @ar=MAX(CASE WHEN RoleCode=N'admin' THEN RoleId END)
    FROM dbo.Roles WHERE RoleCode IN(N'visitor',N'leader',N'admin');
    IF @vr IS NULL OR @lr IS NULL OR @ar IS NULL OR @vr=@lr OR @vr=@ar OR @lr=@ar
        THROW 58110, N'PATHB_SEED_ROLE_RESOLUTION_HOLD', 1;

    INSERT dbo.Users(OrganizationId,TeamId,EmployeeNo,DisplayName,IsActive,CreatedAt)
    VALUES(@org,@team,N'pilotv01',N'Path B UAT Visitor',1,@now),
          (@org,@team,N'pilotl01',N'Path B UAT Leader',1,@now),
          (@org,@team,N'pilota01',N'Path B UAT Admin',1,@now);
    SELECT @vu=MAX(CASE WHEN EmployeeNo=N'pilotv01' THEN UserId END),
           @lu=MAX(CASE WHEN EmployeeNo=N'pilotl01' THEN UserId END),
           @au=MAX(CASE WHEN EmployeeNo=N'pilota01' THEN UserId END)
    FROM dbo.Users WHERE EmployeeNo IN(N'pilotv01',N'pilotl01',N'pilota01');
    IF @vu IS NULL OR @lu IS NULL OR @au IS NULL OR @vu=@lu OR @vu=@au OR @lu=@au
        THROW 58111, N'PATHB_SEED_USER_RESOLUTION_HOLD', 1;

    INSERT dbo.UserIdentityProfiles(UserId,UserType,UserCode,IdentityProvider,CreatedAt)
    VALUES(@vu,N'Internal',N'pilotv01',N'Demo',@now),
          (@lu,N'Internal',N'pilotl01',N'Demo',@now),
          (@au,N'Internal',N'pilota01',N'Demo',@now);
    INSERT dbo.UserEmploymentPeriods
        (UserId,EmploymentStatus,EffectiveFrom,EffectiveTo,SourceType,SourceReference,CreatedAt)
    VALUES(@vu,N'Active',@since,NULL,N'Synthetic',N'PathB-UAT-ThreeRole-v1',@now),
          (@lu,N'Active',@since,NULL,N'Synthetic',N'PathB-UAT-ThreeRole-v1',@now),
          (@au,N'Active',@since,NULL,N'Synthetic',N'PathB-UAT-ThreeRole-v1',@now);
    INSERT dbo.UserRoleAssignments(UserId,RoleId,EffectiveFrom,EffectiveTo,CreatedAt)
    VALUES(@vu,@vr,@since,NULL,@now),(@lu,@lr,@since,NULL,@now),(@au,@ar,@since,NULL,@now);
    INSERT dbo.UserTeamAssignments(UserId,TeamId,IsPrimary,EffectiveFrom,EffectiveTo,CreatedAt)
    VALUES(@vu,@team,1,@since,NULL,@now),(@lu,@team,1,@since,NULL,@now),
          (@au,@team,1,@since,NULL,@now);

    IF (SELECT COUNT(*) FROM dbo.Organizations WHERE OrganizationId=@org AND OrganizationCode=N'UAT'
        AND IsActive=1 AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today))<>1
       OR (SELECT COUNT(*) FROM dbo.Teams WHERE TeamId=@team AND OrganizationId=@org AND TeamCode=N'N01'
        AND IsActive=1 AND EffectiveFrom<=@today AND (EffectiveTo IS NULL OR EffectiveTo>=@today))<>1
       OR (SELECT COUNT(*) FROM dbo.Users WHERE UserId IN(@vu,@lu,@au) AND OrganizationId=@org
        AND TeamId=@team AND IsActive=1)<>3
       OR (SELECT COUNT(*) FROM dbo.UserIdentityProfiles WHERE UserId IN(@vu,@lu,@au)
        AND UserType=N'Internal' AND IdentityProvider=N'Demo' AND EmploymentId IS NULL)<>3
       OR (SELECT COUNT(*) FROM dbo.UserEmploymentPeriods WHERE UserId IN(@vu,@lu,@au)
        AND EmploymentStatus=N'Active' AND EffectiveFrom<=@today
        AND (EffectiveTo IS NULL OR EffectiveTo>=@today))<>3
       OR (SELECT COUNT(*) FROM dbo.UserRoleAssignments a JOIN dbo.Roles r ON r.RoleId=a.RoleId
        WHERE a.UserId IN(@vu,@lu,@au) AND r.IsActive=1 AND a.EffectiveFrom<=@today
        AND (a.EffectiveTo IS NULL OR a.EffectiveTo>=@today)
        AND ((a.UserId=@vu AND r.RoleCode=N'visitor') OR (a.UserId=@lu AND r.RoleCode=N'leader')
          OR (a.UserId=@au AND r.RoleCode=N'admin')))<>3
       OR (SELECT COUNT(*) FROM dbo.UserTeamAssignments WHERE UserId IN(@vu,@lu,@au)
        AND TeamId=@team AND IsPrimary=1 AND EffectiveFrom<=@today
        AND (EffectiveTo IS NULL OR EffectiveTo>=@today))<>3
        THROW 58112, N'PATHB_SEED_POSTCHECK_OR_LOGIN_ELIGIBILITY_HOLD', 1;

    COMMIT TRANSACTION;
    SELECT N'PATHB_UAT_THREE_ROLE_SEED_APPLIED' AS Evidence,
           1 AS Organizations,1 AS Teams,3 AS Roles,3 AS Users,
           3 AS IdentityProfiles,3 AS EmploymentPeriods,
           3 AS RoleAssignments,3 AS TeamAssignments,20 AS TotalRows;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
