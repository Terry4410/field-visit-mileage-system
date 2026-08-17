SET NOCOUNT ON;

DECLARE @Errors TABLE
(
    ErrorMessage NVARCHAR(1000) NOT NULL
);

IF NOT EXISTS(
    SELECT 1
    FROM dbo.SchemaVersions
    WHERE VersionNumber = N'1.7.0-004'
)
    INSERT @Errors
    VALUES(N'SchemaVersions does not contain 1.7.0-004');

DECLARE @OrgId INT = NULL;
DECLARE @UserId INT = NULL;
DECLARE @Today DATE = CONVERT(date, SYSUTCDATETIME());

SELECT @OrgId = OrganizationId
FROM dbo.Organizations
WHERE OrganizationCode = N'UAT';

IF @OrgId IS NOT NULL
BEGIN
    SELECT @UserId = u.UserId
    FROM dbo.Users u
    JOIN dbo.UserIdentityProfiles p
      ON p.UserId = u.UserId
    WHERE
        u.OrganizationId = @OrgId
        AND p.UserCode = N'gov01'
        AND u.Email = N'gov01@example.com';

    IF @UserId IS NULL
        INSERT @Errors
        VALUES(N'UAT gov01 was not found');

    IF @UserId IS NOT NULL
    BEGIN
        IF EXISTS(
            SELECT 1
            FROM dbo.Users
            WHERE UserId = @UserId
              AND EmployeeNo IS NOT NULL
        )
            INSERT @Errors
            VALUES(N'gov01 EmployeeNo must be NULL');

        IF EXISTS(
            SELECT 1
            FROM dbo.Users
            WHERE UserId = @UserId
              AND TeamId IS NOT NULL
        )
            INSERT @Errors
            VALUES(N'gov01 Users.TeamId must be NULL');

        IF NOT EXISTS(
            SELECT 1
            FROM dbo.UserIdentityProfiles
            WHERE
                UserId = @UserId
                AND UserType = N'External'
                AND UserCode = N'gov01'
                AND NULLIF(
                    LTRIM(RTRIM(ExternalOrganization)),
                    N''
                ) IS NOT NULL
                AND AuthorizationFrom <= @Today
                AND AuthorizationTo >= @Today
        )
            INSERT @Errors
            VALUES(N'gov01 External identity profile is invalid');

        IF EXISTS(
            SELECT 1
            FROM dbo.UserTeamAssignments
            WHERE
                UserId = @UserId
                AND EffectiveFrom <= @Today
                AND (
                    EffectiveTo IS NULL
                    OR EffectiveTo >= @Today
                )
        )
            INSERT @Errors
            VALUES(N'gov01 still has active Team Membership');

        IF EXISTS(
            SELECT 1
            FROM dbo.UserTeamScopes
            WHERE
                UserId = @UserId
                AND IsActive = 1
        )
            INSERT @Errors
            VALUES(N'gov01 still has active legacy UserTeamScope');

        IF (
            SELECT COUNT(*)
            FROM dbo.UserDataScopes
            WHERE
                UserId = @UserId
                AND ScopeType = N'Organization'
                AND OrganizationId = @OrgId
                AND EffectiveFrom <= @Today
                AND (
                    EffectiveTo IS NULL
                    OR EffectiveTo >= @Today
                )
        ) <> 1
            INSERT @Errors
            VALUES(N'gov01 must have exactly one active Organization scope');

        IF EXISTS(
            SELECT 1
            FROM dbo.UserDataScopes
            WHERE
                UserId = @UserId
                AND ScopeType = N'Team'
                AND EffectiveFrom <= @Today
                AND (
                    EffectiveTo IS NULL
                    OR EffectiveTo >= @Today
                )
        )
            INSERT @Errors
            VALUES(N'gov01 still has an active Team data scope');

        IF (
            SELECT COUNT(*)
            FROM dbo.UserCapabilities
            WHERE
                UserId = @UserId
                AND CapabilityCode
                    IN(N'ExportExcel', N'ExportPdf')
                AND IsAllowed = 0
                AND EffectiveFrom <= @Today
                AND (
                    EffectiveTo IS NULL
                    OR EffectiveTo >= @Today
                )
        ) <> 2
            INSERT @Errors
            VALUES(N'gov01 export capabilities must both be explicitly OFF');

        IF (
            SELECT COUNT(*)
            FROM dbo.UserRoleAssignments a
            JOIN dbo.Roles r
              ON r.RoleId = a.RoleId
            WHERE
                a.UserId = @UserId
                AND LOWER(LTRIM(RTRIM(r.RoleCode)))
                    IN(N'supervisor', N'government')
                AND a.EffectiveFrom <= @Today
                AND (
                    a.EffectiveTo IS NULL
                    OR a.EffectiveTo >= @Today
                )
        ) <> 1
            INSERT @Errors
            VALUES(
                N'gov01 must have exactly one active Supervisor role'
            );
    END;
END;

IF EXISTS(SELECT 1 FROM @Errors)
BEGIN
    SELECT ErrorMessage
    FROM @Errors
    ORDER BY ErrorMessage;

    THROW 51990,
          N'v1.7.0-004 Verify FAILED.',
          1;
END;

SELECT
    N'PASS' AS VerifyStatus,
    N'1.7.0-004' AS SchemaVersion,
    @OrgId AS UatOrganizationId,
    @UserId AS Gov01UserId;

PRINT N'v1.7.0-004 Verify PASS.';
