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
    DisplayName nvarchar(200) NOT NULL
);
INSERT dbo.Users(UserId,DisplayName) VALUES(99,N'D-B Work1 Test Admin');

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

CREATE TABLE dbo.VisitTripSnapshots(
    VisitTripSnapshotId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY
);
