-- Review candidate: parameterized, approved first Admin only. No SQLCMD substitution.
-- Parameters supplied by DBA wrapper or disposable test harness. Never run against populated DB.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF CONVERT(int,SERVERPROPERTY(N'ProductMajorVersion'))<>16 OR CONVERT(int,SERVERPROPERTY(N'EngineEdition')) NOT IN(2,3,4)
 OR CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')) COLLATE Latin1_General_100_BIN2 <> @ServerName COLLATE Latin1_General_100_BIN2
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> @DatabaseName COLLATE Latin1_General_100_BIN2
 OR DB_ID()<=4 OR DB_NAME() LIKE N'db-fieldvisit-%' OR DB_NAME() LIKE N'OptionCIsolated[_]%'
 OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL'),0)<>1
 OR @@TRANCOUNT<>0
 THROW 58100,N'BOOTSTRAP_TARGET_OR_INSTALLER_DENIED',1;
IF @TenantId IS NULL OR @ObjectId IS NULL
 OR @TenantId=CONVERT(uniqueidentifier,'00000000-0000-0000-0000-000000000000')
 OR @ObjectId=CONVERT(uniqueidentifier,'00000000-0000-0000-0000-000000000000')
 OR NULLIF(LTRIM(RTRIM(@OrganizationCode)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(@OrganizationName)),N'') IS NULL
 OR NULLIF(LTRIM(RTRIM(@EmployeeNo)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(@DisplayName)),N'') IS NULL
 OR NULLIF(LTRIM(RTRIM(@Email)),N'') IS NULL
 OR @HireDate IS NULL OR @HireDate>CONVERT(date,DATEADD(hour,8,SYSUTCDATETIME()))
 THROW 58101,N'BOOTSTRAP_INPUT_INVALID',1;
DECLARE @now datetime2=SYSUTCDATETIME(),@today date=CONVERT(date,DATEADD(hour,8,SYSUTCDATETIME()));
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'FieldVisit_v180_first_admin',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;
 IF @lock<0 THROW 58102,N'BOOTSTRAP_LOCK_DENIED',1;
 IF (SELECT COUNT(*) FROM dbo.SchemaVersions)<>3
 OR (SELECT COUNT(*) FROM dbo.SchemaVersions WHERE VersionNumber IN(N'1.8.0-CREATE-LOWRISK',N'1.8.0-LOWRISK-DIRECT',N'1.8.0-EXCEL-CANONICAL-STATUS'))<>3
 OR (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo'))<>69
 OR (SELECT COUNT(*) FROM dbo.Roles)<>3
 OR (SELECT COUNT(*) FROM dbo.Roles WHERE RoleCode IN(N'Visitor',N'Leader',N'Admin') AND IsActive=1)<>3
 THROW 58103,N'BOOTSTRAP_SCHEMA_OR_ROLES_DENIED',1;
 IF EXISTS(SELECT 1 FROM dbo.Organizations WITH(UPDLOCK,HOLDLOCK)) OR EXISTS(SELECT 1 FROM dbo.Users WITH(UPDLOCK,HOLDLOCK))
 OR EXISTS(SELECT 1 FROM dbo.Persons) OR EXISTS(SELECT 1 FROM dbo.Employments) OR EXISTS(SELECT 1 FROM dbo.UserIdentityProfiles)
 OR EXISTS(SELECT 1 FROM dbo.UserRoleAssignments) OR EXISTS(SELECT 1 FROM dbo.UserRoles) OR EXISTS(SELECT 1 FROM dbo.VisitTrips)
 OR EXISTS(SELECT 1 FROM dbo.Teams) OR EXISTS(SELECT 1 FROM dbo.EmploymentStatusPeriods)
 THROW 58104,N'BOOTSTRAP_ALREADY_INITIALIZED_OR_NONEMPTY',1;
 DECLARE @org int,@user int,@person bigint,@employment bigint,@role int;
 SELECT @role=RoleId FROM dbo.Roles WHERE RoleCode=N'Admin' AND IsActive=1;
 IF @role IS NULL THROW 58105,N'BOOTSTRAP_ADMIN_ROLE_REQUIRED',1;
 INSERT dbo.Organizations(OrganizationCode,OrganizationName,IsActive,EffectiveFrom,CreatedAt)
 VALUES(@OrganizationCode,@OrganizationName,1,@today,@now);
 SET @org=CONVERT(int,SCOPE_IDENTITY());
 INSERT dbo.Users(OrganizationId,TeamId,EmployeeNo,DisplayName,Email,EntraObjectId,IsActive,CreatedAt)
 VALUES(@org,NULL,@EmployeeNo,@DisplayName,@Email,NULL,1,@now);
 SET @user=CONVERT(int,SCOPE_IDENTITY());
 INSERT dbo.Persons(DisplayName,LegacyUserId,CreatedAt,CreatedByUserId) VALUES(@DisplayName,@user,@now,NULL);
 SET @person=CONVERT(bigint,SCOPE_IDENTITY());
 INSERT dbo.Employments(PersonId,OrganizationId,EmployeeNo,Email,HireDate,LegacyUserId,SourceType,SourceReference)
 VALUES(@person,@org,@EmployeeNo,@Email,@HireDate,@user,N'Manual',N'ITFirstAdminBootstrap');
 SET @employment=CONVERT(bigint,SCOPE_IDENTITY());
 INSERT dbo.UserIdentityProfiles(UserId,EmploymentId,UserType,UserCode,IdentityProvider,EntraTenantId,EntraObjectId,CreatedAt)
 VALUES(@user,@employment,N'Internal',@EmployeeNo,N'EntraId',@TenantId,@ObjectId,@now);
 INSERT dbo.EmploymentStatusPeriods(EmploymentId,EmploymentStatus,EffectiveFrom,EffectiveTo,SourceType,SourceReference)
 VALUES(@employment,N'Active',@HireDate,NULL,N'Manual',N'ITFirstAdminBootstrap');
 INSERT dbo.UserRoleAssignments(UserId,RoleId,EffectiveFrom,EffectiveTo,AssignedByUserId,CreatedAt)
 VALUES(@user,@role,@today,NULL,NULL,@now);
 INSERT dbo.UserRoles(UserId,RoleId,AssignedAt) VALUES(@user,@role,@now);
 INSERT dbo.AuditLogs(UserId,EntityType,EntityId,Action,NewValues,CreatedAt)
 VALUES(NULL,N'User',CONVERT(nvarchar(20),@user),N'ITFirstAdminBootstrap',N'{"method":"DBA-approved explicit Entra identity","scope":"first internal Admin"}',@now);
 IF (SELECT COUNT(*) FROM dbo.Users)<>1 OR (SELECT COUNT(*) FROM dbo.UserRoleAssignments)<>1
 OR (SELECT COUNT(*) FROM dbo.UserRoles)<>1 OR (SELECT COUNT(*) FROM dbo.EmploymentStatusPeriods)<>1
 OR NOT EXISTS(SELECT 1 FROM dbo.UserIdentityProfiles WHERE UserId=@user AND EmploymentId=@employment AND EntraTenantId=@TenantId AND EntraObjectId=@ObjectId)
 THROW 58106,N'BOOTSTRAP_READBACK_FAILED',1;
 COMMIT;
 SELECT @user AS UserId,@org AS OrganizationId;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
