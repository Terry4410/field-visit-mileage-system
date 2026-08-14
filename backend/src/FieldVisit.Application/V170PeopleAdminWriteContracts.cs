using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public sealed record SaveExternalSupervisorRequest(
    string DisplayName,
    string Email,
    string ExternalOrganization,
    string? ExternalTitle,
    DateOnly AuthorizationFrom,
    DateOnly AuthorizationTo,
    string ScopeType,
    IReadOnlyList<int> TeamIds,
    bool CanExportExcel,
    bool CanExportPdf,
    bool AdminEnabled = true,
    string? IdentityProvider = null,
    Guid? EntraTenantId = null,
    Guid? EntraObjectId = null);


public sealed record V170IdentityBindingInput(
    bool IsSpecified,
    string? IdentityProvider,
    Guid? EntraTenantId,
    Guid? EntraObjectId);

public static class V170IdentityBindingRules
{
    public static V170IdentityBindingInput Normalize(
        string? identityProvider,
        Guid? entraTenantId,
        Guid? entraObjectId,
        bool defaultToDemo)
    {
        var providerProvided =
            !string.IsNullOrWhiteSpace(
                identityProvider);

        var anyIdentityField =
            providerProvided
            || entraTenantId.HasValue
            || entraObjectId.HasValue;

        if (!anyIdentityField
            && !defaultToDemo)
        {
            return new V170IdentityBindingInput(
                false,
                null,
                null,
                null);
        }

        if (!providerProvided
            && (
                entraTenantId.HasValue
                || entraObjectId.HasValue))
        {
            throw new InvalidOperationException(
                "填寫 EntraTenantId 或 EntraObjectId 時，IdentityProvider 必填。");
        }

        var provider =
            providerProvided
                ? identityProvider!.Trim()
                : "Demo";

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

        if (provider == "EntraId"
            && (
                !entraTenantId.HasValue
                || !entraObjectId.HasValue))
        {
            throw new InvalidOperationException(
                "IdentityProvider=EntraId 時，EntraTenantId 與 EntraObjectId 都必填。");
        }

        if (provider == "Demo"
            && (
                entraTenantId.HasValue
                || entraObjectId.HasValue))
        {
            throw new InvalidOperationException(
                "IdentityProvider=Demo 時不可填寫 EntraTenantId 或 EntraObjectId。");
        }

        return new V170IdentityBindingInput(
            true,
            provider,
            entraTenantId,
            entraObjectId);
    }
}

public static class V170ExternalSupervisorRules
{
    public static SaveExternalSupervisorRequest Normalize(
        SaveExternalSupervisorRequest request)
    {
        var name =
            (request.DisplayName ?? "").Trim();

        if (name.Length == 0)
            throw new InvalidOperationException(
                "外部督導姓名必填。");

        if (name.Length > 100)
            throw new InvalidOperationException(
                "外部督導姓名不可超過 100 個字元。");

        var email =
            (request.Email ?? "").Trim()
                .ToLowerInvariant();

        if (email.Length == 0
            || !email.Contains('@')
            || email.Length > 256)
        {
            throw new InvalidOperationException(
                "請輸入正確的 Email。");
        }

        var organization =
            (request.ExternalOrganization ?? "").Trim();

        if (organization.Length == 0)
            throw new InvalidOperationException(
                "外部督導所屬機構必填。");

        if (organization.Length > 200)
            throw new InvalidOperationException(
                "所屬機構不可超過 200 個字元。");

        var title =
            string.IsNullOrWhiteSpace(
                request.ExternalTitle)
                ? null
                : request.ExternalTitle.Trim();

        if (title?.Length > 200)
            throw new InvalidOperationException(
                "職稱不可超過 200 個字元。");

        if (request.AuthorizationTo
            < request.AuthorizationFrom)
        {
            throw new InvalidOperationException(
                "授權結束日不可早於開始日。");
        }

        var scope =
            (request.ScopeType ?? "")
                .Trim();

        if (!scope.Equals(
                DataScopeTypes.Organization,
                StringComparison.OrdinalIgnoreCase)
            && !scope.Equals(
                DataScopeTypes.Team,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Data Scope 只允許 Organization 或 Team。");
        }

        scope =
            scope.Equals(
                DataScopeTypes.Organization,
                StringComparison.OrdinalIgnoreCase)
                ? DataScopeTypes.Organization
                : DataScopeTypes.Team;

        var teamIds =
            (request.TeamIds ?? [])
                .Where(x => x > 0)
                .Distinct()
                .ToList();

        if (scope == DataScopeTypes.Organization
            && teamIds.Count > 0)
        {
            throw new InvalidOperationException(
                "Organization Scope 不可同時指定 Team。");
        }

        if (scope == DataScopeTypes.Team
            && teamIds.Count == 0)
        {
            throw new InvalidOperationException(
                "Team Scope 至少需要一個 Team。");
        }

        var identity =
            V170IdentityBindingRules.Normalize(
                request.IdentityProvider,
                request.EntraTenantId,
                request.EntraObjectId,
                defaultToDemo: true);

        return request with
        {
            DisplayName = name,
            Email = email,
            ExternalOrganization = organization,
            ExternalTitle = title,
            ScopeType = scope,
            TeamIds = teamIds,
            IdentityProvider =
                identity.IdentityProvider,
            EntraTenantId =
                identity.EntraTenantId,
            EntraObjectId =
                identity.EntraObjectId
        };
    }
}


public sealed record UpdateExternalSupervisorRequest(
    string DisplayName,
    string Email,
    string ExternalOrganization,
    string? ExternalTitle,
    DateOnly AuthorizationFrom,
    DateOnly AuthorizationTo,
    string ScopeType,
    IReadOnlyList<int> TeamIds,
    bool CanExportExcel,
    bool CanExportPdf,
    bool AdminEnabled,
    DateOnly ChangeEffectiveFrom,
    bool ConfirmRetroactive = false,
    string? IdentityProvider = null,
    Guid? EntraTenantId = null,
    Guid? EntraObjectId = null);

public static class V170ExternalSupervisorUpdateRules
{
    public static UpdateExternalSupervisorRequest Normalize(
        UpdateExternalSupervisorRequest request,
        DateOnly today)
    {
        var normalized =
            V170ExternalSupervisorRules.Normalize(
                new SaveExternalSupervisorRequest(
                    request.DisplayName,
                    request.Email,
                    request.ExternalOrganization,
                    request.ExternalTitle,
                    request.AuthorizationFrom,
                    request.AuthorizationTo,
                    request.ScopeType,
                    request.TeamIds,
                    request.CanExportExcel,
                    request.CanExportPdf,
                    request.AdminEnabled));

        var identity =
            V170IdentityBindingRules.Normalize(
                request.IdentityProvider,
                request.EntraTenantId,
                request.EntraObjectId,
                defaultToDemo: false);

        if (request.ChangeEffectiveFrom
            < request.AuthorizationFrom
            || request.ChangeEffectiveFrom
            > request.AuthorizationTo)
        {
            throw new InvalidOperationException(
                "異動生效日必須位於授權起訖期間內。");
        }

        if (request.ChangeEffectiveFrom < today
            && !request.ConfirmRetroactive)
        {
            throw new InvalidOperationException(
                "異動生效日早於今天，請二次確認回溯異動。");
        }

        return request with
        {
            DisplayName =
                normalized.DisplayName,

            Email =
                normalized.Email,

            ExternalOrganization =
                normalized.ExternalOrganization,

            ExternalTitle =
                normalized.ExternalTitle,

            ScopeType =
                normalized.ScopeType,

            TeamIds =
                normalized.TeamIds,

            IdentityProvider =
                identity.IdentityProvider,

            EntraTenantId =
                identity.EntraTenantId,

            EntraObjectId =
                identity.EntraObjectId
        };
    }
}


public sealed record InternalTeamAssignmentInput(
    int TeamId,
    bool IsPrimary);

public sealed record UpdateInternalUserAccessRequest(
    IReadOnlyList<string> Roles,
    IReadOnlyList<InternalTeamAssignmentInput> TeamAssignments,
    bool AdminEnabled,
    DateOnly ChangeEffectiveFrom,
    bool ConfirmRetroactive = false,
    string? IdentityProvider = null,
    Guid? EntraTenantId = null,
    Guid? EntraObjectId = null);

public static class V170InternalUserAccessRules
{
    private static readonly HashSet<string>
        AllowedRoles =
            new(
                new[]
                {
                    "visitor",
                    "leader",
                    "admin"
                },
                StringComparer.OrdinalIgnoreCase);

    public static UpdateInternalUserAccessRequest Normalize(
        UpdateInternalUserAccessRequest request,
        DateOnly today)
    {
        var roles =
            (request.Roles ?? [])
                .Select(NormalizeRole)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

        if (roles.Count == 0)
        {
            throw new InvalidOperationException(
                "Internal User 至少需要一個角色。");
        }

        if (roles.Any(
                x => !AllowedRoles.Contains(x)))
        {
            throw new InvalidOperationException(
                "Internal User 只允許 Visitor、Leader、Admin 角色；Supervisor 必須使用 External Supervisor 管理。");
        }

        var teams =
            (request.TeamAssignments ?? [])
                .ToList();

        if (teams.Any(x => x.TeamId <= 0))
        {
            throw new InvalidOperationException(
                "TeamId 不正確。");
        }

        if (teams.Select(x => x.TeamId)
            .Distinct()
            .Count() != teams.Count)
        {
            throw new InvalidOperationException(
                "同一個 Team 不可重複設定。");
        }

        var primaryCount =
            teams.Count(x => x.IsPrimary);

        if (teams.Count > 0
            && primaryCount != 1)
        {
            throw new InvalidOperationException(
                "有 Team Membership 時必須且只能指定一個 Primary Team。");
        }

        if (teams.Count == 0
            && roles.Any(
                x =>
                    x == "visitor"
                    || x == "leader"))
        {
            throw new InvalidOperationException(
                "Visitor 或 Leader 至少需要一個 Team Membership。");
        }

        if (request.ChangeEffectiveFrom < today
            && !request.ConfirmRetroactive)
        {
            throw new InvalidOperationException(
                "異動生效日早於今天，請二次確認回溯異動。");
        }

        var identity =
            V170IdentityBindingRules.Normalize(
                request.IdentityProvider,
                request.EntraTenantId,
                request.EntraObjectId,
                defaultToDemo: false);

        return request with
        {
            Roles = roles,
            TeamAssignments = teams,
            IdentityProvider =
                identity.IdentityProvider,
            EntraTenantId =
                identity.EntraTenantId,
            EntraObjectId =
                identity.EntraObjectId
        };
    }

    public static string NormalizeRole(
        string role)
        => (role ?? "")
            .Trim()
            .ToLowerInvariant() switch
        {
            "visitor" => "visitor",
            "leader" => "leader",
            "admin" => "admin",
            "supervisor" => "supervisor",
            "government" => "supervisor",
            var value => value
        };
}

public interface IV170PeopleAdminWriter
{
    Task<int> CreateExternalSupervisorAsync(
        CurrentUserDto admin,
        SaveExternalSupervisorRequest request,
        CancellationToken ct);

    Task UpdateExternalSupervisorAsync(
        CurrentUserDto admin,
        int userId,
        UpdateExternalSupervisorRequest request,
        CancellationToken ct);

    Task UpdateInternalUserAccessAsync(
        CurrentUserDto admin,
        int userId,
        UpdateInternalUserAccessRequest request,
        CancellationToken ct);
}
