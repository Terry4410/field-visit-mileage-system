SET XACT_ABORT ON;
CREATE TABLE dbo.Organizations(
 OrganizationId int IDENTITY PRIMARY KEY,OrganizationCode nvarchar(50) NOT NULL,
 OrganizationName nvarchar(200) NOT NULL,IsActive bit NOT NULL,
 EffectiveFrom date NULL,EffectiveTo date NULL,CreatedAt datetime2(3) NOT NULL,UpdatedAt datetime2(3) NULL,
 CONSTRAINT UQ_Organizations_Code UNIQUE(OrganizationCode));
CREATE TABLE dbo.Teams(
 TeamId int IDENTITY PRIMARY KEY,OrganizationId int NOT NULL,TeamCode nvarchar(50) NOT NULL,
 TeamName nvarchar(200) NOT NULL,IsActive bit NOT NULL,EffectiveFrom date NULL,EffectiveTo date NULL,
 CreatedAt datetime2(3) NOT NULL,UpdatedAt datetime2(3) NULL,
 CONSTRAINT FK_Teams_Org FOREIGN KEY(OrganizationId) REFERENCES dbo.Organizations(OrganizationId),
 CONSTRAINT UQ_Teams_Code UNIQUE(OrganizationId,TeamCode));
CREATE TABLE dbo.Roles(
 RoleId int IDENTITY PRIMARY KEY,RoleCode nvarchar(50) NOT NULL,RoleName nvarchar(100) NOT NULL,
 Description nvarchar(500) NULL,IsActive bit NOT NULL,CreatedAt datetime2(3) NOT NULL,
 CONSTRAINT UQ_Roles_Code UNIQUE(RoleCode));
CREATE TABLE dbo.Users(
 UserId int IDENTITY PRIMARY KEY,OrganizationId int NULL,TeamId int NULL,EmployeeNo nvarchar(50) NULL,
 DisplayName nvarchar(200) NOT NULL,Email nvarchar(320) NULL,EntraObjectId uniqueidentifier NULL,
 IsActive bit NOT NULL,CreatedAt datetime2(3) NOT NULL,UpdatedAt datetime2(3) NULL,
 CONSTRAINT FK_Users_Org FOREIGN KEY(OrganizationId) REFERENCES dbo.Organizations(OrganizationId),
 CONSTRAINT FK_Users_Team FOREIGN KEY(TeamId) REFERENCES dbo.Teams(TeamId));
CREATE UNIQUE INDEX UX_Users_EmployeeNo_NotNull ON dbo.Users(EmployeeNo) WHERE EmployeeNo IS NOT NULL;
CREATE TABLE dbo.UserIdentityProfiles(
 UserId int NOT NULL PRIMARY KEY,UserType nvarchar(20) NOT NULL,UserCode nvarchar(100) NOT NULL,
 IdentityProvider nvarchar(30) NOT NULL,ExternalOrganization nvarchar(200) NULL,
 ExternalTitle nvarchar(200) NULL,AuthorizationFrom date NULL,AuthorizationTo date NULL,
 EmploymentId bigint NULL,EntraTenantId uniqueidentifier NULL,EntraObjectId uniqueidentifier NULL,
 CreatedAt datetime2(3) NOT NULL,UpdatedAt datetime2(3) NULL,
 CONSTRAINT FK_UIP_User FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId),
 CONSTRAINT CK_UIP_Type CHECK(UserType IN(N'Internal',N'External')),
 CONSTRAINT UQ_UIP_Code UNIQUE(UserCode));
CREATE TABLE dbo.UserEmploymentPeriods(
 UserEmploymentPeriodId bigint IDENTITY PRIMARY KEY,UserId int NOT NULL,EmploymentStatus nvarchar(30) NOT NULL,
 EffectiveFrom date NOT NULL,EffectiveTo date NULL,SourceType nvarchar(30) NOT NULL,
 SourceReference nvarchar(200) NULL,CreatedAt datetime2(3) NOT NULL,UpdatedAt datetime2(3) NULL,
 CONSTRAINT FK_UEP_User FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId),
 CONSTRAINT CK_UEP_Status CHECK(EmploymentStatus IN(N'Active',N'Leave',N'Terminated',N'PreHire')),
 CONSTRAINT CK_UEP_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo>=EffectiveFrom));
CREATE TABLE dbo.UserRoleAssignments(
 UserRoleAssignmentId bigint IDENTITY PRIMARY KEY,UserId int NOT NULL,RoleId int NOT NULL,
 EffectiveFrom date NOT NULL,EffectiveTo date NULL,AssignedByUserId int NULL,CreatedAt datetime2(3) NOT NULL,
 CONSTRAINT FK_URA_User FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId),
 CONSTRAINT FK_URA_Role FOREIGN KEY(RoleId) REFERENCES dbo.Roles(RoleId),
 CONSTRAINT UQ_URA UNIQUE(UserId,RoleId,EffectiveFrom),
 CONSTRAINT CK_URA_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo>=EffectiveFrom));
CREATE TABLE dbo.UserTeamAssignments(
 UserTeamAssignmentId bigint IDENTITY PRIMARY KEY,UserId int NOT NULL,TeamId int NOT NULL,IsPrimary bit NOT NULL,
 EffectiveFrom date NOT NULL,EffectiveTo date NULL,AssignedByUserId int NULL,CreatedAt datetime2(3) NOT NULL,
 CONSTRAINT FK_UTA_User FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId),
 CONSTRAINT FK_UTA_Team FOREIGN KEY(TeamId) REFERENCES dbo.Teams(TeamId),
 CONSTRAINT UQ_UTA UNIQUE(UserId,TeamId,EffectiveFrom),
 CONSTRAINT CK_UTA_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo>=EffectiveFrom));
