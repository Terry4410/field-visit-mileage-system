SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;

DECLARE @Now datetime2 = SYSUTCDATETIME();
DECLARE @OrgId int, @TeamId int, @VisitorRole int, @LeaderRole int, @AdminRole int, @SupervisorRole int;
DECLARE @Visitor01 int, @Visitor02 int, @Leader01 int, @Admin01 int, @Gov01 int;

-- Organization
SELECT @OrgId = OrganizationId FROM dbo.Organizations WHERE OrganizationCode = N'UAT';
IF @OrgId IS NULL
BEGIN
  INSERT dbo.Organizations(OrganizationCode,OrganizationName,IsActive,CreatedAt) VALUES(N'UAT',N'外訪系統 UAT 組織',1,@Now);
  SET @OrgId = CONVERT(int,SCOPE_IDENTITY());
END;

-- Team
SELECT @TeamId = TeamId FROM dbo.Teams WHERE OrganizationId=@OrgId AND TeamCode=N'N01';
IF @TeamId IS NULL
BEGIN
  INSERT dbo.Teams(OrganizationId,TeamCode,TeamName,IsActive,CreatedAt) VALUES(@OrgId,N'N01',N'北區第一組',1,@Now);
  SET @TeamId=CONVERT(int,SCOPE_IDENTITY());
END;

-- Roles
SELECT @VisitorRole=RoleId FROM dbo.Roles WHERE UPPER(RoleCode)=N'VISITOR';
IF @VisitorRole IS NULL BEGIN INSERT dbo.Roles(RoleCode,RoleName,Description,IsActive,CreatedAt) VALUES(N'Visitor',N'外訪員',N'UAT Visitor',1,@Now); SET @VisitorRole=CONVERT(int,SCOPE_IDENTITY()); END;
SELECT @LeaderRole=RoleId FROM dbo.Roles WHERE UPPER(RoleCode)=N'LEADER';
IF @LeaderRole IS NULL BEGIN INSERT dbo.Roles(RoleCode,RoleName,Description,IsActive,CreatedAt) VALUES(N'Leader',N'小組長',N'UAT Leader',1,@Now); SET @LeaderRole=CONVERT(int,SCOPE_IDENTITY()); END;
SELECT @AdminRole=RoleId FROM dbo.Roles WHERE UPPER(RoleCode)=N'ADMIN';
IF @AdminRole IS NULL BEGIN INSERT dbo.Roles(RoleCode,RoleName,Description,IsActive,CreatedAt) VALUES(N'Admin',N'管理者',N'UAT Admin',1,@Now); SET @AdminRole=CONVERT(int,SCOPE_IDENTITY()); END;
SELECT @SupervisorRole=RoleId FROM dbo.Roles WHERE UPPER(RoleCode) IN(N'SUPERVISOR',N'GOVERNMENT');
IF @SupervisorRole IS NULL BEGIN INSERT dbo.Roles(RoleCode,RoleName,Description,IsActive,CreatedAt) VALUES(N'Supervisor',N'督導',N'UAT Read-only Supervisor',1,@Now); SET @SupervisorRole=CONVERT(int,SCOPE_IDENTITY()); END;

-- Users helper pattern
SELECT @Visitor01=UserId FROM dbo.Users WHERE EmployeeNo=N'visitor01';
IF @Visitor01 IS NULL BEGIN INSERT dbo.Users(OrganizationId,TeamId,EmployeeNo,DisplayName,Email,EntraObjectId,IsActive,CreatedAt) VALUES(@OrgId,@TeamId,N'visitor01',N'王小明',N'visitor01@example.com',NULL,1,@Now); SET @Visitor01=CONVERT(int,SCOPE_IDENTITY()); END;
SELECT @Visitor02=UserId FROM dbo.Users WHERE EmployeeNo=N'visitor02';
IF @Visitor02 IS NULL BEGIN INSERT dbo.Users(OrganizationId,TeamId,EmployeeNo,DisplayName,Email,EntraObjectId,IsActive,CreatedAt) VALUES(@OrgId,@TeamId,N'visitor02',N'李小華',N'visitor02@example.com',NULL,1,@Now); SET @Visitor02=CONVERT(int,SCOPE_IDENTITY()); END;
SELECT @Leader01=UserId FROM dbo.Users WHERE EmployeeNo=N'leader01';
IF @Leader01 IS NULL BEGIN INSERT dbo.Users(OrganizationId,TeamId,EmployeeNo,DisplayName,Email,EntraObjectId,IsActive,CreatedAt) VALUES(@OrgId,@TeamId,N'leader01',N'林組長',N'leader01@example.com',NULL,1,@Now); SET @Leader01=CONVERT(int,SCOPE_IDENTITY()); END;
SELECT @Admin01=UserId FROM dbo.Users WHERE EmployeeNo=N'admin01';
IF @Admin01 IS NULL BEGIN INSERT dbo.Users(OrganizationId,TeamId,EmployeeNo,DisplayName,Email,EntraObjectId,IsActive,CreatedAt) VALUES(@OrgId,NULL,N'admin01',N'系統管理員',N'admin01@example.com',NULL,1,@Now); SET @Admin01=CONVERT(int,SCOPE_IDENTITY()); END;
SELECT @Gov01=UserId
FROM dbo.Users
WHERE Email=N'gov01@example.com';

IF @Gov01 IS NULL
    SELECT @Gov01=UserId
    FROM dbo.Users
    WHERE EmployeeNo=N'gov01';
IF @Gov01 IS NULL BEGIN INSERT dbo.Users(OrganizationId,TeamId,EmployeeNo,DisplayName,Email,EntraObjectId,IsActive,CreatedAt) VALUES(@OrgId,NULL,N'gov01',N'督導人員',N'gov01@example.com',NULL,1,@Now); SET @Gov01=CONVERT(int,SCOPE_IDENTITY()); END;

IF NOT EXISTS(SELECT 1 FROM dbo.UserRoles WHERE UserId=@Visitor01 AND RoleId=@VisitorRole) INSERT dbo.UserRoles(UserId,RoleId,AssignedAt) VALUES(@Visitor01,@VisitorRole,@Now);
IF NOT EXISTS(SELECT 1 FROM dbo.UserRoles WHERE UserId=@Visitor02 AND RoleId=@VisitorRole) INSERT dbo.UserRoles(UserId,RoleId,AssignedAt) VALUES(@Visitor02,@VisitorRole,@Now);
IF NOT EXISTS(SELECT 1 FROM dbo.UserRoles WHERE UserId=@Leader01 AND RoleId=@LeaderRole) INSERT dbo.UserRoles(UserId,RoleId,AssignedAt) VALUES(@Leader01,@LeaderRole,@Now);
IF NOT EXISTS(SELECT 1 FROM dbo.UserRoles WHERE UserId=@Admin01 AND RoleId=@AdminRole) INSERT dbo.UserRoles(UserId,RoleId,AssignedAt) VALUES(@Admin01,@AdminRole,@Now);
IF NOT EXISTS(SELECT 1 FROM dbo.UserRoles WHERE UserId=@Gov01 AND RoleId=@SupervisorRole) INSERT dbo.UserRoles(UserId,RoleId,AssignedAt) VALUES(@Gov01,@SupervisorRole,@Now);

-- Visit types
IF NOT EXISTS(SELECT 1 FROM dbo.VisitTypes WHERE VisitTypeCode=N'IN_PERSON') INSERT dbo.VisitTypes(VisitTypeCode,VisitTypeName,Description,SortOrder,IsActive,CreatedAt) VALUES(N'IN_PERSON',N'親訪',N'現場拜訪',10,1,@Now);
IF NOT EXISTS(SELECT 1 FROM dbo.VisitTypes WHERE VisitTypeCode=N'PHONE') INSERT dbo.VisitTypes(VisitTypeCode,VisitTypeName,Description,SortOrder,IsActive,CreatedAt) VALUES(N'PHONE',N'電訪',N'電話訪談',20,1,@Now);
IF NOT EXISTS(SELECT 1 FROM dbo.VisitTypes WHERE VisitTypeCode=N'VIDEO') INSERT dbo.VisitTypes(VisitTypeCode,VisitTypeName,Description,SortOrder,IsActive,CreatedAt) VALUES(N'VIDEO',N'視訊訪談',N'視訊訪談',30,1,@Now);
IF NOT EXISTS(SELECT 1 FROM dbo.VisitTypes WHERE VisitTypeCode=N'DOCUMENT') INSERT dbo.VisitTypes(VisitTypeCode,VisitTypeName,Description,SortOrder,IsActive,CreatedAt) VALUES(N'DOCUMENT',N'文件送達',N'文件送達',40,1,@Now);
IF NOT EXISTS(SELECT 1 FROM dbo.VisitTypes WHERE VisitTypeCode=N'OTHER') INSERT dbo.VisitTypes(VisitTypeCode,VisitTypeName,Description,SortOrder,IsActive,CreatedAt) VALUES(N'OTHER',N'其他',N'其他',99,1,@Now);

-- Locations
IF NOT EXISTS(SELECT 1 FROM dbo.Locations WHERE OrganizationId=@OrgId AND LocationCode=N'HQ')
 INSERT dbo.Locations(OrganizationId,TeamId,LocationCode,LocationName,LocationType,PostalCode,City,District,Address,PlusCode,Latitude,Longitude,IsTemporary,ApprovalStatus,GeocodingStatus,GeocodedAt,CreatedByUserId,IsActive,CreatedAt)
 VALUES(@OrgId,@TeamId,N'HQ',N'總公司',N'Office',NULL,N'台北市',N'松山區',N'台北市松山區復興北路 100 號',NULL,25.0520000,121.5440000,0,N'Approved',N'Completed',@Now,@Admin01,1,@Now);
IF NOT EXISTS(SELECT 1 FROM dbo.Locations WHERE OrganizationId=@OrgId AND LocationCode=N'CUST-A')
 INSERT dbo.Locations(OrganizationId,TeamId,LocationCode,LocationName,LocationType,PostalCode,City,District,Address,PlusCode,Latitude,Longitude,IsTemporary,ApprovalStatus,GeocodingStatus,GeocodedAt,CreatedByUserId,IsActive,CreatedAt)
 VALUES(@OrgId,@TeamId,N'CUST-A',N'客戶 A｜內湖據點',N'Customer',NULL,N'台北市',N'內湖區',N'台北市內湖區瑞光路 258 號',NULL,25.0785000,121.5752000,0,N'Approved',N'Completed',@Now,@Admin01,1,@Now);
IF NOT EXISTS(SELECT 1 FROM dbo.Locations WHERE OrganizationId=@OrgId AND LocationCode=N'CUST-B')
 INSERT dbo.Locations(OrganizationId,TeamId,LocationCode,LocationName,LocationType,PostalCode,City,District,Address,PlusCode,Latitude,Longitude,IsTemporary,ApprovalStatus,GeocodingStatus,GeocodedAt,CreatedByUserId,IsActive,CreatedAt)
 VALUES(@OrgId,@TeamId,N'CUST-B',N'客戶 B｜南港據點',N'Customer',NULL,N'台北市',N'南港區',N'台北市南港區三重路 19-13 號',NULL,25.0564000,121.6139000,0,N'Approved',N'Completed',@Now,@Admin01,1,@Now);
IF NOT EXISTS(SELECT 1 FROM dbo.Locations WHERE OrganizationId=@OrgId AND LocationCode=N'CUST-C')
 INSERT dbo.Locations(OrganizationId,TeamId,LocationCode,LocationName,LocationType,PostalCode,City,District,Address,PlusCode,Latitude,Longitude,IsTemporary,ApprovalStatus,GeocodingStatus,GeocodedAt,CreatedByUserId,IsActive,CreatedAt)
 VALUES(@OrgId,@TeamId,N'CUST-C',N'客戶 C｜汐止據點',N'Customer',NULL,N'新北市',N'汐止區',N'新北市汐止區新台五路一段 99 號',NULL,25.0613000,121.6498000,0,N'Approved',N'Completed',@Now,@Admin01,1,@Now);

-- Projects
DECLARE @ProjectId int;
SELECT @ProjectId=ProjectId FROM dbo.Projects WHERE OrganizationId=@OrgId AND ProjectCode=N'CARE-001';
IF @ProjectId IS NULL
BEGIN
 INSERT dbo.Projects(OrganizationId,TeamId,ProjectCode,ProjectName,Description,LocationMode,StartDate,EndDate,IsActive,CreatedAt)
 VALUES(@OrgId,@TeamId,N'CARE-001',N'高齡關懷訪視專案',N'UAT 固定清單專案',N'List','2026-01-01',NULL,1,@Now);
 SET @ProjectId=CONVERT(int,SCOPE_IDENTITY());
END;
INSERT dbo.ProjectLocations(ProjectId,LocationId,IsPrimary,IsActive,CreatedAt)
SELECT @ProjectId,LocationId,CASE WHEN LocationCode=N'CUST-A' THEN 1 ELSE 0 END,1,@Now FROM dbo.Locations l
WHERE l.OrganizationId=@OrgId AND l.LocationCode IN(N'CUST-A',N'CUST-B',N'CUST-C')
AND NOT EXISTS(SELECT 1 FROM dbo.ProjectLocations pl WHERE pl.ProjectId=@ProjectId AND pl.LocationId=l.LocationId);
IF NOT EXISTS(SELECT 1 FROM dbo.Projects WHERE OrganizationId=@OrgId AND ProjectCode=N'JOB-001')
 INSERT dbo.Projects(OrganizationId,TeamId,ProjectCode,ProjectName,Description,LocationMode,StartDate,EndDate,IsActive,CreatedAt)
 VALUES(@OrgId,@TeamId,N'JOB-001',N'就業服務追蹤專案',N'UAT 自行維護地點專案',N'SelfMaintained','2026-01-01',NULL,1,@Now);

-- Mileage rate：確保 2026-01-01 起 UAT 可測 2.5 元。
IF NOT EXISTS(SELECT 1 FROM dbo.MileageRateRules WHERE (OrganizationId=@OrgId OR OrganizationId IS NULL) AND VehicleType=N'Motorcycle' AND EffectiveFrom <= '2026-01-01' AND (EffectiveTo IS NULL OR EffectiveTo >= '2026-01-01'))
 INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive,CreatedAt)
 VALUES(@OrgId,N'UAT 機車里程補助 2.5',N'Motorcycle',2.50,'2026-01-01',NULL,1,@Now);

COMMIT;
PRINT N'020_uat_demo_seed.sql completed.';
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
END CATCH;
