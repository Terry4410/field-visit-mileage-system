SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    /* ================================================================
       v1.7.0-004 Legacy Supervisor Normalization

       Purpose:
       Normalize the legacy UAT gov01 account created before the
       v1.7 External Supervisor model.

       IMPORTANT:
       - This migration targets only the exact UAT demo identity:
           OrganizationCode = UAT
           UserCode         = gov01
           Email            = gov01@example.com
       - It does NOT convert all Supervisor-role users to External.
       - Production environments without this exact UAT record are no-op.
       - 1700_001 / 002 / 003 remain immutable.
       ================================================================ */

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
        THROW 51900,
              N'找不到 dbo.SchemaVersions。',
              1;

    IF NOT EXISTS(
        SELECT 1
        FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-003'
    )
        THROW 51901,
              N'尚未套用 prerequisite Migration 1.7.0-003。',
              1;

    IF EXISTS(
        SELECT 1
        FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-004'
    )
        THROW 51902,
              N'Migration 1.7.0-004 已套用，不得重複執行。',
              1;

    IF OBJECT_ID(N'dbo.UserIdentityProfiles', N'U') IS NULL
       OR OBJECT_ID(N'dbo.UserRoleAssignments', N'U') IS NULL
       OR OBJECT_ID(N'dbo.UserTeamAssignments', N'U') IS NULL
       OR OBJECT_ID(N'dbo.UserDataScopes', N'U') IS NULL
       OR OBJECT_ID(N'dbo.UserCapabilities', N'U') IS NULL
       OR OBJECT_ID(N'dbo.UserTeamScopes', N'U') IS NULL
        THROW 51903,
              N'缺少 v1.7 Identity / Access 必要資料表。',
              1;

    DECLARE @Now DATETIME2(3) =
        SYSUTCDATETIME();

    DECLARE @Today DATE =
        CONVERT(date, @Now);

    DECLARE @AuthorizationTo DATE =
        CONVERT(date, N'2099-12-31');

    DECLARE @OrgId INT = NULL;
    DECLARE @UserId INT = NULL;
    DECLARE @SupervisorRoleAssignmentId BIGINT = NULL;
    DECLARE @AuthorizationFrom DATE = NULL;

    SELECT @OrgId = OrganizationId
    FROM dbo.Organizations
    WHERE OrganizationCode = N'UAT';

    /*
       Production / non-UAT:
       intentionally no business-data normalization.
       SchemaVersion is still registered below.
    */
    IF @OrgId IS NOT NULL
    BEGIN
        DECLARE @TargetCount INT;

        SELECT
            @TargetCount = COUNT(*),
            @UserId = MAX(u.UserId)
        FROM dbo.Users u
        JOIN dbo.UserIdentityProfiles p
          ON p.UserId = u.UserId
        WHERE
            u.OrganizationId = @OrgId
            AND p.UserCode = N'gov01'
            AND u.Email = N'gov01@example.com';

        IF @TargetCount <> 1
            THROW 51904,
                  N'UAT gov01 必須且只能存在一筆，請由 IT Review。',
                  1;

        IF NOT EXISTS(
            SELECT 1
            FROM dbo.UserIdentityProfiles
            WHERE UserId = @UserId
              AND UserType = N'Internal'
        )
            THROW 51905,
                  N'UAT gov01 不是預期的 legacy Internal 狀態。',
                  1;

        /*
           Resolve the Supervisor Role from gov01's CURRENT effective
           assignment instead of selecting a global Role definition.

           This avoids ambiguity when both Supervisor and Government
           role codes exist in the database.
        */
        DECLARE @SupervisorAssignmentCount INT = 0;

        SELECT
            @SupervisorAssignmentCount =
                COUNT(*),

            @SupervisorRoleAssignmentId =
                MAX(a.UserRoleAssignmentId),

            @AuthorizationFrom =
                MIN(a.EffectiveFrom)
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
            );

        IF @SupervisorAssignmentCount <> 1
            THROW 51906,
                  N'gov01 必須且只能有一筆有效 Supervisor Role Assignment。',
                  1;

        IF EXISTS(
            SELECT 1
            FROM dbo.UserRoleAssignments a
            JOIN dbo.Roles r
              ON r.RoleId = a.RoleId
            WHERE
                a.UserId = @UserId
                AND a.EffectiveFrom <= @Today
                AND (
                    a.EffectiveTo IS NULL
                    OR a.EffectiveTo >= @Today
                )
                AND LOWER(LTRIM(RTRIM(r.RoleCode)))
                    NOT IN(N'supervisor', N'government')
        )
            THROW 51908,
                  N'gov01 存在非 Supervisor 的有效 Role，請由 IT Review。',
                  1;

        IF @AuthorizationFrom IS NULL
            SET @AuthorizationFrom = @Today;

        IF @AuthorizationFrom > @Today
            SET @AuthorizationFrom = @Today;
        /*
           Legacy migration should not silently overwrite previously
           administered export capability history.
        */
        IF EXISTS(
            SELECT 1
            FROM dbo.UserCapabilities
            WHERE UserId = @UserId
              AND CapabilityCode
                  IN(N'ExportExcel', N'ExportPdf')
        )
            THROW 51909,
                  N'gov01 已有 Export Capability 歷史，請由 IT Review。',
                  1;

        /*
           More than one current Organization scope indicates manual /
           unexpected authorization history.
        */
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
        ) > 1
            THROW 51910,
                  N'gov01 存在多筆有效 Organization Scope，請由 IT Review。',
                  1;

        /* ------------------------------------------------------------
           1. Identity: Internal legacy -> External Supervisor
           ------------------------------------------------------------ */

        UPDATE dbo.Users
        SET
            EmployeeNo = NULL,
            TeamId = NULL,
            UpdatedAt = @Now
        WHERE UserId = @UserId;

        UPDATE dbo.UserIdentityProfiles
        SET
            UserType = N'External',
            ExternalOrganization = N'UAT 政府督導單位',
            ExternalTitle = N'督導',
            AuthorizationFrom = @AuthorizationFrom,
            AuthorizationTo = @AuthorizationTo,
            UpdatedAt = @Now
        WHERE UserId = @UserId;

        /* ------------------------------------------------------------
           2. Supervisor Role remains.
              Align its authorization end date.
           ------------------------------------------------------------ */

        UPDATE dbo.UserRoleAssignments
        SET EffectiveTo = @AuthorizationTo
        WHERE UserRoleAssignmentId =
              @SupervisorRoleAssignmentId;

        /* ------------------------------------------------------------
           3. External Supervisor must NOT be a Team Member.

              Historical assignments before today are retained.
              Assignments that have not started yet are removed because
              they must never become effective for an External user.
           ------------------------------------------------------------ */

        DELETE dbo.UserTeamAssignments
        WHERE UserId = @UserId
          AND EffectiveFrom >= @Today;

        UPDATE dbo.UserTeamAssignments
        SET EffectiveTo =
            DATEADD(day, -1, @Today)
        WHERE
            UserId = @UserId
            AND EffectiveFrom < @Today
            AND (
                EffectiveTo IS NULL
                OR EffectiveTo >= @Today
            );

        UPDATE dbo.UserTeamScopes
        SET
            IsActive = 0,
            EndedAt = COALESCE(EndedAt, @Now)
        WHERE UserId = @UserId
          AND IsActive = 1;

        /* ------------------------------------------------------------
           4. Read scope = Organization.

              Remove current/future Team data scopes, retain historical.
           ------------------------------------------------------------ */

        DELETE dbo.UserDataScopes
        WHERE
            UserId = @UserId
            AND ScopeType = N'Team'
            AND EffectiveFrom >= @Today;

        UPDATE dbo.UserDataScopes
        SET EffectiveTo =
            DATEADD(day, -1, @Today)
        WHERE
            UserId = @UserId
            AND ScopeType = N'Team'
            AND EffectiveFrom < @Today
            AND (
                EffectiveTo IS NULL
                OR EffectiveTo >= @Today
            );

        IF EXISTS(
            SELECT 1
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
        )
        BEGIN
            UPDATE dbo.UserDataScopes
            SET EffectiveTo = @AuthorizationTo
            WHERE
                UserId = @UserId
                AND ScopeType = N'Organization'
                AND OrganizationId = @OrgId
                AND EffectiveFrom <= @Today
                AND (
                    EffectiveTo IS NULL
                    OR EffectiveTo >= @Today
                );
        END
        ELSE
        BEGIN
            INSERT dbo.UserDataScopes
            (
                UserId,
                ScopeType,
                OrganizationId,
                TeamId,
                EffectiveFrom,
                EffectiveTo,
                GrantedByUserId,
                CreatedAt
            )
            VALUES
            (
                @UserId,
                N'Organization',
                @OrgId,
                NULL,
                @AuthorizationFrom,
                @AuthorizationTo,
                NULL,
                @Now
            );
        END;

        /* ------------------------------------------------------------
           5. Export capability defaults = OFF.
           ------------------------------------------------------------ */

        INSERT dbo.UserCapabilities
        (
            UserId,
            CapabilityCode,
            IsAllowed,
            EffectiveFrom,
            EffectiveTo,
            GrantedByUserId,
            CreatedAt
        )
        VALUES
        (
            @UserId,
            N'ExportExcel',
            0,
            @AuthorizationFrom,
            @AuthorizationTo,
            NULL,
            @Now
        ),
        (
            @UserId,
            N'ExportPdf',
            0,
            @AuthorizationFrom,
            @AuthorizationTo,
            NULL,
            @Now
        );
    END;

    INSERT dbo.SchemaVersions
    (
        VersionNumber,
        Description,
        AppliedAt,
        AppliedBy
    )
    VALUES
    (
        N'1.7.0-004',
        N'Normalize legacy UAT gov01 to the v1.7 External Supervisor identity, scope and capability model',
        @Now,
        N'v1.7.0 People Bulk UAT defect correction'
    );

    COMMIT TRANSACTION;

    PRINT N'v1.7.0-004 Legacy Supervisor Normalization completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
