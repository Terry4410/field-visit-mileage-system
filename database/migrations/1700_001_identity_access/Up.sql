SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    /* ================================================================
       v1.7.0-001 Identity & Access Foundation
       Additive only. Existing Users/UserRoles/UserTeamScopes remain
       untouched as the v1.6.1 compatibility layer.
       ================================================================ */

    IF OBJECT_ID(N'dbo.UserIdentityProfiles', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.UserIdentityProfiles(
            UserId INT NOT NULL
                CONSTRAINT PK_UserIdentityProfiles PRIMARY KEY,

            UserType NVARCHAR(20) NOT NULL,
            UserCode NVARCHAR(100) NOT NULL,
            IdentityProvider NVARCHAR(30) NOT NULL
                CONSTRAINT DF_UserIdentityProfiles_Provider DEFAULT(N'Demo'),

            ExternalOrganization NVARCHAR(200) NULL,
            ExternalTitle NVARCHAR(200) NULL,

            AuthorizationFrom DATE NULL,
            AuthorizationTo DATE NULL,

            CreatedAt DATETIME2(3) NOT NULL,
            UpdatedAt DATETIME2(3) NULL,

            CONSTRAINT FK_UserIdentityProfiles_Users
                FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId),

            CONSTRAINT CK_UserIdentityProfiles_UserType
                CHECK(UserType IN(N'Internal', N'External')),

            CONSTRAINT CK_UserIdentityProfiles_AuthorizationDates
                CHECK(
                    AuthorizationTo IS NULL
                    OR AuthorizationFrom IS NULL
                    OR AuthorizationTo >= AuthorizationFrom
                )
        );

        CREATE UNIQUE INDEX UX_UserIdentityProfiles_UserCode
            ON dbo.UserIdentityProfiles(UserCode);
    END;

    /* Existing users are internal by default.
       No HR hire date is invented here. */
    IF EXISTS(
        SELECT CandidateCode
        FROM (
            SELECT CASE
                     WHEN NULLIF(LTRIM(RTRIM(EmployeeNo)), N'') IS NOT NULL
                         THEN LTRIM(RTRIM(EmployeeNo))
                     ELSE CONCAT(N'USR-', UserId)
                   END AS CandidateCode
            FROM dbo.Users
        ) q
        GROUP BY CandidateCode
        HAVING COUNT(*) > 1
    )
        THROW 51701,
              N'Users 存在重複 EmployeeNo/UserCode 候選值，請先修正後再執行 v1.7 Migration。',
              1;

    INSERT dbo.UserIdentityProfiles(
        UserId,
        UserType,
        UserCode,
        IdentityProvider,
        CreatedAt
    )
    SELECT
        u.UserId,
        N'Internal',
        CASE
          WHEN NULLIF(LTRIM(RTRIM(u.EmployeeNo)), N'') IS NOT NULL
              THEN LTRIM(RTRIM(u.EmployeeNo))
          ELSE CONCAT(N'USR-', u.UserId)
        END,
        N'Demo',
        SYSUTCDATETIME()
    FROM dbo.Users u
    WHERE NOT EXISTS(
        SELECT 1
        FROM dbo.UserIdentityProfiles p
        WHERE p.UserId = u.UserId
    );

    IF OBJECT_ID(N'dbo.UserEmploymentPeriods', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.UserEmploymentPeriods(
            UserEmploymentPeriodId BIGINT IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_UserEmploymentPeriods PRIMARY KEY,

            UserId INT NOT NULL,
            EmploymentStatus NVARCHAR(30) NOT NULL,

            EffectiveFrom DATE NOT NULL,
            EffectiveTo DATE NULL,

            SourceType NVARCHAR(30) NOT NULL,
            SourceReference NVARCHAR(200) NULL,

            CreatedAt DATETIME2(3) NOT NULL,
            UpdatedAt DATETIME2(3) NULL,

            CONSTRAINT FK_UserEmploymentPeriods_Users
                FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId),

            CONSTRAINT CK_UserEmploymentPeriods_Status
                CHECK(EmploymentStatus IN(
                    N'Active',
                    N'Leave',
                    N'Terminated',
                    N'PreHire'
                )),

            CONSTRAINT CK_UserEmploymentPeriods_Dates
                CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)
        );

        CREATE INDEX IX_UserEmploymentPeriods_User_Effective
            ON dbo.UserEmploymentPeriods(
                UserId,
                EffectiveFrom,
                EffectiveTo
            );
    END;

    IF OBJECT_ID(N'dbo.UserRoleAssignments', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.UserRoleAssignments(
            UserRoleAssignmentId BIGINT IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_UserRoleAssignments PRIMARY KEY,

            UserId INT NOT NULL,
            RoleId INT NOT NULL,

            EffectiveFrom DATE NOT NULL,
            EffectiveTo DATE NULL,

            AssignedByUserId INT NULL,
            CreatedAt DATETIME2(3) NOT NULL,

            CONSTRAINT FK_UserRoleAssignments_Users
                FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId),

            CONSTRAINT FK_UserRoleAssignments_Roles
                FOREIGN KEY(RoleId) REFERENCES dbo.Roles(RoleId),

            CONSTRAINT FK_UserRoleAssignments_AssignedBy
                FOREIGN KEY(AssignedByUserId) REFERENCES dbo.Users(UserId),

            CONSTRAINT CK_UserRoleAssignments_Dates
                CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),

            CONSTRAINT UQ_UserRoleAssignments_User_Role_Start
                UNIQUE(UserId, RoleId, EffectiveFrom)
        );

        CREATE INDEX IX_UserRoleAssignments_User_Effective
            ON dbo.UserRoleAssignments(
                UserId,
                EffectiveFrom,
                EffectiveTo
            );
    END;

    /* Backfill existing current roles without inventing an end date. */
    INSERT dbo.UserRoleAssignments(
        UserId,
        RoleId,
        EffectiveFrom,
        EffectiveTo,
        CreatedAt
    )
    SELECT
        ur.UserId,
        ur.RoleId,
        CONVERT(date, ur.AssignedAt),
        NULL,
        SYSUTCDATETIME()
    FROM dbo.UserRoles ur
    WHERE NOT EXISTS(
        SELECT 1
        FROM dbo.UserRoleAssignments a
        WHERE a.UserId = ur.UserId
          AND a.RoleId = ur.RoleId
          AND a.EffectiveFrom = CONVERT(date, ur.AssignedAt)
    );

    IF OBJECT_ID(N'dbo.UserTeamAssignments', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.UserTeamAssignments(
            UserTeamAssignmentId BIGINT IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_UserTeamAssignments PRIMARY KEY,

            UserId INT NOT NULL,
            TeamId INT NOT NULL,
            IsPrimary BIT NOT NULL
                CONSTRAINT DF_UserTeamAssignments_Primary DEFAULT(0),

            EffectiveFrom DATE NOT NULL,
            EffectiveTo DATE NULL,

            AssignedByUserId INT NULL,
            CreatedAt DATETIME2(3) NOT NULL,

            CONSTRAINT FK_UserTeamAssignments_Users
                FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId),

            CONSTRAINT FK_UserTeamAssignments_Teams
                FOREIGN KEY(TeamId) REFERENCES dbo.Teams(TeamId),

            CONSTRAINT FK_UserTeamAssignments_AssignedBy
                FOREIGN KEY(AssignedByUserId) REFERENCES dbo.Users(UserId),

            CONSTRAINT CK_UserTeamAssignments_Dates
                CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),

            CONSTRAINT UQ_UserTeamAssignments_User_Team_Start
                UNIQUE(UserId, TeamId, EffectiveFrom)
        );

        CREATE INDEX IX_UserTeamAssignments_User_Effective
            ON dbo.UserTeamAssignments(
                UserId,
                EffectiveFrom,
                EffectiveTo
            );

        CREATE INDEX IX_UserTeamAssignments_Team_Effective
            ON dbo.UserTeamAssignments(
                TeamId,
                EffectiveFrom,
                EffectiveTo
            );
    END;

    /* Backfill current team membership. UserTeamScopes remains the
       compatibility layer used by v1.6.1 core flows. */
    INSERT dbo.UserTeamAssignments(
        UserId,
        TeamId,
        IsPrimary,
        EffectiveFrom,
        EffectiveTo,
        AssignedByUserId,
        CreatedAt
    )
    SELECT
        s.UserId,
        s.TeamId,
        s.IsPrimary,
        CONVERT(date, s.AssignedAt),
        CASE
          WHEN s.EndedAt IS NULL THEN NULL
          ELSE CONVERT(date, s.EndedAt)
        END,
        s.AssignedByUserId,
        SYSUTCDATETIME()
    FROM dbo.UserTeamScopes s
    WHERE NOT EXISTS(
        SELECT 1
        FROM dbo.UserTeamAssignments a
        WHERE a.UserId = s.UserId
          AND a.TeamId = s.TeamId
          AND a.EffectiveFrom = CONVERT(date, s.AssignedAt)
    );

    IF OBJECT_ID(N'dbo.UserDataScopes', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.UserDataScopes(
            UserDataScopeId BIGINT IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_UserDataScopes PRIMARY KEY,

            UserId INT NOT NULL,
            ScopeType NVARCHAR(30) NOT NULL,

            OrganizationId INT NULL,
            TeamId INT NULL,

            EffectiveFrom DATE NOT NULL,
            EffectiveTo DATE NULL,

            GrantedByUserId INT NULL,
            CreatedAt DATETIME2(3) NOT NULL,

            CONSTRAINT FK_UserDataScopes_Users
                FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId),

            CONSTRAINT FK_UserDataScopes_Organizations
                FOREIGN KEY(OrganizationId)
                REFERENCES dbo.Organizations(OrganizationId),

            CONSTRAINT FK_UserDataScopes_Teams
                FOREIGN KEY(TeamId) REFERENCES dbo.Teams(TeamId),

            CONSTRAINT FK_UserDataScopes_GrantedBy
                FOREIGN KEY(GrantedByUserId) REFERENCES dbo.Users(UserId),

            CONSTRAINT CK_UserDataScopes_Type
                CHECK(ScopeType IN(N'Organization', N'Team')),

            CONSTRAINT CK_UserDataScopes_Dates
                CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),

            CONSTRAINT CK_UserDataScopes_Target
                CHECK(
                    (ScopeType = N'Organization'
                        AND OrganizationId IS NOT NULL
                        AND TeamId IS NULL)
                    OR
                    (ScopeType = N'Team'
                        AND TeamId IS NOT NULL)
                )
        );

        CREATE INDEX IX_UserDataScopes_User_Effective
            ON dbo.UserDataScopes(
                UserId,
                EffectiveFrom,
                EffectiveTo
            );
    END;

    IF OBJECT_ID(N'dbo.UserCapabilities', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.UserCapabilities(
            UserCapabilityId BIGINT IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_UserCapabilities PRIMARY KEY,

            UserId INT NOT NULL,
            CapabilityCode NVARCHAR(100) NOT NULL,
            IsAllowed BIT NOT NULL,

            EffectiveFrom DATE NOT NULL,
            EffectiveTo DATE NULL,

            GrantedByUserId INT NULL,
            CreatedAt DATETIME2(3) NOT NULL,

            CONSTRAINT FK_UserCapabilities_Users
                FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId),

            CONSTRAINT FK_UserCapabilities_GrantedBy
                FOREIGN KEY(GrantedByUserId) REFERENCES dbo.Users(UserId),

            CONSTRAINT CK_UserCapabilities_Dates
                CHECK(EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom),

            CONSTRAINT UQ_UserCapabilities_User_Code_Start
                UNIQUE(UserId, CapabilityCode, EffectiveFrom)
        );

        CREATE INDEX IX_UserCapabilities_User_Effective
            ON dbo.UserCapabilities(
                UserId,
                EffectiveFrom,
                EffectiveTo
            );
    END;

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NOT NULL
       AND NOT EXISTS(
           SELECT 1
           FROM dbo.SchemaVersions
           WHERE VersionNumber = N'1.7.0-001'
       )
    BEGIN
        INSERT dbo.SchemaVersions(
            VersionNumber,
            Description,
            AppliedAt,
            AppliedBy
        )
        VALUES(
            N'1.7.0-001',
            N'Identity/access foundation: internal/external identity, employment, effective roles/teams, supervisor data scope and capabilities',
            SYSUTCDATETIME(),
            N'v1.7.0 Phase 1A'
        );
    END;

    COMMIT TRANSACTION;

    PRINT N'v1.7.0-001 Identity & Access Migration completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
