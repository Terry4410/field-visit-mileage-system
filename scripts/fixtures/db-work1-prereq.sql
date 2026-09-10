SET NOCOUNT ON;
SET XACT_ABORT ON;

CREATE TABLE dbo.SchemaVersions(
    VersionNumber nvarchar(50) NOT NULL PRIMARY KEY,
    Description nvarchar(500) NULL,
    AppliedAt datetime2(3) NOT NULL,
    AppliedBy nvarchar(200) NULL
);
INSERT dbo.SchemaVersions(VersionNumber,Description,AppliedAt,AppliedBy)
VALUES(N'1.8.0-004',N'D-B Work1 disposable prerequisite',SYSUTCDATETIME(),N'test');

CREATE TABLE dbo.Users(
    UserId int NOT NULL PRIMARY KEY,
    OrganizationId int NULL,
    TeamId int NULL,
    EmployeeNo nvarchar(50) NULL,
    DisplayName nvarchar(200) NOT NULL,
    Email nvarchar(320) NULL,
    EntraObjectId uniqueidentifier NULL,
    IsActive bit NOT NULL CONSTRAINT DF_DBW1_Users_IsActive DEFAULT(1),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_DBW1_Users_CreatedAt DEFAULT(SYSUTCDATETIME()),
    UpdatedAt datetime2(3) NULL
);
INSERT dbo.Users(UserId,OrganizationId,TeamId,EmployeeNo,DisplayName,IsActive)
VALUES(99,1,NULL,N'ADMIN',N'D-B Work1 Test Admin',1);

CREATE TABLE dbo.Organizations(
    OrganizationId int NOT NULL PRIMARY KEY,
    OrganizationCode nvarchar(50) NOT NULL,
    OrganizationName nvarchar(200) NOT NULL,
    IsActive bit NOT NULL,
    CreatedAt datetime2(3) NOT NULL,
    UpdatedAt datetime2(3) NULL,
    Notes nvarchar(1000) NULL,
    InactivatedAt datetime2(3) NULL,
    InactivatedByUserId int NULL,
    RowVersion rowversion NOT NULL
);
INSERT dbo.Organizations(OrganizationId,OrganizationCode,OrganizationName,IsActive,CreatedAt)
VALUES(1,N'O1',N'Work1 Org',1,SYSUTCDATETIME()),(2,N'O2',N'Other Org',1,SYSUTCDATETIME());

CREATE TABLE dbo.Teams(
    TeamId int NOT NULL PRIMARY KEY,
    OrganizationId int NOT NULL,
    TeamCode nvarchar(50) NOT NULL,
    TeamName nvarchar(200) NOT NULL,
    IsActive bit NOT NULL,
    CreatedAt datetime2(3) NOT NULL,
    UpdatedAt datetime2(3) NULL,
    EffectiveFrom date NULL,
    EffectiveTo date NULL,
    Notes nvarchar(1000) NULL,
    InactivatedByUserId int NULL,
    RowVersion rowversion NOT NULL
);
INSERT dbo.Teams(TeamId,OrganizationId,TeamCode,TeamName,IsActive,CreatedAt,EffectiveFrom)
VALUES(10,1,N'T10',N'Team 10',1,SYSUTCDATETIME(),'2026-01-01'),(20,2,N'T20',N'Team 20',1,SYSUTCDATETIME(),'2026-01-01');

CREATE TABLE dbo.Projects(
    ProjectId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OrganizationId int NOT NULL,
    TeamId int NULL,
    ProjectCode nvarchar(50) NOT NULL,
    ProjectName nvarchar(200) NOT NULL,
    Description nvarchar(1000) NULL,
    LocationMode nvarchar(30) NOT NULL CONSTRAINT DF_DBW1_Projects_LocationMode DEFAULT(N'List'),
    StartDate date NULL,
    EndDate date NULL,
    IsActive bit NOT NULL CONSTRAINT DF_DBW1_Projects_IsActive DEFAULT(1),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_DBW1_Projects_CreatedAt DEFAULT(SYSUTCDATETIME()),
    UpdatedAt datetime2(3) NULL
);

CREATE TABLE dbo.VisitTypes(
    VisitTypeId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    VisitTypeCode nvarchar(50) NOT NULL,
    VisitTypeName nvarchar(200) NOT NULL,
    Description nvarchar(1000) NULL,
    SortOrder int NOT NULL,
    IsActive bit NOT NULL CONSTRAINT DF_DBW1_VisitTypes_IsActive DEFAULT(1),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_DBW1_VisitTypes_CreatedAt DEFAULT(SYSUTCDATETIME()),
    UpdatedAt datetime2(3) NULL
);

CREATE TABLE dbo.MileageRateRules(
    MileageRateRuleId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OrganizationId int NULL,
    RuleName nvarchar(200) NOT NULL,
    VehicleType nvarchar(50) NOT NULL,
    RatePerKm decimal(10,2) NOT NULL,
    EffectiveFrom date NOT NULL,
    EffectiveTo date NULL,
    IsActive bit NOT NULL,
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_DBW1_Rates_CreatedAt DEFAULT(SYSUTCDATETIME()),
    UpdatedAt datetime2(3) NULL
);

CREATE TABLE dbo.AuditLogs(
    AuditLogId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId int NULL,
    EntityType nvarchar(100) NOT NULL,
    EntityId nvarchar(100) NULL,
    Action nvarchar(100) NOT NULL,
    OldValues nvarchar(max) NULL,
    NewValues nvarchar(max) NULL,
    IpAddress nvarchar(100) NULL,
    CorrelationId uniqueidentifier NULL,
    CreatedAt datetime2(3) NOT NULL
);

CREATE TABLE dbo.VisitTripSnapshots(
    VisitTripSnapshotId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY
);
