-- SQL2022 disposable test-only canonical 1800_001..003 CREATE TABLE extracts; NOT full DB/triggers.
-- NEVER run on any Azure server or user data; pre-existing eight core tables from synthetic fixture.
-- Canonical: database/migrations/1800_002_person_employment_role_membership/Up.sql
CREATE TABLE dbo.Persons
    (
        PersonId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Persons PRIMARY KEY,
        DisplayName NVARCHAR(200) NOT NULL,
        LegacyUserId INT NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Persons_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId INT NULL,
        UpdatedAt DATETIME2(3) NULL,
        UpdatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_Persons_LegacyUser FOREIGN KEY(LegacyUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_Persons_CreatedByUser FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_Persons_UpdatedByUser FOREIGN KEY(UpdatedByUserId) REFERENCES dbo.Users(UserId)
    );
GO
-- Canonical: database/migrations/1800_002_person_employment_role_membership/Up.sql
CREATE TABLE dbo.Employments
    (
        EmploymentId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Employments PRIMARY KEY,
        PersonId BIGINT NOT NULL,
        OrganizationId INT NOT NULL,
        EmployeeNo NVARCHAR(50) NULL,
        Email NVARCHAR(320) NULL,
        HireDate DATE NULL,
        TerminationDate DATE NULL,
        LegacyUserId INT NULL,
        SourceType NVARCHAR(30) NOT NULL,
        SourceReference NVARCHAR(200) NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Employments_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId INT NULL,
        UpdatedAt DATETIME2(3) NULL,
        UpdatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_Employments_Persons FOREIGN KEY(PersonId) REFERENCES dbo.Persons(PersonId),
        CONSTRAINT FK_Employments_Organizations FOREIGN KEY(OrganizationId) REFERENCES dbo.Organizations(OrganizationId),
        CONSTRAINT FK_Employments_LegacyUser FOREIGN KEY(LegacyUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_Employments_CreatedByUser FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_Employments_UpdatedByUser FOREIGN KEY(UpdatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_Employments_Dates CHECK(TerminationDate IS NULL OR HireDate IS NULL OR TerminationDate >= HireDate)
    );
GO
-- Canonical: database/migrations/1800_002_person_employment_role_membership/Up.sql
CREATE TABLE dbo.EmploymentStatusPeriods
    (
        EmploymentStatusPeriodId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmploymentStatusPeriods PRIMARY KEY,
        EmploymentId BIGINT NOT NULL,
        EmploymentStatus NVARCHAR(30) NOT NULL,
        EffectiveFrom DATE NOT NULL,
        EffectiveTo DATE NULL,
        SourceType NVARCHAR(30) NOT NULL,
        SourceReference NVARCHAR(200) NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_EmploymentStatusPeriods_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_EmploymentStatusPeriods_Employments FOREIGN KEY(EmploymentId) REFERENCES dbo.Employments(EmploymentId),
        CONSTRAINT FK_EmploymentStatusPeriods_CreatedByUser FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_EmploymentStatusPeriods_Status CHECK(EmploymentStatus IN(N'Active', N'Leave', N'Terminated', N'PreHire')),
        CONSTRAINT CK_EmploymentStatusPeriods_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
        CONSTRAINT UQ_EmploymentStatusPeriods_Start UNIQUE(EmploymentId, EffectiveFrom)
    );
GO
-- Canonical: database/migrations/1800_002_person_employment_role_membership/Up.sql
CREATE TABLE dbo.TeamMemberships
    (
        TeamMembershipId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TeamMemberships PRIMARY KEY,
        EmploymentId BIGINT NOT NULL,
        TeamId INT NOT NULL,
        IsPrimary BIT NOT NULL CONSTRAINT DF_TeamMemberships_IsPrimary DEFAULT(0),
        EffectiveFrom DATE NOT NULL,
        EffectiveTo DATE NULL,
        ChangeReason NVARCHAR(500) NULL,
        AssignedByUserId INT NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_TeamMemberships_CreatedAt DEFAULT(SYSUTCDATETIME()),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_TeamMemberships_Employments FOREIGN KEY(EmploymentId) REFERENCES dbo.Employments(EmploymentId),
        CONSTRAINT FK_TeamMemberships_Teams FOREIGN KEY(TeamId) REFERENCES dbo.Teams(TeamId),
        CONSTRAINT FK_TeamMemberships_AssignedByUser FOREIGN KEY(AssignedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_TeamMemberships_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
        CONSTRAINT UQ_TeamMemberships_Start UNIQUE(EmploymentId, TeamId, EffectiveFrom)
    );
GO
-- Canonical: database/migrations/1800_001_organization_center_team_lifecycle/Up.sql
CREATE TABLE dbo.Centers
    (
        CenterId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_Centers PRIMARY KEY,
        OrganizationId INT NOT NULL,
        CenterCode NVARCHAR(50) NOT NULL,
        CenterName NVARCHAR(200) NOT NULL,
        EffectiveFrom DATE NOT NULL,
        EffectiveTo DATE NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Centers_IsActive DEFAULT(1),
        Notes NVARCHAR(1000) NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Centers_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId INT NULL,
        UpdatedAt DATETIME2(3) NULL,
        UpdatedByUserId INT NULL,
        InactivatedAt DATETIME2(3) NULL,
        InactivatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_Centers_Organizations
            FOREIGN KEY(OrganizationId) REFERENCES dbo.Organizations(OrganizationId),
        CONSTRAINT FK_Centers_CreatedByUser
            FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_Centers_UpdatedByUser
            FOREIGN KEY(UpdatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_Centers_InactivatedByUser
            FOREIGN KEY(InactivatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_Centers_EffectiveDates
            CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
        CONSTRAINT UQ_Centers_Organization_Code
            UNIQUE(OrganizationId, CenterCode)
    );
GO
-- Canonical: database/migrations/1800_001_organization_center_team_lifecycle/Up.sql
CREATE TABLE dbo.TeamCenterAssignments
    (
        TeamCenterAssignmentId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TeamCenterAssignments PRIMARY KEY,
        TeamId INT NOT NULL,
        CenterId INT NOT NULL,
        EffectiveFrom DATE NOT NULL,
        EffectiveTo DATE NULL,
        ChangeReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2(3) NOT NULL
            CONSTRAINT DF_TeamCenterAssignments_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_TeamCenterAssignments_Teams
            FOREIGN KEY(TeamId) REFERENCES dbo.Teams(TeamId),
        CONSTRAINT FK_TeamCenterAssignments_Centers
            FOREIGN KEY(CenterId) REFERENCES dbo.Centers(CenterId),
        CONSTRAINT FK_TeamCenterAssignments_CreatedByUser
            FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_TeamCenterAssignments_Dates
            CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
        CONSTRAINT UQ_TeamCenterAssignments_Team_Start
            UNIQUE(TeamId, EffectiveFrom)
    );
GO
-- Canonical: database/migrations/1800_003_deployment_sites/Up.sql
CREATE TABLE dbo.DeploymentSites
    (
        DeploymentSiteId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DeploymentSites PRIMARY KEY,
        CenterId INT NOT NULL,
        SiteCode NVARCHAR(50) NOT NULL,
        SiteName NVARCHAR(200) NOT NULL,
        EffectiveFrom DATE NOT NULL,
        EffectiveTo DATE NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_DeploymentSites_IsActive DEFAULT(1),
        Notes NVARCHAR(1000) NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_DeploymentSites_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId INT NULL,
        UpdatedAt DATETIME2(3) NULL,
        UpdatedByUserId INT NULL,
        InactivatedAt DATETIME2(3) NULL,
        InactivatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_DeploymentSites_Centers FOREIGN KEY(CenterId) REFERENCES dbo.Centers(CenterId),
        CONSTRAINT FK_DeploymentSites_CreatedByUser FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_DeploymentSites_UpdatedByUser FOREIGN KEY(UpdatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_DeploymentSites_InactivatedByUser FOREIGN KEY(InactivatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_DeploymentSites_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
        CONSTRAINT UQ_DeploymentSites_Center_Code UNIQUE(CenterId, SiteCode)
    );
GO
-- Synthetic base Location stub before canonical site->location foreign key.
CREATE TABLE dbo.Locations(LocationId int IDENTITY PRIMARY KEY,OrganizationId int NOT NULL,TeamId int NULL,LocationCode nvarchar(50) NULL,LocationName nvarchar(200) NOT NULL,LocationType nvarchar(30) NOT NULL,PostalCode nvarchar(20) NULL,City nvarchar(100) NULL,District nvarchar(100) NULL,Address nvarchar(500) NULL,PlusCode nvarchar(200) NULL,Latitude decimal(10,7) NULL,Longitude decimal(10,7) NULL,IsTemporary bit NOT NULL,ApprovalStatus nvarchar(30) NOT NULL,GeocodingStatus nvarchar(30) NOT NULL,GeocodedAt datetime2(3) NULL,CreatedByUserId int NULL,IsActive bit NOT NULL,CreatedAt datetime2(3) NOT NULL,FOREIGN KEY(OrganizationId) REFERENCES dbo.Organizations(OrganizationId),FOREIGN KEY(TeamId) REFERENCES dbo.Teams(TeamId),FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(UserId));
GO

-- Canonical: database/migrations/1800_003_deployment_sites/Up.sql
CREATE TABLE dbo.DeploymentSiteLocationAssignments
    (
        DeploymentSiteLocationAssignmentId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_DeploymentSiteLocationAssignments PRIMARY KEY,
        DeploymentSiteId INT NOT NULL,
        LocationId INT NOT NULL,
        EffectiveFrom DATE NOT NULL,
        EffectiveTo DATE NULL,
        ChangeReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_DeploymentSiteLocationAssignments_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_DeploymentSiteLocationAssignments_Sites FOREIGN KEY(DeploymentSiteId) REFERENCES dbo.DeploymentSites(DeploymentSiteId),
        CONSTRAINT FK_DeploymentSiteLocationAssignments_Locations FOREIGN KEY(LocationId) REFERENCES dbo.Locations(LocationId),
        CONSTRAINT FK_DeploymentSiteLocationAssignments_CreatedByUser FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_DeploymentSiteLocationAssignments_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
        CONSTRAINT UQ_DeploymentSiteLocationAssignments_Start UNIQUE(DeploymentSiteId, EffectiveFrom)
    );
GO
-- Canonical: database/migrations/1800_003_deployment_sites/Up.sql
CREATE TABLE dbo.TeamDeploymentSiteAssignments
    (
        TeamDeploymentSiteAssignmentId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TeamDeploymentSiteAssignments PRIMARY KEY,
        TeamId INT NOT NULL,
        DeploymentSiteId INT NOT NULL,
        EffectiveFrom DATE NOT NULL,
        EffectiveTo DATE NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_TeamDeploymentSiteAssignments_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_TeamDeploymentSiteAssignments_Teams FOREIGN KEY(TeamId) REFERENCES dbo.Teams(TeamId),
        CONSTRAINT FK_TeamDeploymentSiteAssignments_Sites FOREIGN KEY(DeploymentSiteId) REFERENCES dbo.DeploymentSites(DeploymentSiteId),
        CONSTRAINT FK_TeamDeploymentSiteAssignments_CreatedByUser FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_TeamDeploymentSiteAssignments_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
        CONSTRAINT UQ_TeamDeploymentSiteAssignments_Start UNIQUE(TeamId, DeploymentSiteId, EffectiveFrom)
    );
GO
-- Canonical: database/migrations/1800_003_deployment_sites/Up.sql
CREATE TABLE dbo.EmploymentDeploymentSiteAssignments
    (
        EmploymentDeploymentSiteAssignmentId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_EmploymentDeploymentSiteAssignments PRIMARY KEY,
        EmploymentId BIGINT NOT NULL,
        DeploymentSiteId INT NOT NULL,
        IsPrimary BIT NOT NULL CONSTRAINT DF_EmploymentDeploymentSiteAssignments_IsPrimary DEFAULT(0),
        EffectiveFrom DATE NOT NULL,
        EffectiveTo DATE NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_EmploymentDeploymentSiteAssignments_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_EmploymentDeploymentSiteAssignments_Employments FOREIGN KEY(EmploymentId) REFERENCES dbo.Employments(EmploymentId),
        CONSTRAINT FK_EmploymentDeploymentSiteAssignments_Sites FOREIGN KEY(DeploymentSiteId) REFERENCES dbo.DeploymentSites(DeploymentSiteId),
        CONSTRAINT FK_EmploymentDeploymentSiteAssignments_CreatedByUser FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_EmploymentDeploymentSiteAssignments_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
        CONSTRAINT UQ_EmploymentDeploymentSiteAssignments_Start UNIQUE(EmploymentId, DeploymentSiteId, EffectiveFrom)
    );
GO
-- Base 1.5 tables are unavailable as a complete canonical source: synthetic stubs below.
CREATE TABLE dbo.VisitTypes(VisitTypeId int IDENTITY PRIMARY KEY,VisitTypeCode nvarchar(50) NOT NULL UNIQUE,VisitTypeName nvarchar(200) NOT NULL,Description nvarchar(500) NULL,SortOrder int NOT NULL,IsActive bit NOT NULL,CreatedAt datetime2(3) NOT NULL);
GO
CREATE TABLE dbo.Projects(ProjectId int IDENTITY PRIMARY KEY,OrganizationId int NOT NULL,TeamId int NULL,ProjectCode nvarchar(50) NOT NULL,ProjectName nvarchar(200) NOT NULL,Description nvarchar(500) NULL,LocationMode nvarchar(30) NOT NULL,StartDate date NULL,IsActive bit NOT NULL,CreatedAt datetime2(3) NOT NULL,UNIQUE(OrganizationId,ProjectCode),FOREIGN KEY(OrganizationId) REFERENCES dbo.Organizations(OrganizationId),FOREIGN KEY(TeamId) REFERENCES dbo.Teams(TeamId));
GO
