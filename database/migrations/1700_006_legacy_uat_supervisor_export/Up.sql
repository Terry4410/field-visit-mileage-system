SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    /* ================================================================
       v1.7.0-006 Legacy UAT Supervisor Export Capability Correction

       Purpose:
       Correct the exact legacy UAT gov01 supervisor normalized by
       migration 1.7.0-004. That migration deliberately initialized
       ExportExcel / ExportPdf to OFF, but UAT now requires this supervisor
       to be able to download both report formats.

       IMPORTANT:
       - Targets only the exact UAT demo identity:
           OrganizationCode = UAT
           UserCode         = gov01
           Email            = gov01@example.com
       - Does NOT grant export to every Supervisor.
       - Does NOT change data scope, query logic, report content,
         mileage/subsidy calculation, approvals, or snapshots.
       - Refuses to overwrite capability history that appears to have been
         administered after 1.7.0-004.
       - 1700_001 through 1700_005 remain immutable.
       ================================================================ */

    IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
        THROW 52100,
              N'找不到 dbo.SchemaVersions。',
              1;

    IF NOT EXISTS(
        SELECT 1
        FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-005'
    )
        THROW 52101,
              N'尚未套用 prerequisite Migration 1.7.0-005。',
              1;

    IF EXISTS(
        SELECT 1
        FROM dbo.SchemaVersions
        WHERE VersionNumber = N'1.7.0-006'
    )
        THROW 52102,
              N'Migration 1.7.0-006 已套用，不得重複執行。',
              1;

    IF OBJECT_ID(N'dbo.Organizations', N'U') IS NULL
       OR OBJECT_ID(N'dbo.Users', N'U') IS NULL
       OR OBJECT_ID(N'dbo.UserIdentityProfiles', N'U') IS NULL
       OR OBJECT_ID(N'dbo.UserRoleAssignments', N'U') IS NULL
       OR OBJECT_ID(N'dbo.Roles', N'U') IS NULL
       OR OBJECT_ID(N'dbo.UserCapabilities', N'U') IS NULL
        THROW 52103,
              N'缺少 v1.7 Identity / Access 必要資料表。',
              1;

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @Today DATE = CONVERT(date, @Now);
    DECLARE @OrgId INT = NULL;
    DECLARE @UserId INT = NULL;

    SELECT @OrgId = OrganizationId
    FROM dbo.Organizations
    WHERE OrganizationCode = N'UAT';

    /*
       Production / non-UAT:
       intentionally no business-data correction.
       SchemaVersion is still registered below.
    */
    IF @OrgId IS NOT NULL
    BEGIN
        DECLARE @TargetCount INT = 0;

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
            THROW 52104,
                  N'UAT gov01 必須且只能存在一筆，請由 IT Review。',
                  1;

        IF NOT EXISTS(
            SELECT 1
            FROM dbo.UserIdentityProfiles
            WHERE
                UserId = @UserId
                AND UserType = N'External'
                AND AuthorizationFrom <= @Today
                AND AuthorizationTo >= @Today
        )
            THROW 52105,
                  N'UAT gov01 不是有效期間內的 External Supervisor。',
                  1;

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
            THROW 52106,
                  N'gov01 必須且只能有一筆有效 Supervisor Role Assignment。',
                  1;

        /*
           1.7.0-004 created exactly one history row per export capability.
           If any later admin/bulk maintenance created additional history,
           stop instead of overwriting an intentional authorization change.
        */
        IF (
            SELECT COUNT(*)
            FROM dbo.UserCapabilities
            WHERE
                UserId = @UserId
                AND CapabilityCode
                    IN(N'ExportExcel', N'ExportPdf')
        ) <> 2
            THROW 52107,
                  N'gov01 Export Capability 歷史不是 1.7.0-004 預期狀態，請由 IT Review。',
                  1;

        IF (
            SELECT COUNT(*)
            FROM dbo.UserCapabilities
            WHERE
                UserId = @UserId
                AND CapabilityCode
                    IN(N'ExportExcel', N'ExportPdf')
                AND EffectiveFrom <= @Today
                AND (
                    EffectiveTo IS NULL
                    OR EffectiveTo >= @Today
                )
        ) <> 2
            THROW 52108,
                  N'gov01 必須有兩筆有效 Export Capability，請由 IT Review。',
                  1;

        IF (
            SELECT COUNT(*)
            FROM dbo.UserCapabilities
            WHERE
                UserId = @UserId
                AND CapabilityCode
                    IN(N'ExportExcel', N'ExportPdf')
                AND IsAllowed = 0
                AND GrantedByUserId IS NULL
                AND EffectiveFrom <= @Today
                AND (
                    EffectiveTo IS NULL
                    OR EffectiveTo >= @Today
                )
        ) <> 2
            THROW 52109,
                  N'gov01 Export Capability 已有人工維護或狀態不符，停止自動修正。',
                  1;

        UPDATE dbo.UserCapabilities
        SET IsAllowed = 1
        WHERE
            UserId = @UserId
            AND CapabilityCode
                IN(N'ExportExcel', N'ExportPdf')
            AND IsAllowed = 0
            AND GrantedByUserId IS NULL
            AND EffectiveFrom <= @Today
            AND (
                EffectiveTo IS NULL
                OR EffectiveTo >= @Today
            );

        IF @@ROWCOUNT <> 2
            THROW 52110,
                  N'gov01 Export Capability 修正筆數不是 2，已停止交易。',
                  1;
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
        N'1.7.0-006',
        N'Enable Excel and PDF export capabilities for the exact legacy UAT gov01 supervisor',
        @Now,
        N'v1.7.0 UAT supervisor export authorization defect correction'
    );

    COMMIT TRANSACTION;

    PRINT N'v1.7.0-006 Legacy UAT Supervisor Export Capability Correction completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
