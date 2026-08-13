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
    bool AdminEnabled = true);

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

        return request with
        {
            DisplayName = name,
            Email = email,
            ExternalOrganization = organization,
            ExternalTitle = title,
            ScopeType = scope,
            TeamIds = teamIds
        };
    }
}

public interface IV170PeopleAdminWriter
{
    Task<int> CreateExternalSupervisorAsync(
        CurrentUserDto admin,
        SaveExternalSupervisorRequest request,
        CancellationToken ct);
}
