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
        THROW 53600, N'無法取得 FieldVisit Migration lock。', 1;

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
       OR NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-003')
        THROW 53601, N'尚未套用 prerequisite Migration 1.8.0-003。', 1;

    IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-004')
        THROW 53602, N'Migration 1.8.0-004 已套用，不得重複執行。', 1;

    IF OBJECT_ID(N'dbo.TeamLocationNotes', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.TeamLocationNoteHistory', N'U') IS NOT NULL
       OR COL_LENGTH(N'dbo.Locations', N'TaxId') IS NOT NULL
       OR COL_LENGTH(N'dbo.Locations', N'MasterNote') IS NOT NULL
       OR COL_LENGTH(N'dbo.Locations', N'InactivatedAt') IS NOT NULL
       OR COL_LENGTH(N'dbo.Locations', N'InactivatedByUserId') IS NOT NULL
       OR COL_LENGTH(N'dbo.Locations', N'DuplicateOfLocationId') IS NOT NULL
       OR COL_LENGTH(N'dbo.Locations', N'DuplicateReason') IS NOT NULL
       OR COL_LENGTH(N'dbo.Locations', N'NormalizedLocationName') IS NOT NULL
       OR COL_LENGTH(N'dbo.Locations', N'NormalizedAddress') IS NOT NULL
        THROW 53603, N'偵測到 1.8.0-004 部分物件或欄位已存在；請由 IT Review。', 1;

    ALTER TABLE dbo.Locations ADD
        TaxId NVARCHAR(20) NULL,
        MasterNote NVARCHAR(1000) NULL,
        InactivatedAt DATETIME2(3) NULL,
        InactivatedByUserId INT NULL,
        DuplicateOfLocationId INT NULL,
        DuplicateReason NVARCHAR(500) NULL;

    ALTER TABLE dbo.Locations ADD
        NormalizedLocationName AS
        (UPPER(REPLACE(REPLACE(LTRIM(RTRIM(LocationName)), N' ', N''), N'　', N''))) PERSISTED;

    ALTER TABLE dbo.Locations ADD
        NormalizedAddress AS
        (UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(Address, N''))), N' ', N''), N'　', N''))) PERSISTED;

    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.Locations WITH CHECK ADD
            CONSTRAINT FK_Locations_InactivatedByUser
                FOREIGN KEY(InactivatedByUserId) REFERENCES dbo.Users(UserId),
            CONSTRAINT FK_Locations_DuplicateOf
                FOREIGN KEY(DuplicateOfLocationId) REFERENCES dbo.Locations(LocationId),
            CONSTRAINT CK_Locations_DuplicateReference
                CHECK(DuplicateOfLocationId IS NULL OR DuplicateOfLocationId <> LocationId);';

    /* TaxId is searchable evidence, never a unique Location key. */
    EXEC sys.sp_executesql N'
        CREATE INDEX IX_Locations_Organization_TaxId
            ON dbo.Locations(OrganizationId, TaxId, IsActive)
            INCLUDE(LocationCode, LocationName, Address)
            WHERE TaxId IS NOT NULL;

        CREATE INDEX IX_Locations_NormalizedNameAddress
            ON dbo.Locations(OrganizationId, NormalizedLocationName, NormalizedAddress, IsActive)
            INCLUDE(LocationCode, LocationName, TaxId);

        CREATE INDEX IX_Locations_DuplicateOf
            ON dbo.Locations(DuplicateOfLocationId)
            WHERE DuplicateOfLocationId IS NOT NULL;';

    CREATE TABLE dbo.TeamLocationNotes
    (
        TeamLocationNoteId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TeamLocationNotes PRIMARY KEY,
        TeamId INT NOT NULL,
        LocationId INT NOT NULL,
        Note NVARCHAR(1000) NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_TeamLocationNotes_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId INT NOT NULL,
        UpdatedAt DATETIME2(3) NULL,
        UpdatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_TeamLocationNotes_Teams FOREIGN KEY(TeamId) REFERENCES dbo.Teams(TeamId),
        CONSTRAINT FK_TeamLocationNotes_Locations FOREIGN KEY(LocationId) REFERENCES dbo.Locations(LocationId),
        CONSTRAINT FK_TeamLocationNotes_CreatedByUser FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_TeamLocationNotes_UpdatedByUser FOREIGN KEY(UpdatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_TeamLocationNotes_NotBlank CHECK(Note IS NULL OR LEN(LTRIM(RTRIM(Note))) > 0),
        CONSTRAINT UQ_TeamLocationNotes_Team_Location UNIQUE(TeamId, LocationId)
    );
    CREATE INDEX IX_TeamLocationNotes_Location_Team
        ON dbo.TeamLocationNotes(LocationId, TeamId);

    CREATE TABLE dbo.TeamLocationNoteHistory
    (
        TeamLocationNoteHistoryId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TeamLocationNoteHistory PRIMARY KEY,
        TeamLocationNoteId BIGINT NOT NULL,
        TeamId INT NOT NULL,
        LocationId INT NOT NULL,
        Action NVARCHAR(20) NOT NULL,
        OldNote NVARCHAR(1000) NULL,
        NewNote NVARCHAR(1000) NULL,
        ChangeReason NVARCHAR(500) NULL,
        ChangedAt DATETIME2(3) NOT NULL CONSTRAINT DF_TeamLocationNoteHistory_ChangedAt DEFAULT(SYSUTCDATETIME()),
        ChangedByUserId INT NOT NULL,
        CONSTRAINT FK_TeamLocationNoteHistory_Note FOREIGN KEY(TeamLocationNoteId) REFERENCES dbo.TeamLocationNotes(TeamLocationNoteId),
        CONSTRAINT FK_TeamLocationNoteHistory_Team FOREIGN KEY(TeamId) REFERENCES dbo.Teams(TeamId),
        CONSTRAINT FK_TeamLocationNoteHistory_Location FOREIGN KEY(LocationId) REFERENCES dbo.Locations(LocationId),
        CONSTRAINT FK_TeamLocationNoteHistory_ChangedByUser FOREIGN KEY(ChangedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_TeamLocationNoteHistory_Action CHECK(Action IN(N'Created', N'Updated', N'Cleared'))
    );
    CREATE INDEX IX_TeamLocationNoteHistory_Note_Changed
        ON dbo.TeamLocationNoteHistory(TeamLocationNoteId, ChangedAt DESC);

    DECLARE @BusinessToday date =
        CONVERT(date, DATEADD(HOUR, 8, SYSUTCDATETIME()));

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Locations l
        JOIN dbo.DeploymentSiteLocationAssignments a
          ON a.LocationId = l.LocationId
        WHERE l.IsActive = 0
          AND
          (
              a.EffectiveTo IS NULL
              OR a.EffectiveTo >= @BusinessToday
          )
    )
        THROW 53604, N'1.8.0-004 preflight failed: inactive Location 仍有 current/future Deployment Site Location assignment。', 1;

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_Locations_ProtectCurrentDeploymentSiteLocations
        ON dbo.Locations AFTER UPDATE AS
        BEGIN
            SET NOCOUNT ON;

            IF NOT EXISTS
            (
                SELECT 1
                FROM inserted i
                JOIN deleted d ON d.LocationId = i.LocationId
                WHERE d.IsActive = 1
                  AND i.IsActive = 0
            )
                RETURN;

            DECLARE @InvariantLockResult INT;
            EXEC @InvariantLockResult = sys.sp_getapplock
                @Resource = N''FieldVisit.LocationDeploymentAssignmentActiveInvariant'',
                @LockMode = N''Exclusive'',
                @LockOwner = N''Transaction'',
                @LockTimeout = 10000;

            IF @InvariantLockResult < 0
                THROW 53605, N''無法取得 Location deployment-assignment active invariant lock。'', 1;

            DECLARE @BusinessToday date =
                CONVERT(date, DATEADD(HOUR, 8, SYSUTCDATETIME()));

            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                JOIN deleted d ON d.LocationId = i.LocationId
                JOIN dbo.DeploymentSiteLocationAssignments a
                  ON a.LocationId = i.LocationId
                WHERE d.IsActive = 1
                  AND i.IsActive = 0
                  AND
                  (
                      a.EffectiveTo IS NULL
                      OR a.EffectiveTo >= @BusinessToday
                  )
            )
                THROW 53606, N''Location 仍有 current/future Deployment Site Location assignment，不得停用。'', 1;
        END;';

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_DeploymentSiteLocationAssignments_ProtectActiveLocation
        ON dbo.DeploymentSiteLocationAssignments AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;

            DECLARE @InvariantLockResult INT;
            EXEC @InvariantLockResult = sys.sp_getapplock
                @Resource = N''FieldVisit.LocationDeploymentAssignmentActiveInvariant'',
                @LockMode = N''Exclusive'',
                @LockOwner = N''Transaction'',
                @LockTimeout = 10000;

            IF @InvariantLockResult < 0
                THROW 53607, N''無法取得 Location deployment-assignment active invariant lock。'', 1;

            DECLARE @BusinessToday date =
                CONVERT(date, DATEADD(HOUR, 8, SYSUTCDATETIME()));

            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                JOIN dbo.Locations l ON l.LocationId = i.LocationId
                WHERE l.IsActive = 0
                  AND
                  (
                      i.EffectiveTo IS NULL
                      OR i.EffectiveTo >= @BusinessToday
                  )
            )
                THROW 53608, N''current/future Deployment Site Location assignment 不得引用 inactive Location。'', 1;
        END;';

    INSERT dbo.SchemaVersions(VersionNumber, Description, AppliedAt, AppliedBy)
    VALUES
    (
        N'1.8.0-004',
        N'Location TaxId/master note/inactivation/duplicate governance and audited Team Location Notes',
        SYSUTCDATETIME(),
        N'v1.8.0 Post-UAT'
    );

    COMMIT TRANSACTION;
    PRINT N'1.8.0-004 location governance completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
