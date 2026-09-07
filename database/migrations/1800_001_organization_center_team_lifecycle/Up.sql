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
        THROW 53000, N'無法取得 FieldVisit Migration lock；不得與其他 Migration 同時執行。', 1;

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
        THROW 53001, N'找不到 dbo.SchemaVersions。', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-008'
    )
        THROW 53002, N'尚未套用 prerequisite Migration 1.7.0-008。', 1;

    IF EXISTS
    (
        SELECT 1 FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.8.0-001'
    )
        THROW 53003, N'Migration 1.8.0-001 已套用，不得重複執行。', 1;

    IF OBJECT_ID(N'dbo.Organizations', N'U') IS NULL
       OR OBJECT_ID(N'dbo.Teams', N'U') IS NULL
       OR OBJECT_ID(N'dbo.Users', N'U') IS NULL
       OR OBJECT_ID(N'dbo.VisitTrips', N'U') IS NULL
       OR OBJECT_ID(N'dbo.VisitTripSnapshots', N'U') IS NULL
       OR OBJECT_ID(N'dbo.VisitTripSnapshotStops', N'U') IS NULL
        THROW 53004, N'v1.7.2 核心資料表不完整；停止 Migration。', 1;

    IF OBJECT_ID(N'dbo.SchemaMigrationDataBaselines', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.Centers', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.TeamCenterAssignments', N'U') IS NOT NULL
        THROW 53005, N'偵測到 1.8.0-001 部分物件已存在；請由 IT Review，不得覆蓋。', 1;

    IF COL_LENGTH(N'dbo.Organizations', N'Notes') IS NOT NULL
       OR COL_LENGTH(N'dbo.Organizations', N'InactivatedAt') IS NOT NULL
       OR COL_LENGTH(N'dbo.Organizations', N'InactivatedByUserId') IS NOT NULL
       OR COL_LENGTH(N'dbo.Organizations', N'RowVersion') IS NOT NULL
       OR COL_LENGTH(N'dbo.Teams', N'EffectiveFrom') IS NOT NULL
       OR COL_LENGTH(N'dbo.Teams', N'EffectiveTo') IS NOT NULL
       OR COL_LENGTH(N'dbo.Teams', N'Notes') IS NOT NULL
       OR COL_LENGTH(N'dbo.Teams', N'InactivatedByUserId') IS NOT NULL
       OR COL_LENGTH(N'dbo.Teams', N'RowVersion') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'CenterIdSnapshot') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'CenterCodeSnapshot') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'CenterNameSnapshot') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'TeamCodeSnapshot') IS NOT NULL
        THROW 53006, N'偵測到 1.8.0-001 部分欄位已存在；請由 IT Review，不得覆蓋。', 1;

    CREATE TABLE dbo.SchemaMigrationDataBaselines
    (
        MigrationVersion NVARCHAR(30) NOT NULL
            CONSTRAINT PK_SchemaMigrationDataBaselines PRIMARY KEY,
        CapturedAt DATETIME2(3) NOT NULL,
        MaxVisitTripId BIGINT NOT NULL,
        VisitTripCount BIGINT NOT NULL,
        VisitTripHash VARBINARY(32) NOT NULL,
        MaxVisitTripSnapshotId BIGINT NOT NULL,
        VisitTripSnapshotCount BIGINT NOT NULL,
        VisitTripSnapshotHash VARBINARY(32) NOT NULL,
        MaxVisitTripSnapshotStopId BIGINT NOT NULL,
        VisitTripSnapshotStopCount BIGINT NOT NULL,
        VisitTripSnapshotStopHash VARBINARY(32) NOT NULL
    );

    DECLARE
        @MaxTripId BIGINT = ISNULL((SELECT MAX(VisitTripId) FROM dbo.VisitTrips), 0),
        @MaxSnapshotId BIGINT = ISNULL((SELECT MAX(VisitTripSnapshotId) FROM dbo.VisitTripSnapshots), 0),
        @MaxSnapshotStopId BIGINT = ISNULL((SELECT MAX(VisitTripSnapshotStopId) FROM dbo.VisitTripSnapshotStops), 0);

    INSERT dbo.SchemaMigrationDataBaselines
    (
        MigrationVersion, CapturedAt,
        MaxVisitTripId, VisitTripCount, VisitTripHash,
        MaxVisitTripSnapshotId, VisitTripSnapshotCount, VisitTripSnapshotHash,
        MaxVisitTripSnapshotStopId, VisitTripSnapshotStopCount, VisitTripSnapshotStopHash
    )
    SELECT
        N'1.8.0-001', SYSUTCDATETIME(),
        @MaxTripId,
        (SELECT COUNT_BIG(*) FROM dbo.VisitTrips WHERE VisitTripId <= @MaxTripId),
        HASHBYTES(N'SHA2_256', CONVERT(VARBINARY(MAX), COALESCE((
            SELECT VisitTripId, TripNo, UserId, OrganizationId, TeamId, VisitDate,
                   StartTime, EndTime, HasTimeOverlapWarning, TimeOverlapConfirmed,
                   Status, VehicleType, Purpose, Notes, ReturnReason, SubmittedAt,
                   ApprovedAt, CreatedAt, CreatedByUserId, UpdatedAt, UpdatedByUserId
            FROM dbo.VisitTrips
            WHERE VisitTripId <= @MaxTripId
            ORDER BY VisitTripId
            FOR JSON PATH, INCLUDE_NULL_VALUES
        ), N'[]'))),
        @MaxSnapshotId,
        (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshots WHERE VisitTripSnapshotId <= @MaxSnapshotId),
        HASHBYTES(N'SHA2_256', CONVERT(VARBINARY(MAX), COALESCE((
            SELECT VisitTripSnapshotId, VisitTripId, SnapshotVersion, SnapshotType,
                   TripNo, UserId, EmployeeNoSnapshot, DisplayNameSnapshot,
                   OrganizationId, OrganizationNameSnapshot, TeamId, TeamNameSnapshot,
                   VisitDate, StartTime, EndTime, StatusSnapshot, VehicleTypeSnapshot,
                   ClaimedDistanceKmSnapshot, SystemDistanceKmSnapshot,
                   ApprovedDistanceKmSnapshot, RatePerKmSnapshot, SubsidyAmountSnapshot,
                   RouteProviderSnapshot, SubmittedAtSnapshot, ApprovedAtSnapshot,
                   ApproverUserId, ApproverNameSnapshot, NotesSnapshot, CreatedAt,
                   CreatedByUserId
            FROM dbo.VisitTripSnapshots
            WHERE VisitTripSnapshotId <= @MaxSnapshotId
            ORDER BY VisitTripSnapshotId
            FOR JSON PATH, INCLUDE_NULL_VALUES
        ), N'[]'))),
        @MaxSnapshotStopId,
        (SELECT COUNT_BIG(*) FROM dbo.VisitTripSnapshotStops WHERE VisitTripSnapshotStopId <= @MaxSnapshotStopId),
        HASHBYTES(N'SHA2_256', CONVERT(VARBINARY(MAX), COALESCE((
            SELECT VisitTripSnapshotStopId, VisitTripSnapshotId, StopSequence, LocationId,
                   LocationCodeSnapshot, LocationNameSnapshot, AddressSnapshot,
                   ProjectId, ProjectCodeSnapshot, ProjectNameSnapshot, VisitTypeId,
                   VisitTypeCodeSnapshot, VisitTypeNameSnapshot, VisitPurposeSnapshot,
                   NotesSnapshot, CreatedAt
            FROM dbo.VisitTripSnapshotStops
            WHERE VisitTripSnapshotStopId <= @MaxSnapshotStopId
            ORDER BY VisitTripSnapshotStopId
            FOR JSON PATH, INCLUDE_NULL_VALUES
        ), N'[]')));

    IF EXISTS
    (
        SELECT OrganizationId, TeamCode
        FROM dbo.Teams
        GROUP BY OrganizationId, TeamCode
        HAVING COUNT(*) > 1
    )
        THROW 53007, N'Teams 存在 Organization 內重複 TeamCode；停止 Migration，不自動改碼。', 1;

    ALTER TABLE dbo.Organizations ADD
        Notes NVARCHAR(1000) NULL,
        InactivatedAt DATETIME2(3) NULL,
        InactivatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL;

    ALTER TABLE dbo.Organizations WITH CHECK ADD
        CONSTRAINT FK_Organizations_InactivatedByUser
        FOREIGN KEY(InactivatedByUserId) REFERENCES dbo.Users(UserId);

    ALTER TABLE dbo.Teams ADD
        EffectiveFrom DATE NULL,
        EffectiveTo DATE NULL,
        Notes NVARCHAR(1000) NULL,
        InactivatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL;

    ALTER TABLE dbo.Teams WITH CHECK ADD
        CONSTRAINT CK_Teams_EffectiveDates
            CHECK(EffectiveTo IS NULL OR EffectiveFrom IS NULL OR EffectiveTo >= EffectiveFrom),
        CONSTRAINT FK_Teams_InactivatedByUser
            FOREIGN KEY(InactivatedByUserId) REFERENCES dbo.Users(UserId);

    CREATE UNIQUE INDEX UX_Teams_Organization_TeamCode
        ON dbo.Teams(OrganizationId, TeamCode);

    CREATE INDEX IX_Teams_Organization_Effective
        ON dbo.Teams(OrganizationId, IsActive, EffectiveFrom, EffectiveTo)
        INCLUDE(TeamCode, TeamName);

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

    CREATE INDEX IX_Centers_Organization_Effective
        ON dbo.Centers(OrganizationId, IsActive, EffectiveFrom, EffectiveTo)
        INCLUDE(CenterCode, CenterName);

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

    CREATE INDEX IX_TeamCenterAssignments_Center_Effective
        ON dbo.TeamCenterAssignments(CenterId, EffectiveFrom, EffectiveTo, TeamId);

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_TeamCenterAssignments_NoOverlap
        ON dbo.TeamCenterAssignments
        AFTER INSERT, UPDATE
        AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                JOIN dbo.Teams t ON t.TeamId = i.TeamId
                JOIN dbo.Centers c ON c.CenterId = i.CenterId
                WHERE t.OrganizationId <> c.OrganizationId
                   OR (t.EffectiveFrom IS NOT NULL AND i.EffectiveFrom < t.EffectiveFrom)
                   OR (t.EffectiveTo IS NOT NULL AND ISNULL(i.EffectiveTo, CONVERT(date, N''99991231'')) > t.EffectiveTo)
                   OR i.EffectiveFrom < c.EffectiveFrom
                   OR ISNULL(i.EffectiveTo, CONVERT(date, N''99991231'')) > ISNULL(c.EffectiveTo, CONVERT(date, N''99991231''))
            )
                THROW 53019, N''Team-Center 必須同 Organization，且 assignment period 必須位於 Team/Center 有效期間內。'', 1;

            IF EXISTS
            (
                SELECT 1
                FROM inserted i
                JOIN dbo.TeamCenterAssignments x
                  ON x.TeamId = i.TeamId
                 AND x.TeamCenterAssignmentId <> i.TeamCenterAssignmentId
                 AND i.EffectiveFrom <= ISNULL(x.EffectiveTo, CONVERT(date, N''99991231''))
                 AND x.EffectiveFrom <= ISNULL(i.EffectiveTo, CONVERT(date, N''99991231''))
            )
                THROW 53020, N''Team-Center effective periods 不得重疊。'', 1;
        END;';

    ALTER TABLE dbo.VisitTripSnapshots ADD
        CenterIdSnapshot INT NULL,
        CenterCodeSnapshot NVARCHAR(50) NULL,
        CenterNameSnapshot NVARCHAR(200) NULL,
        TeamCodeSnapshot NVARCHAR(50) NULL;

    INSERT dbo.SchemaVersions(VersionNumber, Description, AppliedAt, AppliedBy)
    VALUES
    (
        N'1.8.0-001',
        N'Organization, Center and Team lifecycle with effective Team-Center history and additive snapshot fields',
        SYSUTCDATETIME(),
        N'v1.8.0 Post-UAT'
    );

    COMMIT TRANSACTION;
    PRINT N'1.8.0-001 organization / center / team lifecycle completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
