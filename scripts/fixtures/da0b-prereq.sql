SET NOCOUNT ON;
SET XACT_ABORT ON;

CREATE TABLE dbo.SchemaVersions
(
    VersionNumber NVARCHAR(50) NOT NULL CONSTRAINT PK_DA0B_SchemaVersions PRIMARY KEY,
    Description NVARCHAR(500) NULL,
    AppliedAt DATETIME2(3) NOT NULL,
    AppliedBy NVARCHAR(200) NULL
);

INSERT dbo.SchemaVersions(VersionNumber, Description, AppliedAt, AppliedBy)
VALUES(N'1.8.0-003', N'D-A0b disposable prerequisite fixture', SYSUTCDATETIME(), N'CI');

CREATE TABLE dbo.Users
(
    UserId INT NOT NULL CONSTRAINT PK_DA0B_Users PRIMARY KEY
);

CREATE TABLE dbo.Teams
(
    TeamId INT NOT NULL CONSTRAINT PK_DA0B_Teams PRIMARY KEY
);

CREATE TABLE dbo.Locations
(
    LocationId INT NOT NULL CONSTRAINT PK_DA0B_Locations PRIMARY KEY,
    OrganizationId INT NOT NULL,
    LocationCode NVARCHAR(50) NOT NULL,
    LocationName NVARCHAR(200) NOT NULL,
    Address NVARCHAR(500) NULL,
    IsActive BIT NOT NULL
);

CREATE TABLE dbo.DeploymentSites
(
    DeploymentSiteId INT NOT NULL CONSTRAINT PK_DA0B_DeploymentSites PRIMARY KEY
);

CREATE TABLE dbo.DeploymentSiteLocationAssignments
(
    DeploymentSiteLocationAssignmentId BIGINT IDENTITY(1,1) NOT NULL
        CONSTRAINT PK_DA0B_DeploymentSiteLocationAssignments PRIMARY KEY,
    DeploymentSiteId INT NOT NULL,
    LocationId INT NOT NULL,
    EffectiveFrom DATE NOT NULL,
    EffectiveTo DATE NULL,
    ChangeReason NVARCHAR(500) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_DA0B_Assignment_CreatedAt DEFAULT(SYSUTCDATETIME()),
    CreatedByUserId INT NULL,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT FK_DA0B_Assignment_Site FOREIGN KEY(DeploymentSiteId) REFERENCES dbo.DeploymentSites(DeploymentSiteId),
    CONSTRAINT FK_DA0B_Assignment_Location FOREIGN KEY(LocationId) REFERENCES dbo.Locations(LocationId),
    CONSTRAINT CK_DA0B_Assignment_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
    CONSTRAINT UQ_DA0B_Assignment_Start UNIQUE(DeploymentSiteId, EffectiveFrom)
);

/* Match the relevant 1800_003 production index ordering; do not add a test-only LocationId-first index. */
CREATE INDEX IX_DeploymentSiteLocationAssignments_AsOf
    ON dbo.DeploymentSiteLocationAssignments(DeploymentSiteId, EffectiveFrom, EffectiveTo, LocationId);

CREATE TABLE dbo.VisitTripSnapshots
(
    VisitTripSnapshotId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DA0B_VisitTripSnapshots PRIMARY KEY
);

CREATE TABLE dbo.SchemaMigrationDataBaselines
(
    MigrationVersion NVARCHAR(50) NOT NULL CONSTRAINT PK_DA0B_SchemaMigrationDataBaselines PRIMARY KEY,
    VisitTripSnapshotCount BIGINT NOT NULL
);

INSERT dbo.SchemaMigrationDataBaselines(MigrationVersion, VisitTripSnapshotCount)
VALUES(N'1.8.0-001', 0);
