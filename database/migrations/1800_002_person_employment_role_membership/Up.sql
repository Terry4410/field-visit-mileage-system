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
        THROW 53200, N'無法取得 FieldVisit Migration lock。', 1;

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
       OR NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-001')
        THROW 53201, N'尚未套用 prerequisite Migration 1.8.0-001。', 1;

    IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber = N'1.8.0-002')
        THROW 53202, N'Migration 1.8.0-002 已套用，不得重複執行。', 1;

    IF OBJECT_ID(N'dbo.Persons', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.Employments', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.EmploymentStatusPeriods', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.EmploymentRoleAssignments', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.TeamMemberships', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.TeamLeaderAssignments', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.TeamLeaderDelegations', N'U') IS NOT NULL
        THROW 53203, N'偵測到 1.8.0-002 部分物件已存在；請由 IT Review。', 1;

    IF OBJECT_ID(N'dbo.UserIdentityProfiles', N'U') IS NULL
       OR OBJECT_ID(N'dbo.UserEmploymentPeriods', N'U') IS NULL
       OR OBJECT_ID(N'dbo.UserRoleAssignments', N'U') IS NULL
       OR OBJECT_ID(N'dbo.UserTeamAssignments', N'U') IS NULL
        THROW 53204, N'v1.7 identity/access 相容層不完整；停止 Migration。', 1;

    IF COL_LENGTH(N'dbo.UserIdentityProfiles', N'EmploymentId') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTrips', N'EmploymentId') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'PersonIdSnapshot') IS NOT NULL
       OR COL_LENGTH(N'dbo.VisitTripSnapshots', N'EmploymentIdSnapshot') IS NOT NULL
        THROW 53205, N'偵測到 1.8.0-002 部分欄位已存在；請由 IT Review。', 1;

    /* Refuse ambiguous legacy periods. Do not repair business dates automatically. */
    IF EXISTS
    (
        SELECT 1
        FROM dbo.UserEmploymentPeriods a
        JOIN dbo.UserEmploymentPeriods b
          ON b.UserId = a.UserId
         AND b.UserEmploymentPeriodId > a.UserEmploymentPeriodId
         AND a.EffectiveFrom <= ISNULL(b.EffectiveTo, CONVERT(date, N'99991231'))
         AND b.EffectiveFrom <= ISNULL(a.EffectiveTo, CONVERT(date, N'99991231'))
    )
        THROW 53206, N'Legacy Employment periods 存在重疊；停止 Migration，不自動修正。', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.UserRoleAssignments a
        JOIN dbo.UserRoleAssignments b
          ON b.UserId = a.UserId
         AND b.RoleId = a.RoleId
         AND b.UserRoleAssignmentId > a.UserRoleAssignmentId
         AND a.EffectiveFrom <= ISNULL(b.EffectiveTo, CONVERT(date, N'99991231'))
         AND b.EffectiveFrom <= ISNULL(a.EffectiveTo, CONVERT(date, N'99991231'))
    )
        THROW 53207, N'Legacy Role assignments 存在同角色期間重疊；停止 Migration。', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.UserTeamAssignments a
        JOIN dbo.UserTeamAssignments b
          ON b.UserId = a.UserId
         AND b.UserTeamAssignmentId > a.UserTeamAssignmentId
         AND a.IsPrimary = 1
         AND b.IsPrimary = 1
         AND a.EffectiveFrom <= ISNULL(b.EffectiveTo, CONVERT(date, N'99991231'))
         AND b.EffectiveFrom <= ISNULL(a.EffectiveTo, CONVERT(date, N'99991231'))
    )
        THROW 53208, N'Legacy Team assignments 存在 Primary Team 期間重疊；停止 Migration。', 1;

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

    CREATE UNIQUE INDEX UX_Persons_LegacyUserId
        ON dbo.Persons(LegacyUserId) WHERE LegacyUserId IS NOT NULL;

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

    CREATE UNIQUE INDEX UX_Employments_Organization_EmployeeNo
        ON dbo.Employments(OrganizationId, EmployeeNo) WHERE EmployeeNo IS NOT NULL;
    CREATE UNIQUE INDEX UX_Employments_LegacyUserId
        ON dbo.Employments(LegacyUserId) WHERE LegacyUserId IS NOT NULL;
    CREATE INDEX IX_Employments_Person ON dbo.Employments(PersonId, HireDate, TerminationDate);
    CREATE INDEX IX_Employments_Email ON dbo.Employments(Email) WHERE Email IS NOT NULL;

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
    CREATE INDEX IX_EmploymentStatusPeriods_AsOf
        ON dbo.EmploymentStatusPeriods(EmploymentId, EffectiveFrom, EffectiveTo, EmploymentStatus);

    CREATE TABLE dbo.EmploymentRoleAssignments
    (
        EmploymentRoleAssignmentId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmploymentRoleAssignments PRIMARY KEY,
        EmploymentId BIGINT NOT NULL,
        RoleId INT NOT NULL,
        EffectiveFrom DATE NOT NULL,
        EffectiveTo DATE NULL,
        AssignedByUserId INT NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_EmploymentRoleAssignments_CreatedAt DEFAULT(SYSUTCDATETIME()),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_EmploymentRoleAssignments_Employments FOREIGN KEY(EmploymentId) REFERENCES dbo.Employments(EmploymentId),
        CONSTRAINT FK_EmploymentRoleAssignments_Roles FOREIGN KEY(RoleId) REFERENCES dbo.Roles(RoleId),
        CONSTRAINT FK_EmploymentRoleAssignments_AssignedByUser FOREIGN KEY(AssignedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_EmploymentRoleAssignments_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
        CONSTRAINT UQ_EmploymentRoleAssignments_Start UNIQUE(EmploymentId, RoleId, EffectiveFrom)
    );
    CREATE INDEX IX_EmploymentRoleAssignments_AsOf
        ON dbo.EmploymentRoleAssignments(EmploymentId, EffectiveFrom, EffectiveTo, RoleId);

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
    CREATE INDEX IX_TeamMemberships_Employment_AsOf
        ON dbo.TeamMemberships(EmploymentId, EffectiveFrom, EffectiveTo, IsPrimary, TeamId);
    CREATE INDEX IX_TeamMemberships_Team_AsOf
        ON dbo.TeamMemberships(TeamId, EffectiveFrom, EffectiveTo, EmploymentId);

    CREATE TABLE dbo.TeamLeaderAssignments
    (
        TeamLeaderAssignmentId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TeamLeaderAssignments PRIMARY KEY,
        TeamId INT NOT NULL,
        EmploymentId BIGINT NOT NULL,
        EffectiveFrom DATE NOT NULL,
        EffectiveTo DATE NULL,
        AssignedByUserId INT NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_TeamLeaderAssignments_CreatedAt DEFAULT(SYSUTCDATETIME()),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_TeamLeaderAssignments_Teams FOREIGN KEY(TeamId) REFERENCES dbo.Teams(TeamId),
        CONSTRAINT FK_TeamLeaderAssignments_Employments FOREIGN KEY(EmploymentId) REFERENCES dbo.Employments(EmploymentId),
        CONSTRAINT FK_TeamLeaderAssignments_AssignedByUser FOREIGN KEY(AssignedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_TeamLeaderAssignments_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),
        CONSTRAINT UQ_TeamLeaderAssignments_Start UNIQUE(TeamId, EmploymentId, EffectiveFrom)
    );
    CREATE INDEX IX_TeamLeaderAssignments_Team_AsOf
        ON dbo.TeamLeaderAssignments(TeamId, EffectiveFrom, EffectiveTo, EmploymentId);

    CREATE TABLE dbo.TeamLeaderDelegations
    (
        TeamLeaderDelegationId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TeamLeaderDelegations PRIMARY KEY,
        TeamLeaderAssignmentId BIGINT NOT NULL,
        DelegateEmploymentId BIGINT NOT NULL,
        EffectiveFrom DATE NOT NULL,
        EffectiveTo DATE NOT NULL,
        Reason NVARCHAR(500) NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_TeamLeaderDelegations_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CreatedByUserId INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_TeamLeaderDelegations_LeaderAssignment FOREIGN KEY(TeamLeaderAssignmentId) REFERENCES dbo.TeamLeaderAssignments(TeamLeaderAssignmentId),
        CONSTRAINT FK_TeamLeaderDelegations_DelegateEmployment FOREIGN KEY(DelegateEmploymentId) REFERENCES dbo.Employments(EmploymentId),
        CONSTRAINT FK_TeamLeaderDelegations_CreatedByUser FOREIGN KEY(CreatedByUserId) REFERENCES dbo.Users(UserId),
        CONSTRAINT CK_TeamLeaderDelegations_Dates CHECK(EffectiveTo >= EffectiveFrom),
        CONSTRAINT UQ_TeamLeaderDelegations_Start UNIQUE(TeamLeaderAssignmentId, DelegateEmploymentId, EffectiveFrom)
    );
    CREATE INDEX IX_TeamLeaderDelegations_AsOf
        ON dbo.TeamLeaderDelegations(DelegateEmploymentId, EffectiveFrom, EffectiveTo, TeamLeaderAssignmentId);

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_EmploymentStatusPeriods_NoOverlap
        ON dbo.EmploymentStatusPeriods AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.EmploymentStatusPeriods x
                ON x.EmploymentId=i.EmploymentId AND x.EmploymentStatusPeriodId<>i.EmploymentStatusPeriodId
               AND i.EffectiveFrom<=ISNULL(x.EffectiveTo,CONVERT(date,N''99991231''))
               AND x.EffectiveFrom<=ISNULL(i.EffectiveTo,CONVERT(date,N''99991231'')))
                THROW 53220, N''Employment status effective periods 不得重疊。'', 1;
        END;';

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_EmploymentRoleAssignments_NoOverlap
        ON dbo.EmploymentRoleAssignments AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.EmploymentRoleAssignments x
                ON x.EmploymentId=i.EmploymentId AND x.RoleId=i.RoleId
               AND x.EmploymentRoleAssignmentId<>i.EmploymentRoleAssignmentId
               AND i.EffectiveFrom<=ISNULL(x.EffectiveTo,CONVERT(date,N''99991231''))
               AND x.EffectiveFrom<=ISNULL(i.EffectiveTo,CONVERT(date,N''99991231'')))
                THROW 53221, N''同一 Employment 的相同 Role effective periods 不得重疊。'', 1;
        END;';

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_TeamMemberships_OnePrimary
        ON dbo.TeamMemberships AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.TeamMemberships x
                ON x.EmploymentId=i.EmploymentId
               AND x.TeamMembershipId<>i.TeamMembershipId
               AND i.IsPrimary=1 AND x.IsPrimary=1
               AND i.EffectiveFrom<=ISNULL(x.EffectiveTo,CONVERT(date,N''99991231''))
               AND x.EffectiveFrom<=ISNULL(i.EffectiveTo,CONVERT(date,N''99991231'')))
                THROW 53222, N''同一 Employment 同期間只能有一個 Primary Team。'', 1;
        END;';

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_TeamLeaderAssignments_NoOverlap
        ON dbo.TeamLeaderAssignments AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.TeamLeaderAssignments x
                ON x.TeamId=i.TeamId AND x.EmploymentId=i.EmploymentId
               AND x.TeamLeaderAssignmentId<>i.TeamLeaderAssignmentId
               AND i.EffectiveFrom<=ISNULL(x.EffectiveTo,CONVERT(date,N''99991231''))
               AND x.EffectiveFrom<=ISNULL(i.EffectiveTo,CONVERT(date,N''99991231'')))
                THROW 53223, N''同一 Team/Leader effective periods 不得重疊。'', 1;
        END;';

    EXEC sys.sp_executesql N'
        CREATE TRIGGER dbo.TR_TeamLeaderDelegations_NoOverlap
        ON dbo.TeamLeaderDelegations AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.TeamLeaderDelegations x
                ON x.TeamLeaderAssignmentId=i.TeamLeaderAssignmentId
               AND x.DelegateEmploymentId=i.DelegateEmploymentId
               AND x.TeamLeaderDelegationId<>i.TeamLeaderDelegationId
               AND i.EffectiveFrom<=x.EffectiveTo AND x.EffectiveFrom<=i.EffectiveTo)
                THROW 53224, N''相同 Leader/Delegate delegation periods 不得重疊。'', 1;
        END;';

    /* One legacy User becomes one Person and one Employment. Names are never used to merge people. */
    INSERT dbo.Persons(DisplayName, LegacyUserId, CreatedAt)
    SELECT u.DisplayName, u.UserId, SYSUTCDATETIME()
    FROM dbo.Users u;

    INSERT dbo.Employments
    (
        PersonId, OrganizationId, EmployeeNo, Email, HireDate, TerminationDate,
        LegacyUserId, SourceType, SourceReference, CreatedAt
    )
    SELECT p.PersonId, u.OrganizationId, u.EmployeeNo, u.Email, NULL, NULL,
           u.UserId, N'LegacyUser', CONCAT(N'Users:', u.UserId), SYSUTCDATETIME()
    FROM dbo.Users u
    JOIN dbo.Persons p ON p.LegacyUserId = u.UserId
    WHERE u.OrganizationId IS NOT NULL;

    INSERT dbo.EmploymentStatusPeriods
    (
        EmploymentId, EmploymentStatus, EffectiveFrom, EffectiveTo,
        SourceType, SourceReference, CreatedAt
    )
    SELECT e.EmploymentId, p.EmploymentStatus, p.EffectiveFrom, p.EffectiveTo,
           p.SourceType, p.SourceReference, SYSUTCDATETIME()
    FROM dbo.UserEmploymentPeriods p
    JOIN dbo.Employments e ON e.LegacyUserId = p.UserId;

    INSERT dbo.EmploymentRoleAssignments
    (
        EmploymentId, RoleId, EffectiveFrom, EffectiveTo, AssignedByUserId, CreatedAt
    )
    SELECT e.EmploymentId, a.RoleId, a.EffectiveFrom, a.EffectiveTo,
           a.AssignedByUserId, SYSUTCDATETIME()
    FROM dbo.UserRoleAssignments a
    JOIN dbo.Employments e ON e.LegacyUserId = a.UserId;

    INSERT dbo.TeamMemberships
    (
        EmploymentId, TeamId, IsPrimary, EffectiveFrom, EffectiveTo,
        AssignedByUserId, CreatedAt
    )
    SELECT e.EmploymentId, a.TeamId, a.IsPrimary, a.EffectiveFrom, a.EffectiveTo,
           a.AssignedByUserId, SYSUTCDATETIME()
    FROM dbo.UserTeamAssignments a
    JOIN dbo.Employments e ON e.LegacyUserId = a.UserId;

    INSERT dbo.TeamLeaderAssignments
    (
        TeamId, EmploymentId, EffectiveFrom, EffectiveTo, AssignedByUserId, CreatedAt
    )
    SELECT DISTINCT
        m.TeamId,
        m.EmploymentId,
        CASE WHEN m.EffectiveFrom > r.EffectiveFrom THEN m.EffectiveFrom ELSE r.EffectiveFrom END,
        CASE
            WHEN m.EffectiveTo IS NULL THEN r.EffectiveTo
            WHEN r.EffectiveTo IS NULL THEN m.EffectiveTo
            WHEN m.EffectiveTo < r.EffectiveTo THEN m.EffectiveTo ELSE r.EffectiveTo
        END,
        r.AssignedByUserId,
        SYSUTCDATETIME()
    FROM dbo.TeamMemberships m
    JOIN dbo.EmploymentRoleAssignments r ON r.EmploymentId = m.EmploymentId
    JOIN dbo.Roles role ON role.RoleId = r.RoleId
    WHERE LOWER(LTRIM(RTRIM(role.RoleCode))) = N'leader'
      AND m.EffectiveFrom <= ISNULL(r.EffectiveTo, CONVERT(date, N'99991231'))
      AND r.EffectiveFrom <= ISNULL(m.EffectiveTo, CONVERT(date, N'99991231'));

    ALTER TABLE dbo.UserIdentityProfiles ADD EmploymentId BIGINT NULL;
    ALTER TABLE dbo.UserIdentityProfiles WITH CHECK ADD
        CONSTRAINT FK_UserIdentityProfiles_Employments
        FOREIGN KEY(EmploymentId) REFERENCES dbo.Employments(EmploymentId);
    CREATE UNIQUE INDEX UX_UserIdentityProfiles_Employment
        ON dbo.UserIdentityProfiles(EmploymentId) WHERE EmploymentId IS NOT NULL;

    UPDATE p
       SET EmploymentId = e.EmploymentId,
           UpdatedAt = SYSUTCDATETIME()
    FROM dbo.UserIdentityProfiles p
    JOIN dbo.Employments e ON e.LegacyUserId = p.UserId;

    ALTER TABLE dbo.VisitTrips ADD EmploymentId BIGINT NULL;
    ALTER TABLE dbo.VisitTrips WITH CHECK ADD
        CONSTRAINT FK_VisitTrips_Employments
        FOREIGN KEY(EmploymentId) REFERENCES dbo.Employments(EmploymentId);
    CREATE INDEX IX_VisitTrips_Employment_VisitDate
        ON dbo.VisitTrips(EmploymentId, VisitDate, Status) WHERE EmploymentId IS NOT NULL;

    ALTER TABLE dbo.VisitTripSnapshots ADD
        PersonIdSnapshot BIGINT NULL,
        EmploymentIdSnapshot BIGINT NULL;

    INSERT dbo.SchemaVersions(VersionNumber, Description, AppliedAt, AppliedBy)
    VALUES
    (
        N'1.8.0-002',
        N'Person, Employment, status, role, membership, leader and delegation effective-dated model',
        SYSUTCDATETIME(),
        N'v1.8.0 Post-UAT'
    );

    COMMIT TRANSACTION;
    PRINT N'1.8.0-002 person / employment / role / membership completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
