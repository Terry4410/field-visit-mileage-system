using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V170PeopleBulkWorkbookService(
    AppDbContext db)
    : IV170PeopleBulkWorkbookService
{
    private const string ImportType =
        "people-authorization";

    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly JsonSerializerOptions
        JsonOptions =
            new(JsonSerializerDefaults.Web);

    private static readonly string[]
        InternalHeaders =
        [
            "UserCode",
            "EmployeeNo",
            "DisplayName",
            "Email",
            "EmploymentStatus",
            "AdminEnabled",
            "Roles",
            "TeamCodes",
            "PrimaryTeamCode",
            "ChangeEffectiveFrom",
            "IdentityProvider",
            "EntraTenantId",
            "EntraObjectId"
        ];

    private static readonly string[]
        ExternalHeaders =
        [
            "UserCode",
            "DisplayName",
            "Email",
            "ExternalOrganization",
            "ExternalTitle",
            "AuthorizationFrom",
            "AuthorizationTo",
            "AdminEnabled",
            "ScopeType",
            "ScopeTeamCodes",
            "CanExportExcel",
            "CanExportPdf",
            "IdentityProvider",
            "EntraTenantId",
            "EntraObjectId",
            "ChangeEffectiveFrom"
        ];

    public async Task<ReportExportContext>
        ExportCurrentAsync(
            CurrentUserDto admin,
            CancellationToken ct)
    {
        var orgId =
            RequireAdminOrganization(admin);

        var today =
            BusinessTime.Today;

        var snapshot =
            await LoadSnapshotAsync(
                orgId,
                today,
                ct);

        var internalRows =
            new List<string[]>
            {
                InternalHeaders
            };

        var externalRows =
            new List<string[]>
            {
                ExternalHeaders
            };

        foreach (var user
                 in snapshot.Users.Values
                     .OrderBy(x => x.DisplayName)
                     .ThenBy(x => x.UserId))
        {
            if (!snapshot.Profiles.TryGetValue(
                    user.UserId,
                    out var profile))
            {
                continue;
            }

            if (profile.UserType.Equals(
                    UserTypes.Internal,
                    StringComparison.OrdinalIgnoreCase))
            {
                var roles =
                    snapshot.RolesByUser.TryGetValue(
                        user.UserId,
                        out var currentRoles)
                        ? currentRoles
                            .Select(NormalizeRole)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .OrderBy(x => x)
                            .ToList()
                        : [];

                var teams =
                    snapshot.TeamsByUser.TryGetValue(
                        user.UserId,
                        out var currentTeams)
                        ? currentTeams
                        : [];

                var primary =
                    teams.FirstOrDefault(
                        x => x.IsPrimary);

                snapshot.EmploymentByUser.TryGetValue(
                    user.UserId,
                    out var employmentStatus);

                internalRows.Add(
                [
                    profile.UserCode,
                    user.EmployeeNo ?? "",
                    user.DisplayName,
                    user.Email ?? "",
                    employmentStatus ?? "",
                    Yn(user.IsActive),
                    string.Join(";", roles),
                    string.Join(
                        ";",
                        teams
                            .Select(x => x.TeamCode)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .OrderBy(x => x)),
                    primary?.TeamCode ?? "",
                    DateText(today),
                    profile.IdentityProvider,
                    profile.EntraTenantId
                        ?.ToString("D") ?? "",
                    profile.EntraObjectId
                        ?.ToString("D") ?? ""
                ]);
            }
            else if (profile.UserType.Equals(
                         UserTypes.External,
                         StringComparison.OrdinalIgnoreCase))
            {
                var scopes =
                    snapshot.ScopesByUser.TryGetValue(
                        user.UserId,
                        out var currentScopes)
                        ? currentScopes
                        : [];

                var organizationScope =
                    scopes.Any(
                        x => x.ScopeType.Equals(
                            DataScopeTypes.Organization,
                            StringComparison.OrdinalIgnoreCase));

                var scopeType =
                    organizationScope
                        ? DataScopeTypes.Organization
                        : scopes.Count > 0
                            ? DataScopeTypes.Team
                            : "";

                var scopeTeamCodes =
                    organizationScope
                        ? ""
                        : string.Join(
                            ";",
                            scopes
                                .Where(
                                    x => x.TeamCode is not null)
                                .Select(
                                    x => x.TeamCode!)
                                .Distinct(
                                    StringComparer.OrdinalIgnoreCase)
                                .OrderBy(x => x));

                var excelAllowed =
                    GetCapability(
                        snapshot,
                        user.UserId,
                        CapabilityCodes.ExportExcel);

                var pdfAllowed =
                    GetCapability(
                        snapshot,
                        user.UserId,
                        CapabilityCodes.ExportPdf);

                var effectiveDate =
                    ClampEffectiveDate(
                        today,
                        profile.AuthorizationFrom,
                        profile.AuthorizationTo);

                externalRows.Add(
                [
                    profile.UserCode,
                    user.DisplayName,
                    user.Email ?? "",
                    profile.ExternalOrganization ?? "",
                    profile.ExternalTitle ?? "",
                    profile.AuthorizationFrom
                        .HasValue
                            ? DateText(
                                profile.AuthorizationFrom.Value)
                            : "",
                    profile.AuthorizationTo
                        .HasValue
                            ? DateText(
                                profile.AuthorizationTo.Value)
                            : "",
                    Yn(user.IsActive),
                    scopeType,
                    scopeTeamCodes,
                    Yn(excelAllowed),
                    Yn(pdfAllowed),
                    profile.IdentityProvider,
                    profile.EntraTenantId
                        ?.ToString("D") ?? "",
                    profile.EntraObjectId
                        ?.ToString("D") ?? "",
                    DateText(effectiveDate)
                ]);
            }
        }

        using var stream =
            new MemoryStream();

        using (var doc =
               SpreadsheetDocument.Create(
                   stream,
                   SpreadsheetDocumentType.Workbook,
                   true))
        {
            var wb =
                doc.AddWorkbookPart();

            wb.Workbook =
                new Workbook();

            var sheets =
                wb.Workbook.AppendChild(
                    new Sheets());

            AddSheet(
                wb,
                sheets,
                1,
                "InternalAuthorization",
                internalRows);

            AddSheet(
                wb,
                sheets,
                2,
                "ExternalSupervisors",
                externalRows);

            AddInstructionsSheet(
                wb,
                sheets,
                3);

            wb.Workbook.Save();
        }

        AddAudit(
            admin,
            "PeopleBulkExportCurrent",
            new
            {
                InternalCount =
                    internalRows.Count - 1,

                ExternalCount =
                    externalRows.Count - 1
            });

        await db.SaveChangesAsync(ct);

        return new ReportExportContext(
            $"人員與權限目前設定_{today:yyyyMMdd}.xlsx",
            stream.ToArray(),
            ExcelContentType);
    }

    public async Task<ReportExportContext>
        CreateTemplateAsync(
            CurrentUserDto admin,
            CancellationToken ct)
    {
        RequireAdminOrganization(admin);

        using var stream =
            new MemoryStream();

        using (var doc =
               SpreadsheetDocument.Create(
                   stream,
                   SpreadsheetDocumentType.Workbook,
                   true))
        {
            var wb =
                doc.AddWorkbookPart();

            wb.Workbook =
                new Workbook();

            var sheets =
                wb.Workbook.AppendChild(
                    new Sheets());

            /*
             * Active import sheets intentionally contain headers only.
             * This avoids somebody uploading the sample row and
             * accidentally creating/changing an account.
             */
            AddSheet(
                wb,
                sheets,
                1,
                "InternalAuthorization",
                [InternalHeaders]);

            AddSheet(
                wb,
                sheets,
                2,
                "ExternalSupervisors",
                [ExternalHeaders]);

            AddInstructionsSheet(
                wb,
                sheets,
                3);

            AddSheet(
                wb,
                sheets,
                4,
                "Examples",
                new List<string[]>
                {
                    new[]
                    {
                        "Sheet",
                        "用途",
                        "範例"
                    },
                    new[]
                    {
                        "InternalAuthorization",
                        "內部人員授權",
                        "UserCode=visitor01；Roles=visitor；TeamCodes=TEAM-N01；PrimaryTeamCode=TEAM-N01；AdminEnabled=Y"
                    },
                    new[]
                    {
                        "ExternalSupervisors",
                        "新增外部督導",
                        "UserCode 留白；ScopeType=Organization 或 Team；正式環境 IdentityProvider 可使用 EntraId"
                    }
                });

            wb.Workbook.Save();
        }

        AddAudit(
            admin,
            "PeopleBulkTemplateDownload",
            new
            {
                Version = "1.7.0"
            });

        await db.SaveChangesAsync(ct);

        return new ReportExportContext(
            "人員與權限批次匯入範例.xlsx",
            stream.ToArray(),
            ExcelContentType);
    }

    public async Task<V170PeopleBulkPreviewDto>
        PreviewAsync(
            CurrentUserDto admin,
            byte[] content,
            CancellationToken ct)
    {
        var orgId =
            RequireAdminOrganization(admin);

        if (content.Length == 0)
        {
            throw new InvalidOperationException(
                "上傳檔案為空。");
        }

        var today =
            BusinessTime.Today;

        var snapshot =
            await LoadSnapshotAsync(
                orgId,
                today,
                ct);

        var batch =
            new ImportBatch
            {
                ImportBatchId =
                    Guid.NewGuid(),

                ImportType =
                    ImportType,

                OrganizationId =
                    orgId,

                RequestedByUserId =
                    admin.UserId,

                Status =
                    "Previewed",

                CreatedAt =
                    DateTime.UtcNow,

                ExpiresAt =
                    DateTime.UtcNow.AddHours(4)
            };

        await db.ImportBatches.AddAsync(
            batch,
            ct);

        var result =
            new List<V170PeopleBulkPreviewItemDto>();

        using var stream =
            new MemoryStream(content);

        using var doc =
            SpreadsheetDocument.Open(
                stream,
                false);

        var internalSheet =
            ReadSheet(
                doc,
                "InternalAuthorization");

        var externalSheet =
            ReadSheet(
                doc,
                "ExternalSupervisors");

        EnsureHeaders(
            internalSheet,
            InternalHeaders,
            "InternalAuthorization");

        EnsureHeaders(
            externalSheet,
            ExternalHeaders,
            "ExternalSupervisors");

        var seenInternalCodes =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var seenExternalCodes =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var seenExternalCreateEmails =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        PreviewInternalRows(
            batch,
            result,
            snapshot,
            internalSheet,
            seenInternalCodes,
            today);

        PreviewExternalRows(
            batch,
            result,
            snapshot,
            externalSheet,
            seenExternalCodes,
            seenExternalCreateEmails,
            today);

        if (result.Count == 0)
        {
            throw new InvalidOperationException(
                "Excel 沒有可預覽的資料列。");
        }

        batch.TotalCount =
            result.Count;

        batch.ValidCount =
            result.Count(
                x => x.Status == "Valid");

        batch.ErrorCount =
            result.Count(
                x => x.Status == "Error");

        var requiresRetroactive =
            result.Any(
                x =>
                    x.Status == "Valid"
                    && x.Action != "NoChange"
                    && x.IsRetroactive);

        AddAudit(
            admin,
            "PeopleBulkPreview",
            new
            {
                batch.ImportBatchId,
                batch.TotalCount,
                batch.ValidCount,
                batch.ErrorCount,
                RequiresRetroactiveConfirmation =
                    requiresRetroactive
            });

        await db.SaveChangesAsync(ct);

        return new V170PeopleBulkPreviewDto(
            batch.ImportBatchId,
            batch.TotalCount,
            batch.ValidCount,
            batch.ErrorCount,
            requiresRetroactive,
            result);
    }

    public async Task<ReportExportContext>
        CreateErrorReportAsync(
            CurrentUserDto admin,
            Guid importBatchId,
            CancellationToken ct)
    {
        var orgId =
            RequireAdminOrganization(admin);

        var batch =
            await db.ImportBatches
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.ImportBatchId
                            == importBatchId,
                    ct)
            ?? throw new KeyNotFoundException(
                "找不到匯入批次。");

        if (batch.ImportType != ImportType)
        {
            throw new InvalidOperationException(
                "此批次不是人員與權限匯入。");
        }

        if (batch.OrganizationId != orgId
            || batch.RequestedByUserId
               != admin.UserId)
        {
            throw new UnauthorizedAccessException(
                "只能下載自己建立的人員權限匯入錯誤報告。");
        }

        var errors =
            await db.ImportBatchItems
                .AsNoTracking()
                .Where(
                    x =>
                        x.ImportBatchId
                            == importBatchId
                        && x.Status == "Error")
                .OrderBy(x => x.RowNumber)
                .ThenBy(x => x.EntityType)
                .ToListAsync(ct);

        if (errors.Count == 0)
        {
            throw new InvalidOperationException(
                "此匯入批次沒有錯誤資料。");
        }

        var rows =
            new List<string[]>
            {
                new[]
                {
                    "RowNumber",
                    "Sheet",
                    "EntityType",
                    "Action",
                    "DisplayKey",
                    "ErrorMessage"
                }
            };

        foreach (var item in errors)
        {
            rows.Add(
            [
                item.RowNumber.ToString(
                    CultureInfo.InvariantCulture),

                SheetName(
                    item.EntityType),

                item.EntityType,
                item.Action,
                item.DisplayKey,
                item.ErrorMessage ?? ""
            ]);
        }

        using var stream =
            new MemoryStream();

        using (var doc =
               SpreadsheetDocument.Create(
                   stream,
                   SpreadsheetDocumentType.Workbook,
                   true))
        {
            var wb =
                doc.AddWorkbookPart();

            wb.Workbook =
                new Workbook();

            var sheets =
                wb.Workbook.AppendChild(
                    new Sheets());

            AddSheet(
                wb,
                sheets,
                1,
                "Errors",
                rows);

            wb.Workbook.Save();
        }

        AddAudit(
            admin,
            "PeopleBulkErrorReport",
            new
            {
                importBatchId,
                Count = errors.Count
            });

        await db.SaveChangesAsync(ct);

        return new ReportExportContext(
            $"人員與權限匯入錯誤_{importBatchId:N}.xlsx",
            stream.ToArray(),
            ExcelContentType);
    }

    /*
     * C2A intentionally does not enable database mutation.
     * C2B will route Confirm through the existing v1.7
     * People/Access writer so single-record and bulk rules
     * stay aligned.
     */
    public Task<V170PeopleBulkConfirmResultDto>
        ConfirmAsync(
            CurrentUserDto admin,
            Guid importBatchId,
            V170PeopleBulkConfirmRequest request,
            CancellationToken ct)
        => throw new InvalidOperationException(
            "人員與權限批次確認寫入尚未啟用；請完成 Checkpoint C2B。");

    private void PreviewInternalRows(
        ImportBatch batch,
        List<V170PeopleBulkPreviewItemDto> result,
        PeopleSnapshot snapshot,
        IReadOnlyList<string[]> sheet,
        HashSet<string> seenCodes,
        DateOnly today)
    {
        var headers =
            sheet[0];

        for (var i = 1;
             i < sheet.Count;
             i++)
        {
            var cells =
                sheet[i];

            if (cells.All(
                    string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var rowNo =
                i + 1;

            var raw =
                new V170InternalAuthorizationRawRow(
                    rowNo,
                    Get(cells, headers, "UserCode"),
                    Get(cells, headers, "EmployeeNo"),
                    Get(cells, headers, "DisplayName"),
                    Get(cells, headers, "Email"),
                    Get(cells, headers, "EmploymentStatus"),
                    Get(cells, headers, "AdminEnabled"),
                    Get(cells, headers, "Roles"),
                    Get(cells, headers, "TeamCodes"),
                    Get(cells, headers, "PrimaryTeamCode"),
                    Get(cells, headers, "ChangeEffectiveFrom"),
                    Get(cells, headers, "IdentityProvider"),
                    Get(cells, headers, "EntraTenantId"),
                    Get(cells, headers, "EntraObjectId"));

            V170InternalAuthorizationRow?
                normalized = null;

            string action =
                "Update";

            string? error =
                null;

            try
            {
                normalized =
                    V170PeopleBulkRules
                        .NormalizeInternal(raw);

                if (!seenCodes.Add(
                        normalized.UserCode))
                {
                    throw new InvalidOperationException(
                        "同一份 Excel 內 UserCode 不可重複。");
                }

                if (!snapshot.UserIdByCode
                    .TryGetValue(
                        normalized.UserCode,
                        out var userId))
                {
                    throw new InvalidOperationException(
                        "找不到 Internal User。內部人員必須先由 HR/人員主檔建立，權限 Excel 不可新增內部人員。");
                }

                var profile =
                    snapshot.Profiles[userId];

                var user =
                    snapshot.Users[userId];

                if (!profile.UserType.Equals(
                        UserTypes.Internal,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "此 UserCode 不是 Internal User。");
                }

                ValidateInternalReferenceFields(
                    normalized,
                    user,
                    snapshot.EmploymentByUser
                        .GetValueOrDefault(userId));

                ValidateTeamCodes(
                    normalized.TeamCodes,
                    snapshot);

                ValidateEntraBinding(
                    normalized.IdentityProvider,
                    normalized.EntraTenantId,
                    normalized.EntraObjectId,
                    userId,
                    snapshot);

                action =
                    SameInternal(
                        normalized,
                        user,
                        profile,
                        snapshot)
                        ? "NoChange"
                        : "Update";
            }
            catch (Exception ex)
            {
                error =
                    ex.Message;
            }

            var retroactive =
                normalized is not null
                && action != "NoChange"
                && V170PeopleBulkRules
                    .IsRetroactive(
                        normalized.ChangeEffectiveFrom,
                        today);

            Stage(
                batch,
                result,
                rowNo,
                "InternalAuthorization",
                action,
                normalized?.UserCode
                    ?? raw.UserCode
                    ?? $"Row-{rowNo}",
                (object?)normalized ?? raw,
                error,
                retroactive);
        }
    }

    private void PreviewExternalRows(
        ImportBatch batch,
        List<V170PeopleBulkPreviewItemDto> result,
        PeopleSnapshot snapshot,
        IReadOnlyList<string[]> sheet,
        HashSet<string> seenCodes,
        HashSet<string> seenCreateEmails,
        DateOnly today)
    {
        var headers =
            sheet[0];

        for (var i = 1;
             i < sheet.Count;
             i++)
        {
            var cells =
                sheet[i];

            if (cells.All(
                    string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var rowNo =
                i + 1;

            var raw =
                new V170ExternalSupervisorRawRow(
                    rowNo,
                    Get(cells, headers, "UserCode"),
                    Get(cells, headers, "DisplayName"),
                    Get(cells, headers, "Email"),
                    Get(cells, headers, "ExternalOrganization"),
                    Get(cells, headers, "ExternalTitle"),
                    Get(cells, headers, "AuthorizationFrom"),
                    Get(cells, headers, "AuthorizationTo"),
                    Get(cells, headers, "AdminEnabled"),
                    Get(cells, headers, "ScopeType"),
                    Get(cells, headers, "ScopeTeamCodes"),
                    Get(cells, headers, "CanExportExcel"),
                    Get(cells, headers, "CanExportPdf"),
                    Get(cells, headers, "IdentityProvider"),
                    Get(cells, headers, "EntraTenantId"),
                    Get(cells, headers, "EntraObjectId"),
                    Get(cells, headers, "ChangeEffectiveFrom"));

            V170ExternalSupervisorRow?
                normalized = null;

            var action =
                "Create";

            string? error =
                null;

            try
            {
                normalized =
                    V170PeopleBulkRules
                        .NormalizeExternal(raw);

                ValidateTeamCodes(
                    normalized.ScopeTeamCodes,
                    snapshot);

                int? targetUserId =
                    null;

                if (string.IsNullOrWhiteSpace(
                        normalized.UserCode))
                {
                    action =
                        "Create";

                    if (!seenCreateEmails.Add(
                            normalized.Email))
                    {
                        throw new InvalidOperationException(
                            "同一份 Excel 內新增的 External Supervisor Email 不可重複。");
                    }

                    if (snapshot.Users.Values.Any(
                            x =>
                                EmailEquals(
                                    x.Email,
                                    normalized.Email)))
                    {
                        throw new InvalidOperationException(
                            "此 Email 已存在於系統中。");
                    }
                }
                else
                {
                    action =
                        "Update";

                    if (!seenCodes.Add(
                            normalized.UserCode))
                    {
                        throw new InvalidOperationException(
                            "同一份 Excel 內 UserCode 不可重複。");
                    }

                    if (!snapshot.UserIdByCode
                        .TryGetValue(
                            normalized.UserCode,
                            out var userId))
                    {
                        throw new InvalidOperationException(
                            "找不到 External Supervisor UserCode。若要新增外部督導，請將 UserCode 留白。");
                    }

                    targetUserId =
                        userId;

                    var profile =
                        snapshot.Profiles[userId];

                    var user =
                        snapshot.Users[userId];

                    if (!profile.UserType.Equals(
                            UserTypes.External,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "此 UserCode 不是 External User。");
                    }

                    if (snapshot.Users.Values.Any(
                            x =>
                                x.UserId != userId
                                && EmailEquals(
                                    x.Email,
                                    normalized.Email)))
                    {
                        throw new InvalidOperationException(
                            "此 Email 已被其他帳號使用。");
                    }

                    action =
                        SameExternal(
                            normalized,
                            user,
                            profile,
                            snapshot)
                            ? "NoChange"
                            : "Update";
                }

                ValidateEntraBinding(
                    normalized.IdentityProvider,
                    normalized.EntraTenantId,
                    normalized.EntraObjectId,
                    targetUserId,
                    snapshot);
            }
            catch (Exception ex)
            {
                error =
                    ex.Message;
            }

            var retroactive =
                normalized is not null
                && action != "NoChange"
                && V170PeopleBulkRules
                    .IsRetroactive(
                        normalized.ChangeEffectiveFrom,
                        today);

            Stage(
                batch,
                result,
                rowNo,
                "ExternalSupervisor",
                action,
                normalized?.UserCode
                    ?? normalized?.Email
                    ?? raw.UserCode
                    ?? raw.Email
                    ?? $"Row-{rowNo}",
                (object?)normalized ?? raw,
                error,
                retroactive);
        }
    }

    private static void
        ValidateInternalReferenceFields(
            V170InternalAuthorizationRow row,
            User user,
            string? employmentStatus)
    {
        if (!string.IsNullOrWhiteSpace(
                row.EmployeeNo)
            && !TextEquals(
                row.EmployeeNo,
                user.EmployeeNo))
        {
            throw new InvalidOperationException(
                "EmployeeNo 與目前 HR/人員主檔不一致；請重新下載最新設定。");
        }

        if (!string.IsNullOrWhiteSpace(
                row.DisplayName)
            && !TextEquals(
                row.DisplayName,
                user.DisplayName))
        {
            throw new InvalidOperationException(
                "DisplayName 與目前 HR/人員主檔不一致；此欄為參考欄位，不可由權限 Excel 修改。");
        }

        if (!string.IsNullOrWhiteSpace(
                row.Email)
            && !EmailEquals(
                row.Email,
                user.Email))
        {
            throw new InvalidOperationException(
                "Email 與目前 HR/人員主檔不一致；此欄為參考欄位，不可由權限 Excel 修改。");
        }

        if (!string.IsNullOrWhiteSpace(
                row.EmploymentStatus)
            && !TextEquals(
                row.EmploymentStatus,
                employmentStatus))
        {
            throw new InvalidOperationException(
                "EmploymentStatus 與目前 HR/人員主檔不一致；此欄為參考欄位，不可由權限 Excel 修改。");
        }
    }

    private static void ValidateTeamCodes(
        IEnumerable<string> teamCodes,
        PeopleSnapshot snapshot)
    {
        var missing =
            teamCodes
                .Where(
                    x =>
                        !snapshot.TeamsByCode
                            .ContainsKey(x))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"找不到或已停用的 TeamCode：{string.Join(", ", missing)}");
        }
    }

    private static void ValidateEntraBinding(
        string provider,
        Guid? tenantId,
        Guid? objectId,
        int? targetUserId,
        PeopleSnapshot snapshot)
    {
        if (!provider.Equals(
                "EntraId",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var duplicate =
            snapshot.Profiles.Values.Any(
                x =>
                    x.UserId
                        != targetUserId
                    && x.EntraTenantId
                        == tenantId
                    && x.EntraObjectId
                        == objectId);

        if (duplicate)
        {
            throw new InvalidOperationException(
                "此 EntraTenantId + EntraObjectId 已綁定其他系統使用者。");
        }
    }

    private static bool SameInternal(
        V170InternalAuthorizationRow row,
        User user,
        UserIdentityProfile profile,
        PeopleSnapshot snapshot)
    {
        if (user.IsActive
            != row.AdminEnabled)
        {
            return false;
        }

        if (!TextEquals(
                profile.IdentityProvider,
                row.IdentityProvider)
            || profile.EntraTenantId
               != row.EntraTenantId
            || profile.EntraObjectId
               != row.EntraObjectId)
        {
            return false;
        }

        var currentRoles =
            snapshot.RolesByUser
                .GetValueOrDefault(
                    user.UserId)
                ?? [];

        var normalizedCurrentRoles =
            currentRoles
                .Select(NormalizeRole)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

        if (!SetEquals(
                normalizedCurrentRoles,
                row.Roles))
        {
            return false;
        }

        var currentTeams =
            snapshot.TeamsByUser
                .GetValueOrDefault(
                    user.UserId)
                ?? [];

        var currentCodes =
            currentTeams
                .Select(x => x.TeamCode)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (!SetEquals(
                currentCodes,
                row.TeamCodes))
        {
            return false;
        }

        var currentPrimary =
            currentTeams
                .FirstOrDefault(
                    x => x.IsPrimary)
                ?.TeamCode;

        return TextEquals(
            currentPrimary,
            row.PrimaryTeamCode);
    }

    private static bool SameExternal(
        V170ExternalSupervisorRow row,
        User user,
        UserIdentityProfile profile,
        PeopleSnapshot snapshot)
    {
        if (!TextEquals(
                user.DisplayName,
                row.DisplayName)
            || !EmailEquals(
                user.Email,
                row.Email)
            || user.IsActive
               != row.AdminEnabled
            || !TextEquals(
                profile.ExternalOrganization,
                row.ExternalOrganization)
            || !TextEquals(
                profile.ExternalTitle,
                row.ExternalTitle)
            || profile.AuthorizationFrom
               != row.AuthorizationFrom
            || profile.AuthorizationTo
               != row.AuthorizationTo
            || !TextEquals(
                profile.IdentityProvider,
                row.IdentityProvider)
            || profile.EntraTenantId
               != row.EntraTenantId
            || profile.EntraObjectId
               != row.EntraObjectId)
        {
            return false;
        }

        var scopes =
            snapshot.ScopesByUser
                .GetValueOrDefault(
                    user.UserId)
                ?? [];

        var hasOrganization =
            scopes.Any(
                x => x.ScopeType.Equals(
                    DataScopeTypes.Organization,
                    StringComparison.OrdinalIgnoreCase));

        var currentScopeType =
            hasOrganization
                ? DataScopeTypes.Organization
                : scopes.Count > 0
                    ? DataScopeTypes.Team
                    : "";

        if (!TextEquals(
                currentScopeType,
                row.ScopeType))
        {
            return false;
        }

        var currentTeamCodes =
            hasOrganization
                ? []
                : scopes
                    .Where(
                        x => x.TeamCode
                             is not null)
                    .Select(
                        x => x.TeamCode!)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

        if (!SetEquals(
                currentTeamCodes,
                row.ScopeTeamCodes))
        {
            return false;
        }

        return
            GetCapability(
                snapshot,
                user.UserId,
                CapabilityCodes.ExportExcel)
                == row.CanExportExcel

            && GetCapability(
                snapshot,
                user.UserId,
                CapabilityCodes.ExportPdf)
                == row.CanExportPdf;
    }

    private void Stage(
        ImportBatch batch,
        List<V170PeopleBulkPreviewItemDto> result,
        int rowNumber,
        string entityType,
        string action,
        string displayKey,
        object data,
        string? error,
        bool retroactive)
    {
        var status =
            error is null
                ? "Valid"
                : "Error";

        var message =
            error
            ?? (
                retroactive
                && action != "NoChange"
                    ? "此列涉及回溯異動；確認寫入時需要二次確認。"
                    : null);

        db.ImportBatchItems.Add(
            new ImportBatchItem
            {
                ImportBatchId =
                    batch.ImportBatchId,

                RowNumber =
                    rowNumber,

                EntityType =
                    entityType,

                Action =
                    action,

                Status =
                    status,

                DisplayKey =
                    displayKey,

                DataJson =
                    JsonSerializer.Serialize(
                        data,
                        JsonOptions),

                ErrorMessage =
                    error,

                CreatedAt =
                    DateTime.UtcNow
            });

        result.Add(
            new V170PeopleBulkPreviewItemDto(
                rowNumber,
                SheetName(entityType),
                entityType,
                action,
                displayKey,
                status,
                message,
                retroactive));
    }

    private async Task<PeopleSnapshot>
        LoadSnapshotAsync(
            int orgId,
            DateOnly today,
            CancellationToken ct)
    {
        var users =
            await db.Users
                .AsNoTracking()
                .Where(
                    x =>
                        x.OrganizationId
                            == orgId)
                .ToListAsync(ct);

        var userIds =
            users
                .Select(x => x.UserId)
                .ToList();

        var profiles =
            await db.UserIdentityProfiles
                .AsNoTracking()
                .Where(
                    x =>
                        userIds.Contains(
                            x.UserId))
                .ToListAsync(ct);

        var teams =
            await db.Teams
                .AsNoTracking()
                .Where(
                    x =>
                        x.OrganizationId
                            == orgId
                        && x.IsActive)
                .ToListAsync(ct);

        var employment =
            await db.UserEmploymentPeriods
                .AsNoTracking()
                .Where(
                    x =>
                        userIds.Contains(
                            x.UserId)
                        && x.EffectiveFrom
                           <= today
                        && (
                            x.EffectiveTo
                                == null
                            || x.EffectiveTo
                               >= today))
                .ToListAsync(ct);

        var roleRows =
            await (
                from assignment
                    in db.UserRoleAssignments
                        .AsNoTracking()
                join role
                    in db.Roles
                        .AsNoTracking()
                    on assignment.RoleId
                    equals role.RoleId
                where
                    userIds.Contains(
                        assignment.UserId)
                    && assignment.EffectiveFrom
                       <= today
                    && (
                        assignment.EffectiveTo
                            == null
                        || assignment.EffectiveTo
                           >= today)
                select new
                {
                    assignment.UserId,
                    role.RoleCode
                })
                .ToListAsync(ct);

        var teamRows =
            await (
                from assignment
                    in db.UserTeamAssignments
                        .AsNoTracking()
                join team
                    in db.Teams
                        .AsNoTracking()
                    on assignment.TeamId
                    equals team.TeamId
                where
                    userIds.Contains(
                        assignment.UserId)
                    && assignment.EffectiveFrom
                       <= today
                    && (
                        assignment.EffectiveTo
                            == null
                        || assignment.EffectiveTo
                           >= today)
                select new TeamState(
                    assignment.UserId,
                    team.TeamId,
                    team.TeamCode,
                    assignment.IsPrimary))
                .ToListAsync(ct);

        var scopeRows =
            await (
                from scope
                    in db.UserDataScopes
                        .AsNoTracking()
                join team
                    in db.Teams
                        .AsNoTracking()
                    on scope.TeamId
                    equals team.TeamId
                    into teamJoin
                from team
                    in teamJoin.DefaultIfEmpty()
                where
                    userIds.Contains(
                        scope.UserId)
                    && scope.EffectiveFrom
                       <= today
                    && (
                        scope.EffectiveTo
                            == null
                        || scope.EffectiveTo
                           >= today)
                select new ScopeState(
                    scope.UserId,
                    scope.ScopeType,
                    scope.OrganizationId,
                    scope.TeamId,
                    team == null
                        ? null
                        : team.TeamCode))
                .ToListAsync(ct);

        var capabilityRows =
            await db.UserCapabilities
                .AsNoTracking()
                .Where(
                    x =>
                        userIds.Contains(
                            x.UserId)
                        && x.EffectiveFrom
                           <= today
                        && (
                            x.EffectiveTo
                                == null
                            || x.EffectiveTo
                               >= today))
                .ToListAsync(ct);

        var employmentByUser =
            employment
                .GroupBy(x => x.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .OrderByDescending(
                            x => x.EffectiveFrom)
                        .First()
                        .EmploymentStatus);

        var rolesByUser =
            roleRows
                .GroupBy(x => x.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .Select(
                            x => x.RoleCode)
                        .ToList());

        var teamsByUser =
            teamRows
                .GroupBy(x => x.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToList());

        var scopesByUser =
            scopeRows
                .GroupBy(x => x.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToList());

        var capabilitiesByUser =
            capabilityRows
                .GroupBy(x => x.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .GroupBy(
                            x => x.CapabilityCode,
                            StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            h => h.Key,
                            h => h
                                .OrderByDescending(
                                    x => x.EffectiveFrom)
                                .First()
                                .IsAllowed,
                            StringComparer.OrdinalIgnoreCase));

        var userDictionary =
            users.ToDictionary(
                x => x.UserId);

        var profileDictionary =
            profiles.ToDictionary(
                x => x.UserId);

        var userIdByCode =
            profiles.ToDictionary(
                x => x.UserCode,
                x => x.UserId,
                StringComparer.OrdinalIgnoreCase);

        var teamsByCode =
            teams.ToDictionary(
                x => x.TeamCode,
                x => x,
                StringComparer.OrdinalIgnoreCase);

        return new PeopleSnapshot(
            userDictionary,
            profileDictionary,
            userIdByCode,
            teamsByCode,
            employmentByUser,
            rolesByUser,
            teamsByUser,
            scopesByUser,
            capabilitiesByUser);
    }

    private static bool GetCapability(
        PeopleSnapshot snapshot,
        int userId,
        string capability)
    {
        if (!snapshot.CapabilitiesByUser
            .TryGetValue(
                userId,
                out var caps))
        {
            return false;
        }

        return caps.TryGetValue(
                   capability,
                   out var value)
               && value;
    }

    private static DateOnly ClampEffectiveDate(
        DateOnly today,
        DateOnly? from,
        DateOnly? to)
    {
        var result =
            today;

        if (from.HasValue
            && result < from.Value)
        {
            result =
                from.Value;
        }

        if (to.HasValue
            && result > to.Value)
        {
            result =
                to.Value;
        }

        return result;
    }

    private static int RequireAdminOrganization(
        CurrentUserDto admin)
    {
        if (!admin.Roles.Any(
                x => x.Equals(
                    "admin",
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new UnauthorizedAccessException(
                "只有管理者可以執行人員與權限批次作業。");
        }

        return admin.OrganizationId
            ?? throw new InvalidOperationException(
                "目前管理者缺少 OrganizationId。");
    }

    private void AddAudit(
        CurrentUserDto admin,
        string action,
        object values)
    {
        db.AuditLogs.Add(
            new AuditLog
            {
                UserId =
                    admin.UserId,

                EntityType =
                    "PeopleBulk",

                EntityId =
                    admin.OrganizationId
                        ?.ToString()
                    ?? "",

                Action =
                    action,

                NewValues =
                    JsonSerializer.Serialize(
                        values,
                        JsonOptions),

                CreatedAt =
                    DateTime.UtcNow
            });
    }

    private static string NormalizeRole(
        string role)
        => (role ?? "")
            .Trim()
            .ToLowerInvariant() switch
        {
            "government" =>
                "supervisor",

            var value =>
                value
        };

    private static string Yn(
        bool value)
        => value
            ? "Y"
            : "N";

    private static string DateText(
        DateOnly value)
        => value.ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);

    private static bool SetEquals(
        IEnumerable<string> left,
        IEnumerable<string> right)
        => new HashSet<string>(
                left,
                StringComparer.OrdinalIgnoreCase)
            .SetEquals(right);

    private static bool TextEquals(
        string? left,
        string? right)
        => string.Equals(
            left?.Trim(),
            right?.Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static bool EmailEquals(
        string? left,
        string? right)
        => string.Equals(
            left?.Trim(),
            right?.Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static string SheetName(
        string entityType)
        => entityType switch
        {
            "InternalAuthorization" =>
                "InternalAuthorization",

            "ExternalSupervisor" =>
                "ExternalSupervisors",

            _ =>
                entityType
        };

    private static void EnsureHeaders(
        IReadOnlyList<string[]> rows,
        IReadOnlyList<string> required,
        string sheetName)
    {
        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                $"工作表 {sheetName} 沒有標題列。");
        }

        var headers =
            rows[0]
                .Where(
                    x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var missing =
            required
                .Where(
                    x => !headers.Contains(x))
                .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"工作表 {sheetName} 缺少欄位：{string.Join(", ", missing)}");
        }
    }

    private static string? Get(
        string[] row,
        string[] headers,
        string name)
    {
        var index =
            Array.FindIndex(
                headers,
                x => x.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase));

        if (index < 0
            || index >= row.Length)
        {
            return null;
        }

        var value =
            row[index]?.Trim();

        return string.IsNullOrWhiteSpace(
                value)
            ? null
            : value;
    }

    private static IReadOnlyList<string[]>
        ReadSheet(
            SpreadsheetDocument doc,
            string sheetName)
    {
        var workbook =
            doc.WorkbookPart
            ?? throw new InvalidOperationException(
                "Excel Workbook 不正確。");

        var sheet =
            workbook.Workbook
                .Sheets?
                .Elements<Sheet>()
                .FirstOrDefault(
                    x =>
                        string.Equals(
                            x.Name?.Value,
                            sheetName,
                            StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Excel 缺少工作表：{sheetName}");

        var worksheetPart =
            (WorksheetPart)
            workbook.GetPartById(
                sheet.Id!);

        var result =
            new List<string[]>();

        foreach (var row
                 in worksheetPart.Worksheet
                     .Descendants<Row>())
        {
            var values =
                new string[64];

            var sequentialIndex =
                0;

            foreach (var cell
                     in row.Elements<Cell>())
            {
                var index =
                    ColumnIndex(
                        cell.CellReference?.Value);

                if (index < 0)
                {
                    index =
                        sequentialIndex;
                }

                if (index >= 0
                    && index < values.Length)
                {
                    values[index] =
                        CellText(
                            workbook,
                            cell);
                }

                sequentialIndex =
                    Math.Max(
                        sequentialIndex + 1,
                        index + 1);
            }

            result.Add(values);
        }

        return result;
    }

    private static string CellText(
        WorkbookPart workbook,
        Cell cell)
    {
        if (cell.DataType?.Value
            == CellValues.SharedString)
        {
            if (int.TryParse(
                    cell.CellValue?.Text,
                    out var index))
            {
                return workbook
                    .SharedStringTablePart?
                    .SharedStringTable?
                    .Elements<SharedStringItem>()
                    .ElementAtOrDefault(index)
                    ?.InnerText
                    ?? "";
            }
        }

        if (cell.DataType?.Value
            == CellValues.InlineString)
        {
            return cell.InlineString
                ?.InnerText
                ?? "";
        }

        return cell.CellValue
            ?.Text
            ?? cell.InnerText
            ?? "";
    }

    private static int ColumnIndex(
        string? reference)
    {
        if (string.IsNullOrWhiteSpace(
                reference))
        {
            return -1;
        }

        var value =
            0;

        foreach (var ch
                 in reference)
        {
            if (!char.IsLetter(ch))
            {
                break;
            }

            value =
                value * 26
                + (
                    char.ToUpperInvariant(ch)
                    - 'A'
                    + 1);
        }

        return value > 0
            ? value - 1
            : -1;
    }

    private static void AddInstructionsSheet(
        WorkbookPart wb,
        Sheets sheets,
        uint sheetId)
    {
        AddSheet(
            wb,
            sheets,
            sheetId,
            "Instructions",
            new List<string[]>
            {
                new[]
                {
                    "項目",
                    "說明"
                },
                new[]
                {
                    "基本流程",
                    "下載目前設定 → 修改授權欄位 → 上傳 → 預覽與驗證 → 全部正確後才可確認批次更新。"
                },
                new[]
                {
                    "Internal HR Facts",
                    "EmployeeNo、DisplayName、Email、EmploymentStatus 為參考欄位，不可由本權限 Excel 修改。"
                },
                new[]
                {
                    "Internal Roles",
                    "只允許 visitor、leader、admin。External Supervisor 不可由 InternalAuthorization 設定。"
                },
                new[]
                {
                    "TeamCodes",
                    "多個 TeamCode 使用分號 ; 分隔；Visitor/Leader 至少一個 Team，且 PrimaryTeamCode 必須包含在 TeamCodes。"
                },
                new[]
                {
                    "External Supervisor",
                    "新增時 UserCode 留白；既有人員更新時保留系統 UserCode。"
                },
                new[]
                {
                    "ScopeType",
                    "只允許 Organization 或 Team。Team Scope 時 ScopeTeamCodes 必填。"
                },
                new[]
                {
                    "IdentityProvider",
                    "只允許 Demo 或 EntraId。EntraId 必須同時提供 EntraTenantId 與 EntraObjectId。"
                },
                new[]
                {
                    "ChangeEffectiveFrom",
                    "格式 yyyy-MM-dd；早於今日屬回溯異動，Confirm 時需要二次確認。"
                },
                new[]
                {
                    "AdminEnabled",
                    "可使用 Y/N、Yes/No、True/False、1/0、啟用/停用。"
                },
                new[]
                {
                    "安全機制",
                    "上傳 Excel 只建立 Preview，不會直接修改資料。"
                }
            });
    }

    private static void AddSheet(
        WorkbookPart wb,
        Sheets sheets,
        uint sheetId,
        string name,
        IEnumerable<string[]> rows)
    {
        var worksheetPart =
            wb.AddNewPart<WorksheetPart>();

        var sheetData =
            new SheetData();

        worksheetPart.Worksheet =
            new Worksheet(
                sheetData);

        foreach (var values
                 in rows)
        {
            var row =
                new Row();

            foreach (var value
                     in values)
            {
                row.Append(
                    new Cell
                    {
                        DataType =
                            CellValues.InlineString,

                        InlineString =
                            new InlineString(
                                new Text(
                                    value ?? ""))
                    });
            }

            sheetData.Append(row);
        }

        worksheetPart.Worksheet.Save();

        sheets.Append(
            new Sheet
            {
                Id =
                    wb.GetIdOfPart(
                        worksheetPart),

                SheetId =
                    sheetId,

                Name =
                    name
            });
    }

    private sealed record TeamState(
        int UserId,
        int TeamId,
        string TeamCode,
        bool IsPrimary);

    private sealed record ScopeState(
        int UserId,
        string ScopeType,
        int? OrganizationId,
        int? TeamId,
        string? TeamCode);

    private sealed record PeopleSnapshot(
        IReadOnlyDictionary<int, User> Users,
        IReadOnlyDictionary<int, UserIdentityProfile> Profiles,
        IReadOnlyDictionary<string, int> UserIdByCode,
        IReadOnlyDictionary<string, Team> TeamsByCode,
        IReadOnlyDictionary<int, string> EmploymentByUser,
        IReadOnlyDictionary<int, List<string>> RolesByUser,
        IReadOnlyDictionary<int, List<TeamState>> TeamsByUser,
        IReadOnlyDictionary<int, List<ScopeState>> ScopesByUser,
        IReadOnlyDictionary<
            int,
            Dictionary<string, bool>>
            CapabilitiesByUser);
}
