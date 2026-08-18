namespace FieldVisit.Application;

public sealed record V170PeopleQueryRequest(
    string? Keyword = null,
    string? UserType = null,
    string? EmploymentStatus = null,
    string? Role = null,
    int? TeamId = null,
    bool? IsEnabled = null,
    int Page = 1,
    int PageSize = 50,
    string Sort = "name_asc");

public sealed record V170CurrentTeamAssignmentDto(
    int TeamId,
    string TeamCode,
    string TeamName,
    bool IsPrimary);

public sealed record V170PeopleRowDto(
    int UserId,
    string UserCode,
    string UserType,
    string? EmployeeNo,
    string DisplayName,
    string? Email,
    string? EmploymentStatus,
    bool AdminEnabled,
    bool ActualAccess,
    IReadOnlyList<string> Roles,
    IReadOnlyList<V170CurrentTeamAssignmentDto> TeamAssignments,
    int? PrimaryTeamId,
    string? PrimaryTeamName,
    DateOnly? AuthorizationFrom,
    DateOnly? AuthorizationTo);

public sealed record V170EmploymentPeriodDto(
    long UserEmploymentPeriodId,
    string EmploymentStatus,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string SourceType,
    string? SourceReference,
    bool IsCurrent);

public sealed record V170RoleAssignmentDto(
    long UserRoleAssignmentId,
    int RoleId,
    string RoleCode,
    string RoleName,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsCurrent);

public sealed record V170TeamAssignmentDto(
    long UserTeamAssignmentId,
    int TeamId,
    string TeamCode,
    string TeamName,
    bool IsPrimary,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsCurrent);

public sealed record V170DataScopeDto(
    long UserDataScopeId,
    string ScopeType,
    int? OrganizationId,
    string? OrganizationName,
    int? TeamId,
    string? TeamCode,
    string? TeamName,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsCurrent);

public sealed record V170CapabilityDto(
    long UserCapabilityId,
    string CapabilityCode,
    bool IsAllowed,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsCurrent);

public sealed record V170PersonDetailDto(
    int UserId,
    string UserCode,
    string UserType,
    string IdentityProvider,
    string? EmployeeNo,
    string DisplayName,
    string? Email,
    int? OrganizationId,
    string? OrganizationName,
    bool AdminEnabled,
    bool ActualAccess,
    string? EmploymentStatus,
    string? ExternalOrganization,
    string? ExternalTitle,
    DateOnly? AuthorizationFrom,
    DateOnly? AuthorizationTo,
    IReadOnlyList<V170EmploymentPeriodDto> EmploymentPeriods,
    IReadOnlyList<V170RoleAssignmentDto> RoleAssignments,
    IReadOnlyList<V170TeamAssignmentDto> TeamAssignments,
    IReadOnlyList<V170DataScopeDto> DataScopes,
    IReadOnlyList<V170CapabilityDto> Capabilities);

public static class V170PeopleQueryRules
{
    private static readonly HashSet<string> AllowedUserTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Internal",
            "External"
        };

    private static readonly HashSet<string> AllowedEmploymentStatuses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Active",
            "Leave",
            "Terminated",
            "PreHire"
        };

    private static readonly HashSet<string> AllowedRoles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "visitor",
            "leader",
            "admin",
            "supervisor"
        };

    private static readonly HashSet<string> AllowedSorts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "name_asc",
            "name_desc",
            "code_asc",
            "code_desc"
        };

    public static V170PeopleQueryRequest Normalize(
        V170PeopleQueryRequest request)
    {
        var page = Math.Max(1, request.Page);

        var pageSize =
            request.PageSize is 20 or 50 or 100
                ? request.PageSize
                : 50;

        var keyword =
            string.IsNullOrWhiteSpace(request.Keyword)
                ? null
                : request.Keyword.Trim();

        var userType =
            string.IsNullOrWhiteSpace(request.UserType)
                ? null
                : request.UserType.Trim();

        if (userType is not null
            && !AllowedUserTypes.Contains(userType))
        {
            throw new InvalidOperationException(
                "UserType 只允許 Internal 或 External。");
        }

        var employment =
            string.IsNullOrWhiteSpace(
                request.EmploymentStatus)
                ? null
                : request.EmploymentStatus.Trim();

        if (employment is not null
            && !AllowedEmploymentStatuses.Contains(
                employment))
        {
            throw new InvalidOperationException(
                "EmploymentStatus 不正確。");
        }

        var role =
            string.IsNullOrWhiteSpace(request.Role)
                ? null
                : request.Role.Trim()
                    .ToLowerInvariant();

        if (role == "government")
            role = "supervisor";

        if (role is not null
            && !AllowedRoles.Contains(role))
        {
            throw new InvalidOperationException(
                "Role 不正確。");
        }

        var sort =
            string.IsNullOrWhiteSpace(request.Sort)
                ? "name_asc"
                : request.Sort.Trim()
                    .ToLowerInvariant();

        if (!AllowedSorts.Contains(sort))
            sort = "name_asc";

        return request with
        {
            Keyword = keyword,
            UserType = userType,
            EmploymentStatus = employment,
            Role = role,
            Page = page,
            PageSize = pageSize,
            Sort = sort
        };
    }
}

public interface IV170PeopleAdminRepository
{
    Task<PagedResult<V170PeopleRowDto>> QueryAsync(
        CurrentUserDto admin,
        V170PeopleQueryRequest request,
        CancellationToken ct);

    Task<V170PersonDetailDto> GetAsync(
        CurrentUserDto admin,
        int userId,
        CancellationToken ct);
}
