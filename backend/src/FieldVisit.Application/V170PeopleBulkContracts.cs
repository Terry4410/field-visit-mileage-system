using System.Globalization;

namespace FieldVisit.Application;

public sealed record V170InternalAuthorizationRawRow(
    int RowNumber,
    string? UserCode,
    string? EmployeeNo,
    string? DisplayName,
    string? Email,
    string? EmploymentStatus,
    string? AdminEnabled,
    string? Roles,
    string? TeamCodes,
    string? PrimaryTeamCode,
    string? ChangeEffectiveFrom,
    string? IdentityProvider,
    string? EntraTenantId,
    string? EntraObjectId);

public sealed record V170ExternalSupervisorRawRow(
    int RowNumber,
    string? UserCode,
    string? DisplayName,
    string? Email,
    string? ExternalOrganization,
    string? ExternalTitle,
    string? AuthorizationFrom,
    string? AuthorizationTo,
    string? AdminEnabled,
    string? ScopeType,
    string? ScopeTeamCodes,
    string? CanExportExcel,
    string? CanExportPdf,
    string? IdentityProvider,
    string? EntraTenantId,
    string? EntraObjectId,
    string? ChangeEffectiveFrom);

public sealed record V170InternalAuthorizationRow(
    int RowNumber,
    string UserCode,
    string? EmployeeNo,
    string? DisplayName,
    string? Email,
    string? EmploymentStatus,
    bool AdminEnabled,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> TeamCodes,
    string? PrimaryTeamCode,
    DateOnly ChangeEffectiveFrom,
    string IdentityProvider,
    Guid? EntraTenantId,
    Guid? EntraObjectId);

public sealed record V170ExternalSupervisorRow(
    int RowNumber,
    string? UserCode,
    string DisplayName,
    string Email,
    string ExternalOrganization,
    string? ExternalTitle,
    DateOnly AuthorizationFrom,
    DateOnly AuthorizationTo,
    bool AdminEnabled,
    string ScopeType,
    IReadOnlyList<string> ScopeTeamCodes,
    bool CanExportExcel,
    bool CanExportPdf,
    string IdentityProvider,
    Guid? EntraTenantId,
    Guid? EntraObjectId,
    DateOnly ChangeEffectiveFrom);

public sealed record V170PeopleBulkPreviewItemDto(
    int RowNumber,
    string Sheet,
    string EntityType,
    string Action,
    string DisplayKey,
    string Status,
    string? Message,
    bool IsRetroactive);

public sealed record V170PeopleBulkPreviewDto(
    Guid ImportBatchId,
    int TotalCount,
    int ValidCount,
    int ErrorCount,
    bool RequiresRetroactiveConfirmation,
    IReadOnlyList<V170PeopleBulkPreviewItemDto> Items);

public sealed record V170PeopleBulkConfirmRequest(
    bool ConfirmRetroactive = false);

public sealed record V170PeopleBulkConfirmResultDto(
    Guid ImportBatchId,
    int Created,
    int Updated,
    int Unchanged,
    int Failed,
    IReadOnlyList<string> Errors);

public interface IV170PeopleBulkWorkbookService
{
    Task<ReportExportContext> ExportCurrentAsync(
        CurrentUserDto admin,
        CancellationToken ct);

    Task<ReportExportContext> CreateTemplateAsync(
        CurrentUserDto admin,
        CancellationToken ct);

    Task<V170PeopleBulkPreviewDto> PreviewAsync(
        CurrentUserDto admin,
        byte[] content,
        CancellationToken ct);

    Task<ReportExportContext> CreateErrorReportAsync(
        CurrentUserDto admin,
        Guid importBatchId,
        CancellationToken ct);

    Task<V170PeopleBulkConfirmResultDto> ConfirmAsync(
        CurrentUserDto admin,
        Guid importBatchId,
        V170PeopleBulkConfirmRequest request,
        CancellationToken ct);
}

public static class V170PeopleBulkRules
{
    private static readonly HashSet<string> InternalRoles =
        new(
            new[]
            {
                "visitor",
                "leader",
                "admin"
            },
            StringComparer.OrdinalIgnoreCase);

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "yyyy/M/d",
        "yyyy/MM/dd"
    ];

    public static V170InternalAuthorizationRow NormalizeInternal(
        V170InternalAuthorizationRawRow raw)
    {
        var userCode =
            RequiredCode(
                raw.UserCode,
                "UserCode");

        var roles =
            SplitCodes(raw.Roles)
                .Select(x => x.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

        if (roles.Count == 0)
            throw new InvalidOperationException(
                "Roles 必填。");

        if (roles.Any(x => !InternalRoles.Contains(x)))
            throw new InvalidOperationException(
                "Internal User 的 Roles 只允許 visitor、leader、admin。");

        var teamCodes =
            SplitCodes(raw.TeamCodes)
                .Select(x => x.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        var primary =
            OptionalCode(raw.PrimaryTeamCode)
                ?.ToUpperInvariant();

        if (teamCodes.Count > 0
            && string.IsNullOrWhiteSpace(primary))
        {
            throw new InvalidOperationException(
                "有 TeamCodes 時必須指定 PrimaryTeamCode。");
        }

        if (primary is not null
            && !teamCodes.Contains(
                primary,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "PrimaryTeamCode 必須包含在 TeamCodes 內。");
        }

        if (teamCodes.Count == 0
            && roles.Any(
                x => x is "visitor" or "leader"))
        {
            throw new InvalidOperationException(
                "Visitor 或 Leader 至少需要一個 TeamCode。");
        }

        if (teamCodes.Count == 0
            && primary is not null)
        {
            throw new InvalidOperationException(
                "沒有 TeamCodes 時不可指定 PrimaryTeamCode。");
        }

        var identity =
            NormalizeIdentity(
                raw.IdentityProvider,
                raw.EntraTenantId,
                raw.EntraObjectId);

        return new V170InternalAuthorizationRow(
            raw.RowNumber,
            userCode,
            OptionalText(raw.EmployeeNo),
            OptionalText(raw.DisplayName),
            NormalizeOptionalEmail(raw.Email),
            OptionalText(raw.EmploymentStatus),
            ParseBoolean(
                raw.AdminEnabled,
                "AdminEnabled"),
            roles,
            teamCodes,
            primary,
            ParseDate(
                raw.ChangeEffectiveFrom,
                "ChangeEffectiveFrom"),
            identity.Provider,
            identity.TenantId,
            identity.ObjectId);
    }

    public static V170ExternalSupervisorRow NormalizeExternal(
        V170ExternalSupervisorRawRow raw)
    {
        var userCode =
            OptionalCode(raw.UserCode);

        var displayName =
            RequiredText(
                raw.DisplayName,
                "DisplayName",
                100);

        var email =
            RequiredEmail(raw.Email);

        var organization =
            RequiredText(
                raw.ExternalOrganization,
                "ExternalOrganization",
                200);

        var title =
            OptionalText(raw.ExternalTitle);

        if (title?.Length > 200)
            throw new InvalidOperationException(
                "ExternalTitle 不可超過 200 個字元。");

        var authorizationFrom =
            ParseDate(
                raw.AuthorizationFrom,
                "AuthorizationFrom");

        var authorizationTo =
            ParseDate(
                raw.AuthorizationTo,
                "AuthorizationTo");

        if (authorizationTo < authorizationFrom)
            throw new InvalidOperationException(
                "AuthorizationTo 不可早於 AuthorizationFrom。");

        var changeEffectiveFrom =
            ParseDate(
                raw.ChangeEffectiveFrom,
                "ChangeEffectiveFrom");

        if (changeEffectiveFrom < authorizationFrom
            || changeEffectiveFrom > authorizationTo)
        {
            throw new InvalidOperationException(
                "ChangeEffectiveFrom 必須位於授權起訖期間內。");
        }

        var scope =
            (raw.ScopeType ?? "")
                .Trim();

        if (scope.Equals(
                "Organization",
                StringComparison.OrdinalIgnoreCase))
        {
            scope = "Organization";
        }
        else if (scope.Equals(
                     "Team",
                     StringComparison.OrdinalIgnoreCase))
        {
            scope = "Team";
        }
        else
        {
            throw new InvalidOperationException(
                "ScopeType 只允許 Organization 或 Team。");
        }

        var scopeTeams =
            SplitCodes(raw.ScopeTeamCodes)
                .Select(x => x.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (scope == "Organization"
            && scopeTeams.Count > 0)
        {
            throw new InvalidOperationException(
                "Organization Scope 不可設定 ScopeTeamCodes。");
        }

        if (scope == "Team"
            && scopeTeams.Count == 0)
        {
            throw new InvalidOperationException(
                "Team Scope 至少需要一個 ScopeTeamCode。");
        }

        var identity =
            NormalizeIdentity(
                raw.IdentityProvider,
                raw.EntraTenantId,
                raw.EntraObjectId);

        return new V170ExternalSupervisorRow(
            raw.RowNumber,
            userCode,
            displayName,
            email,
            organization,
            title,
            authorizationFrom,
            authorizationTo,
            ParseBoolean(
                raw.AdminEnabled,
                "AdminEnabled"),
            scope,
            scopeTeams,
            ParseBoolean(
                raw.CanExportExcel,
                "CanExportExcel"),
            ParseBoolean(
                raw.CanExportPdf,
                "CanExportPdf"),
            identity.Provider,
            identity.TenantId,
            identity.ObjectId,
            changeEffectiveFrom);
    }

    public static bool IsRetroactive(
        DateOnly changeEffectiveFrom,
        DateOnly today)
        => changeEffectiveFrom < today;

    public static IReadOnlyList<string> SplitCodes(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw
            .Split(
                [',', ';', '，', '、', '|'],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .ToList();
    }

    public static bool ParseBoolean(
        string? raw,
        string fieldName)
    {
        var value =
            (raw ?? "")
                .Trim()
                .ToLowerInvariant();

        return value switch
        {
            "y" or "yes" or "true" or "1"
                or "啟用" or "是" => true,

            "n" or "no" or "false" or "0"
                or "停用" or "否" => false,

            _ => throw new InvalidOperationException(
                $"{fieldName} 只允許 Y/N、Yes/No、True/False、1/0、啟用/停用。")
        };
    }

    public static DateOnly ParseDate(
        string? raw,
        string fieldName)
    {
        var value =
            (raw ?? "")
                .Trim();

        foreach (var format in DateFormats)
        {
            if (DateOnly.TryParseExact(
                    value,
                    format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var result))
            {
                return result;
            }
        }

        throw new InvalidOperationException(
            $"{fieldName} 日期格式必須為 yyyy-MM-dd。");
    }

    private static (
        string Provider,
        Guid? TenantId,
        Guid? ObjectId)
        NormalizeIdentity(
            string? providerRaw,
            string? tenantRaw,
            string? objectRaw)
    {
        var provider =
            string.IsNullOrWhiteSpace(providerRaw)
                ? "Demo"
                : providerRaw.Trim();

        if (provider.Equals(
                "demo",
                StringComparison.OrdinalIgnoreCase))
        {
            provider = "Demo";
        }
        else if (
            provider.Equals(
                "entra",
                StringComparison.OrdinalIgnoreCase)
            || provider.Equals(
                "entraid",
                StringComparison.OrdinalIgnoreCase))
        {
            provider = "EntraId";
        }
        else
        {
            throw new InvalidOperationException(
                "IdentityProvider 只允許 Demo 或 EntraId。");
        }

        var tenant =
            ParseOptionalGuid(
                tenantRaw,
                "EntraTenantId");

        var objectId =
            ParseOptionalGuid(
                objectRaw,
                "EntraObjectId");

        if (provider == "EntraId"
            && (!tenant.HasValue
                || !objectId.HasValue))
        {
            throw new InvalidOperationException(
                "IdentityProvider=EntraId 時，EntraTenantId 與 EntraObjectId 都必填。");
        }

        if (provider == "Demo"
            && (tenant.HasValue
                || objectId.HasValue))
        {
            throw new InvalidOperationException(
                "IdentityProvider=Demo 時不可填寫 EntraTenantId 或 EntraObjectId。");
        }

        return (
            provider,
            tenant,
            objectId);
    }

    private static Guid? ParseOptionalGuid(
        string? raw,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (!Guid.TryParse(
                raw.Trim(),
                out var value))
        {
            throw new InvalidOperationException(
                $"{fieldName} 必須是有效的 GUID。");
        }

        return value;
    }

    private static string RequiredCode(
        string? raw,
        string fieldName)
    {
        var value =
            (raw ?? "")
                .Trim();

        if (value.Length == 0)
            throw new InvalidOperationException(
                $"{fieldName} 必填。");

        if (value.Length > 50)
            throw new InvalidOperationException(
                $"{fieldName} 不可超過 50 個字元。");

        return value;
    }

    private static string? OptionalCode(
        string? raw)
    {
        var value =
            OptionalText(raw);

        if (value?.Length > 50)
            throw new InvalidOperationException(
                "UserCode 不可超過 50 個字元。");

        return value;
    }

    private static string RequiredText(
        string? raw,
        string fieldName,
        int maxLength)
    {
        var value =
            (raw ?? "")
                .Trim();

        if (value.Length == 0)
            throw new InvalidOperationException(
                $"{fieldName} 必填。");

        if (value.Length > maxLength)
            throw new InvalidOperationException(
                $"{fieldName} 不可超過 {maxLength} 個字元。");

        return value;
    }

    private static string? OptionalText(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Trim();
    }

    private static string? NormalizeOptionalEmail(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Trim()
            .ToLowerInvariant();
    }

    private static string RequiredEmail(
        string? raw)
    {
        var value =
            (raw ?? "")
                .Trim()
                .ToLowerInvariant();

        if (value.Length == 0
            || !value.Contains('@')
            || value.Length > 256)
        {
            throw new InvalidOperationException(
                "Email 格式不正確。");
        }

        return value;
    }
}
