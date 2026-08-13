SET NOCOUNT ON;

DECLARE @Errors TABLE(
    ErrorMessage NVARCHAR(1000) NOT NULL
);


IF EXISTS(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Users')
      AND name = N'EmployeeNo'
      AND is_nullable = 0
)
    INSERT @Errors VALUES(
        N'Users.EmployeeNo is still NOT NULL'
    );

IF NOT EXISTS(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Users')
      AND name = N'UX_Users_EmployeeNo_NotNull'
      AND is_unique = 1
      AND has_filter = 1
)
    INSERT @Errors VALUES(
        N'Missing filtered unique index: UX_Users_EmployeeNo_NotNull'
    );

IF OBJECT_ID(N'dbo.UserIdentityProfiles', N'U') IS NULL
    INSERT @Errors VALUES(N'Missing table: UserIdentityProfiles');

IF OBJECT_ID(N'dbo.UserEmploymentPeriods', N'U') IS NULL
    INSERT @Errors VALUES(N'Missing table: UserEmploymentPeriods');

IF OBJECT_ID(N'dbo.UserRoleAssignments', N'U') IS NULL
    INSERT @Errors VALUES(N'Missing table: UserRoleAssignments');

IF OBJECT_ID(N'dbo.UserTeamAssignments', N'U') IS NULL
    INSERT @Errors VALUES(N'Missing table: UserTeamAssignments');

IF OBJECT_ID(N'dbo.UserDataScopes', N'U') IS NULL
    INSERT @Errors VALUES(N'Missing table: UserDataScopes');

IF OBJECT_ID(N'dbo.UserCapabilities', N'U') IS NULL
    INSERT @Errors VALUES(N'Missing table: UserCapabilities');

IF OBJECT_ID(N'dbo.UserIdentityProfiles', N'U') IS NOT NULL
BEGIN
    IF EXISTS(
        SELECT UserCode
        FROM dbo.UserIdentityProfiles
        GROUP BY UserCode
        HAVING COUNT(*) > 1
    )
        INSERT @Errors VALUES(N'Duplicate UserCode in UserIdentityProfiles');

    IF EXISTS(
        SELECT u.UserId
        FROM dbo.Users u
        LEFT JOIN dbo.UserIdentityProfiles p
            ON p.UserId = u.UserId
        WHERE p.UserId IS NULL
    )
        INSERT @Errors VALUES(
            N'One or more legacy Users were not backfilled into UserIdentityProfiles'
        );
END;

IF OBJECT_ID(N'dbo.UserRoleAssignments', N'U') IS NOT NULL
BEGIN
    IF EXISTS(
        SELECT ur.UserRoleId
        FROM dbo.UserRoles ur
        WHERE NOT EXISTS(
            SELECT 1
            FROM dbo.UserRoleAssignments a
            WHERE a.UserId = ur.UserId
              AND a.RoleId = ur.RoleId
        )
    )
        INSERT @Errors VALUES(
            N'One or more legacy UserRoles were not backfilled'
        );
END;

IF OBJECT_ID(N'dbo.UserTeamAssignments', N'U') IS NOT NULL
BEGIN
    IF EXISTS(
        SELECT s.UserTeamScopeId
        FROM dbo.UserTeamScopes s
        WHERE NOT EXISTS(
            SELECT 1
            FROM dbo.UserTeamAssignments a
            WHERE a.UserId = s.UserId
              AND a.TeamId = s.TeamId
        )
    )
        INSERT @Errors VALUES(
            N'One or more legacy UserTeamScopes were not backfilled'
        );
END;

IF EXISTS(SELECT 1 FROM @Errors)
BEGIN
    SELECT ErrorMessage
    FROM @Errors;

    THROW 51790,
          N'v1.7.0-001 Verify FAILED.',
          1;
END;

SELECT
    N'PASS' AS VerifyStatus,
    (SELECT COUNT(*) FROM dbo.Users) AS LegacyUsers,
    (SELECT COUNT(*) FROM dbo.UserIdentityProfiles) AS IdentityProfiles,
    (SELECT COUNT(*) FROM dbo.UserRoleAssignments) AS RoleAssignments,
    (SELECT COUNT(*) FROM dbo.UserTeamAssignments) AS TeamAssignments,
    (SELECT COUNT(*) FROM dbo.UserDataScopes) AS DataScopes,
    (SELECT COUNT(*) FROM dbo.UserCapabilities) AS Capabilities;

PRINT N'v1.7.0-001 Verify PASS.';
