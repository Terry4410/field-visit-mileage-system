SET NOCOUNT ON;

DECLARE @Errors TABLE
(
    ErrorMessage NVARCHAR(1000) NOT NULL
);

IF NOT EXISTS(
    SELECT 1
    FROM dbo.SchemaVersions
    WHERE VersionNumber = N'1.7.0-006'
)
    INSERT @Errors
    VALUES(N'SchemaVersions does not contain 1.7.0-006');

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
        IF NOT EXISTS(
            SELECT 1
            FROM dbo.UserIdentityProfiles
            WHERE
                UserId = @UserId
                AND UserType = N'External'
                AND AuthorizationFrom <= @Today
                AND AuthorizationTo >= @Today
        )
            INSERT @Errors
            VALUES(N'gov01 External identity profile is not currently valid');

        IF (
            SELECT COUNT(*)
            FROM dbo.UserRoleAssignments a
            JOIN dbo.Roles r
              ON r.RoleId = a.RoleId
            WHERE
                a.UserId = @UserId
                AND r.IsActive = 1
                AND LOWER(LTRIM(RTRIM(r.RoleCode)))
                    IN(N'supervisor', N'government')
                AND a.EffectiveFrom <= @Today
                AND (
                    a.EffectiveTo IS NULL
                    OR a.EffectiveTo >= @Today
                )
        ) <> 1
            INSERT @Errors
            VALUES(N'gov01 must have exactly one active Supervisor role');

        IF (
            SELECT COUNT(*)
            FROM dbo.UserCapabilities
            WHERE
                UserId = @UserId
                AND CapabilityCode
                    IN(N'ExportExcel', N'ExportPdf')
                AND IsAllowed = 1
                AND EffectiveFrom <= @Today
                AND (
                    EffectiveTo IS NULL
                    OR EffectiveTo >= @Today
                )
        ) <> 2
            INSERT @Errors
            VALUES(N'gov01 Excel and PDF export capabilities must both be ON');

        IF EXISTS(
            SELECT 1
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
        )
            INSERT @Errors
            VALUES(N'gov01 still has an active disabled export capability');
    END;
END;

IF EXISTS(SELECT 1 FROM @Errors)
BEGIN
    SELECT ErrorMessage
    FROM @Errors
    ORDER BY ErrorMessage;

    THROW 52190,
          N'v1.7.0-006 Verify FAILED.',
          1;
END;

SELECT
    N'PASS' AS VerifyStatus,
    N'1.7.0-006' AS SchemaVersion,
    @OrgId AS UatOrganizationId,
    @UserId AS Gov01UserId;

PRINT N'v1.7.0-006 Verify PASS.';
