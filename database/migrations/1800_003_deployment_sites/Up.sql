SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @LockResult INT;
    EXEC @LockResult = sys.sp_getapplock
        @Resource = N'FieldVisit.SchemaMigration',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 0;

    IF @LockResult < 0
        THROW 53400, N'無法取得 FieldVisit Migration lock。', 1;

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
       OR NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-002')
        THROW 53401, N'尚未套用 prerequisite Migration 1.8.0-002。', 1;

    IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-003')
        THROW 53402, N'Migration 1.8.0-003 已套用，不得重複執行。', 1;

    IF OBJECT_ID(N'dbo.DeploymentSites', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.DeploymentSiteLocationAssignments', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.TeamDeploymentSiteAssignments', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.EmploymentDeploymentSiteAssignments', N'U') IS NOT NULL
        THROW 53403, N'偵測到 1.8.0-003 部分物件已存在；請由 IT Review。', 1;

    IF COL_LENGTH(N'dbo.VisitTrips', N'StartDeploymentSiteId') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTrips', N'EndDeploymentSiteId') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'StartDeploymentSiteIdSnapshot') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'EndDeploymentSiteIdSnapshot') IS NOT NULL
        THROW 53404, N'偵測到 1.8.0-003 部分欄位已存在；請由 IT Review。', 1;

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
    CREATE INDEX IX_DeploymentSites_Center_Effective
        ON dbo.DeploymentSites(CenterId, IsActive, EffectiveFrom, EffectiveTo)
        INCLUDE(SiteCode, SiteName);

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_DeploymentSites_CenterPeriod
        ON dbo.DeploymentSites AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                JOIN dbo.Centers c ON c.CenterId = i.CenterId
                WHERE i.EffectiveFrom < c.EffectiveFrom
                   OR ISNULL(i.EffectiveTo, CONVERT(date, N''99991231'')) > ISNULL(c.EffectiveTo, CONVERT(date, N''99991231''))
            )
                THROW 53419, N''Deployment Site 有效期間必須位於所屬 Center 有效期間內。'', 1;
        END;';

    /* A relocation closes one Location assignment and opens another; it never overwrites old history. */
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
    CREATE INDEX IX_DeploymentSiteLocationAssignments_AsOf
        ON dbo.DeploymentSiteLocationAssignments(DeploymentSiteId, EffectiveFrom, EffectiveTo, LocationId);

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
    CREATE INDEX IX_TeamDeploymentSiteAssignments_Team_AsOf
        ON dbo.TeamDeploymentSiteAssignments(TeamId, EffectiveFrom, EffectiveTo, DeploymentSiteId);

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
    CREATE INDEX IX_EmploymentDeploymentSiteAssignments_AsOf
        ON dbo.EmploymentDeploymentSiteAssignments(EmploymentId, EffectiveFrom, EffectiveTo, IsPrimary, DeploymentSiteId);

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_DeploymentSiteLocations_NoOverlap
        ON dbo.DeploymentSiteLocationAssignments AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                JOIN dbo.DeploymentSites s ON s.DeploymentSiteId = i.DeploymentSiteId
                WHERE i.EffectiveFrom < s.EffectiveFrom
                   OR ISNULL(i.EffectiveTo, CONVERT(date, N''99991231'')) > ISNULL(s.EffectiveTo, CONVERT(date, N''99991231''))
            )
                THROW 53423, N''Deployment Site Location period 必須位於 Site 有效期間內。'', 1;

            IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DeploymentSiteLocationAssignments x
                ON x.DeploymentSiteId=i.DeploymentSiteId
               AND x.DeploymentSiteLocationAssignmentId<>i.DeploymentSiteLocationAssignmentId
               AND i.EffectiveFrom<=ISNULL(x.EffectiveTo,CONVERT(date,N''99991231''))
               AND x.EffectiveFrom<=ISNULL(i.EffectiveTo,CONVERT(date,N''99991231'')))
                THROW 53420, N''Deployment Site 同期間只能有一個 Location。'', 1;
        END;';

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_TeamDeploymentSites_NoOverlap
        ON dbo.TeamDeploymentSiteAssignments AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                JOIN dbo.DeploymentSites s ON s.DeploymentSiteId = i.DeploymentSiteId
                WHERE i.EffectiveFrom < s.EffectiveFrom
                   OR ISNULL(i.EffectiveTo, CONVERT(date, N''99991231'')) > ISNULL(s.EffectiveTo, CONVERT(date, N''99991231''))
                   OR NOT EXISTS
                      (
                          SELECT 1
                          FROM dbo.TeamCenterAssignments tc
                          WHERE tc.TeamId = i.TeamId
                            AND tc.CenterId = s.CenterId
                            AND tc.EffectiveFrom <= i.EffectiveFrom
                            AND ISNULL(tc.EffectiveTo, CONVERT(date, N''99991231'')) >= ISNULL(i.EffectiveTo, CONVERT(date, N''99991231''))
                      )
            )
                THROW 53424, N''Team-Site 必須屬於同 Center，且 assignment period 必須完整有效。'', 1;

            IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.TeamDeploymentSiteAssignments x
                ON x.TeamId=i.TeamId AND x.DeploymentSiteId=i.DeploymentSiteId
               AND x.TeamDeploymentSiteAssignmentId<>i.TeamDeploymentSiteAssignmentId
               AND i.EffectiveFrom<=ISNULL(x.EffectiveTo,CONVERT(date,N''99991231''))
               AND x.EffectiveFrom<=ISNULL(i.EffectiveTo,CONVERT(date,N''99991231'')))
                THROW 53421, N''相同 Team/Site effective periods 不得重疊。'', 1;
        END;';

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_EmploymentDeploymentSites_OnePrimary
        ON dbo.EmploymentDeploymentSiteAssignments AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                JOIN dbo.DeploymentSites s ON s.DeploymentSiteId = i.DeploymentSiteId
                WHERE i.EffectiveFrom < s.EffectiveFrom
                   OR ISNULL(i.EffectiveTo, CONVERT(date, N''99991231'')) > ISNULL(s.EffectiveTo, CONVERT(date, N''99991231''))
            )
                THROW 53425, N''Employment-Site period 必須位於 Site 有效期間內。'', 1;

            IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.EmploymentDeploymentSiteAssignments x
                ON x.EmploymentId=i.EmploymentId
               AND x.EmploymentDeploymentSiteAssignmentId<>i.EmploymentDeploymentSiteAssignmentId
               AND i.IsPrimary=1 AND x.IsPrimary=1
               AND i.EffectiveFrom<=ISNULL(x.EffectiveTo,CONVERT(date,N''99991231''))
               AND x.EffectiveFrom<=ISNULL(i.EffectiveTo,CONVERT(date,N''99991231'')))
                THROW 53422, N''同一 Employment 同期間只能有一個 Primary Deployment Site。'', 1;
        END;';

    ALTER TABLE dbo.VisitTrips ADD
        StartDeploymentSiteId INT NULL,
        EndDeploymentSiteId INT NULL;
    ALTER TABLE dbo.VisitTrips WITH CHECK ADD
        CONSTRAINT FK_VisitTrips_StartDeploymentSite FOREIGN KEY(StartDeploymentSiteId) REFERENCES dbo.DeploymentSites(DeploymentSiteId),
        CONSTRAINT FK_VisitTrips_EndDeploymentSite FOREIGN KEY(EndDeploymentSiteId) REFERENCES dbo.DeploymentSites(DeploymentSiteId);

    ALTER TABLE dbo.VisitTripSnapshots ADD
        StartDeploymentSiteIdSnapshot INT NULL,
        StartDeploymentSiteCodeSnapshot NVARCHAR(50) NULL,
        StartDeploymentSiteNameSnapshot NVARCHAR(200) NULL,
        StartDeploymentLocationIdSnapshot INT NULL,
        StartDeploymentAddressSnapshot NVARCHAR(500) NULL,
        EndDeploymentSiteIdSnapshot INT NULL,
        EndDeploymentSiteCodeSnapshot NVARCHAR(50) NULL,
        EndDeploymentSiteNameSnapshot NVARCHAR(200) NULL,
        EndDeploymentLocationIdSnapshot INT NULL,
        EndDeploymentAddressSnapshot NVARCHAR(500) NULL;

    INSERT dbo.SchemaVersions(VersionNumber, Description, AppliedAt, AppliedBy)
    VALUES
    (
        N'1.8.0-003',
        N'Deployment Site lifecycle, relocation history, Team/Employment assignments and additive Trip snapshot fields',
        SYSUTCDATETIME(),
        N'v1.8.0 Post-UAT'
    );

    COMMIT TRANSACTION;
    PRINT N'1.8.0-003 deployment sites completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
