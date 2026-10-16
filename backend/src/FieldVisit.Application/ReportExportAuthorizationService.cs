namespace FieldVisit.Application;

/// <summary>
/// Export requests use an explicit active role and re-read current HR, role,
/// organization and team state. JWT claims alone are never sufficient for a
/// report download.
/// </summary>
public sealed class ReportExportAuthorizationService(
    ICurrentUserService current,
    IUserRepository users,
    IV170AccessControl access) : IReportExportAuthorizationService
{
    public async Task<CurrentUserDto> AuthorizeAsync(
        IReadOnlyCollection<string> allowedRoles,
        string? format,
        CancellationToken ct)
    {
        var tokenUser = current.GetRequired();
        if (tokenUser.Roles.Count != 1)
            throw new UnauthorizedAccessException("請先選擇單一有效角色，再下載報表。");

        var requestedRole = tokenUser.Roles[0].Trim().ToLowerInvariant();
        if (!allowedRoles.Contains(requestedRole, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("目前角色無權下載此報表。");

        var isActive = await users.GetIsActiveAsync(tokenUser.UserId, ct);
        if (isActive != true)
            throw new UnauthorizedAccessException("目前帳號已停用，無法下載報表。");

        var eligibility = await access.EvaluateLoginAsync(tokenUser.UserId, isActive.Value, ct);
        if (!eligibility.IsAllowed)
            throw new UnauthorizedAccessException("目前人事或帳號狀態不允許下載報表。");

        var liveUser = await users.GetProfileAsync(tokenUser.UserId, ct)
            ?? throw new UnauthorizedAccessException("目前帳號或授權已不存在。");
        if (!liveUser.Roles.Contains(requestedRole, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("目前角色已撤銷，無法下載報表。");
        if (liveUser.OrganizationId != tokenUser.OrganizationId)
            throw new UnauthorizedAccessException("組織授權已變更，請重新登入後再下載報表。");
        if (!tokenUser.TeamIds.OrderBy(x => x).SequenceEqual(liveUser.TeamIds.OrderBy(x => x))
            || !SameDataScopes(tokenUser.DataScopes, liveUser.DataScopes))
            throw new UnauthorizedAccessException("小組或資料範圍已變更，請重新登入後再下載報表。");
        if ((requestedRole is "admin" or "supervisor") && !liveUser.OrganizationId.HasValue)
            throw new UnauthorizedAccessException("帳號缺少有效組織範圍，無法下載報表。");

        if (requestedRole == "supervisor" && !string.IsNullOrWhiteSpace(format))
            await access.EnsureExportAllowedAsync(liveUser, format, ct);

        // A caller may hold several roles in the database. Return only the
        // explicitly selected, live-validated role so downstream query scope
        // cannot choose a broader role by precedence.
        return liveUser with { Roles = [requestedRole] };
    }

    private static bool SameDataScopes(IReadOnlyList<DataScopeDto>? left,
        IReadOnlyList<DataScopeDto>? right)
    {
        static string Key(DataScopeDto item) => $"{item.ScopeType}:{item.OrganizationId}:{item.TeamId}";
        return (left ?? []).Select(Key).OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual((right ?? []).Select(Key).OrderBy(x => x, StringComparer.Ordinal));
    }
}
