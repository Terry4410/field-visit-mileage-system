SET NOCOUNT ON;
SET XACT_ABORT ON;

/* Disposable D-A1b runtime prerequisites only. The Locations base comes from
   da0b-prereq.sql and the real 1800_004 migration; no production schema is changed. */

ALTER TABLE dbo.Users ADD
    OrganizationId INT NULL,
    TeamId INT NULL,
    EmployeeNo NVARCHAR(50) NULL,
    DisplayName NVARCHAR(200) NOT NULL CONSTRAINT DF_DA1B_Users_DisplayName DEFAULT(N''),
    Email NVARCHAR(320) NULL,
    EntraObjectId UNIQUEIDENTIFIER NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_DA1B_Users_IsActive DEFAULT(1),
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_DA1B_Users_CreatedAt DEFAULT(SYSUTCDATETIME()),
    UpdatedAt DATETIME2(3) NULL;

ALTER TABLE dbo.Teams ADD
    OrganizationId INT NOT NULL CONSTRAINT DF_DA1B_Teams_OrganizationId DEFAULT(1),
    TeamCode NVARCHAR(50) NOT NULL CONSTRAINT DF_DA1B_Teams_TeamCode DEFAULT(N''),
    TeamName NVARCHAR(200) NOT NULL CONSTRAINT DF_DA1B_Teams_TeamName DEFAULT(N''),
    IsActive BIT NOT NULL CONSTRAINT DF_DA1B_Teams_IsActive DEFAULT(1),
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_DA1B_Teams_CreatedAt DEFAULT(SYSUTCDATETIME()),
    UpdatedAt DATETIME2(3) NULL,
    EffectiveFrom DATE NULL,
    EffectiveTo DATE NULL,
    Notes NVARCHAR(1000) NULL,
    InactivatedByUserId INT NULL,
    RowVersion ROWVERSION NOT NULL;

ALTER TABLE dbo.DeploymentSites ADD
    SiteCode NVARCHAR(100) NULL,
    SiteName NVARCHAR(200) NULL;

ALTER TABLE dbo.Locations ALTER COLUMN OrganizationId INT NULL;
ALTER TABLE dbo.Locations ADD
    TeamId INT NULL,
    LocationType NVARCHAR(50) NOT NULL CONSTRAINT DF_DA1B_Locations_LocationType DEFAULT(N'Official'),
    PostalCode NVARCHAR(20) NULL,
    City NVARCHAR(100) NULL,
    District NVARCHAR(100) NULL,
    PlusCode NVARCHAR(100) NULL,
    Latitude DECIMAL(10,7) NULL,
    Longitude DECIMAL(10,7) NULL,
    IsTemporary BIT NOT NULL CONSTRAINT DF_DA1B_Locations_IsTemporary DEFAULT(0),
    ApprovalStatus NVARCHAR(50) NOT NULL CONSTRAINT DF_DA1B_Locations_ApprovalStatus DEFAULT(N'Approved'),
    GeocodingStatus NVARCHAR(50) NOT NULL CONSTRAINT DF_DA1B_Locations_GeocodingStatus DEFAULT(N'Success'),
    GeocodedAt DATETIME2(3) NULL,
    CreatedByUserId INT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_DA1B_Locations_CreatedAt DEFAULT(SYSUTCDATETIME()),
    UpdatedAt DATETIME2(3) NULL,
    RowVersion ROWVERSION NOT NULL;
GO

CREATE TABLE dbo.Organizations
(
    OrganizationId INT NOT NULL CONSTRAINT PK_DA1B_Organizations PRIMARY KEY,
    OrganizationCode NVARCHAR(50) NOT NULL,
    OrganizationName NVARCHAR(200) NOT NULL,
    IsActive BIT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL,
    UpdatedAt DATETIME2(3) NULL,
    Notes NVARCHAR(1000) NULL,
    InactivatedAt DATETIME2(3) NULL,
    InactivatedByUserId INT NULL,
    RowVersion ROWVERSION NOT NULL
);

CREATE TABLE dbo.Roles
(
    RoleId INT NOT NULL CONSTRAINT PK_DA1B_Roles PRIMARY KEY,
    RoleCode NVARCHAR(50) NOT NULL,
    RoleName NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NULL,
    IsActive BIT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL
);

CREATE TABLE dbo.Persons
(
    PersonId BIGINT NOT NULL CONSTRAINT PK_DA1B_Persons PRIMARY KEY,
    DisplayName NVARCHAR(200) NOT NULL,
    LegacyUserId INT NULL,
    CreatedAt DATETIME2(3) NOT NULL,
    CreatedByUserId INT NULL,
    UpdatedAt DATETIME2(3) NULL,
    UpdatedByUserId INT NULL,
    RowVersion ROWVERSION NOT NULL
);

CREATE TABLE dbo.Employments
(
    EmploymentId BIGINT NOT NULL CONSTRAINT PK_DA1B_Employments PRIMARY KEY,
    PersonId BIGINT NOT NULL,
    OrganizationId INT NOT NULL,
    EmployeeNo NVARCHAR(50) NULL,
    Email NVARCHAR(320) NULL,
    HireDate DATE NULL,
    TerminationDate DATE NULL,
    LegacyUserId INT NULL,
    SourceType NVARCHAR(50) NOT NULL,
    SourceReference NVARCHAR(500) NULL,
    CreatedAt DATETIME2(3) NOT NULL,
    CreatedByUserId INT NULL,
    UpdatedAt DATETIME2(3) NULL,
    UpdatedByUserId INT NULL,
    RowVersion ROWVERSION NOT NULL
);

CREATE TABLE dbo.EmploymentStatusPeriods
(
    EmploymentStatusPeriodId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DA1B_EmploymentStatusPeriods PRIMARY KEY,
    EmploymentId BIGINT NOT NULL,
    EmploymentStatus NVARCHAR(50) NOT NULL,
    EffectiveFrom DATE NOT NULL,
    EffectiveTo DATE NULL,
    SourceType NVARCHAR(50) NOT NULL,
    SourceReference NVARCHAR(500) NULL,
    CreatedAt DATETIME2(3) NOT NULL,
    CreatedByUserId INT NULL,
    RowVersion ROWVERSION NOT NULL
);

CREATE TABLE dbo.EmploymentRoleAssignments
(
    EmploymentRoleAssignmentId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DA1B_EmploymentRoleAssignments PRIMARY KEY,
    EmploymentId BIGINT NOT NULL,
    RoleId INT NOT NULL,
    EffectiveFrom DATE NOT NULL,
    EffectiveTo DATE NULL,
    AssignedByUserId INT NULL,
    CreatedAt DATETIME2(3) NOT NULL,
    RowVersion ROWVERSION NOT NULL
);

CREATE TABLE dbo.UserIdentityProfiles
(
    UserId INT NOT NULL CONSTRAINT PK_DA1B_UserIdentityProfiles PRIMARY KEY,
    EmploymentId BIGINT NULL,
    UserType NVARCHAR(50) NOT NULL,
    UserCode NVARCHAR(100) NOT NULL,
    IdentityProvider NVARCHAR(50) NOT NULL,
    EntraTenantId UNIQUEIDENTIFIER NULL,
    EntraObjectId UNIQUEIDENTIFIER NULL,
    ExternalOrganization NVARCHAR(200) NULL,
    ExternalTitle NVARCHAR(200) NULL,
    AuthorizationFrom DATE NULL,
    AuthorizationTo DATE NULL,
    CreatedAt DATETIME2(3) NOT NULL,
    UpdatedAt DATETIME2(3) NULL
);

CREATE TABLE dbo.AuditLogs
(
    AuditLogId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DA1B_AuditLogs PRIMARY KEY,
    UserId INT NULL,
    EntityType NVARCHAR(100) NOT NULL,
    EntityId NVARCHAR(100) NULL,
    Action NVARCHAR(100) NOT NULL,
    OldValues NVARCHAR(MAX) NULL,
    NewValues NVARCHAR(MAX) NULL,
    IpAddress NVARCHAR(100) NULL,
    CorrelationId UNIQUEIDENTIFIER NULL,
    CreatedAt DATETIME2(3) NOT NULL
);

INSERT dbo.Organizations(OrganizationId,OrganizationCode,OrganizationName,IsActive,CreatedAt)
VALUES(1,N'ORG1',N'Organization 1',1,SYSUTCDATETIME()),(2,N'ORG2',N'Organization 2',1,SYSUTCDATETIME());
INSERT dbo.Roles(RoleId,RoleCode,RoleName,IsActive,CreatedAt)
VALUES(1,N'admin',N'Admin',1,SYSUTCDATETIME());
INSERT dbo.Teams(TeamId,OrganizationId,TeamCode,TeamName,IsActive)
VALUES(10,1,N'T10',N'Team 10',1),(11,1,N'T11',N'Team 11',1),(20,2,N'T20',N'Team 20',1);
