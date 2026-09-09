using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public sealed record V180AdminAsOfQuery(DateOnly? AsOf = null, string? Keyword = null,
    bool IncludeInactive = false, int Page = 1, int PageSize = 50);

public sealed record V180OrganizationDto(int OrganizationId, string Code, string Name);
public sealed record V180RoleDto(int RoleId, string Code, string Name);
public sealed record V180TeamMembershipDto(int TeamId, string Code, string Name, bool IsPrimary);
public sealed record V180PersonRowDto(long PersonId, long EmploymentId, int? LegacyUserId,
    string? EmployeeNo, string DisplayName, string? Email, V180OrganizationDto Organization,
    string? EmploymentStatus, IReadOnlyList<V180RoleDto> Roles,
    IReadOnlyList<V180TeamMembershipDto> TeamMemberships, V180TeamMembershipDto? PrimaryTeam,
    string Version);
public sealed record V180LeaderDto(long EmploymentId, string DisplayName,
    long? DelegateEmploymentId, string? DelegateDisplayName);
public sealed record V180TeamAdminDto(int TeamId, string Code, string Name,
    V180OrganizationDto Organization, DateOnly? EffectiveFrom, DateOnly? EffectiveTo,
    bool IsActive, int? CenterId, string? CenterCode, string? CenterName,
    int MemberCount, IReadOnlyList<V180LeaderDto> Leaders, string Version);
public sealed record V180CenterAdminDto(int CenterId, string Code, string Name,
    V180OrganizationDto Organization, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    bool IsActive, string Version);

public interface IV180OrganizationPeopleReader
{
    Task<PagedResult<V180PersonRowDto>> SearchPeopleAsync(CurrentUserDto user, V180AdminAsOfQuery query, CancellationToken ct);
    Task<V180PersonRowDto?> GetPersonAsync(CurrentUserDto user, long employmentId, DateOnly? asOf, CancellationToken ct);
    Task<PagedResult<V180TeamAdminDto>> SearchTeamsAsync(CurrentUserDto user, V180AdminAsOfQuery query, CancellationToken ct);
    Task<PagedResult<V180CenterAdminDto>> SearchCentersAsync(CurrentUserDto user, V180AdminAsOfQuery query, CancellationToken ct);
}

public sealed record V180PersonStableLink(long? PersonId = null, int? LegacyUserId = null,
    int? OrganizationId = null, string? EmployeeNo = null);

public static class V180IdentityRules
{
    public static long ResolvePersonId(V180PersonStableLink link,
        IReadOnlyCollection<Person> people, IReadOnlyCollection<Employment> employments)
    {
        var resolved = new List<long>();
        if (link.PersonId.HasValue)
            resolved.Add(RequireOne(people.Where(x => x.PersonId == link.PersonId).Select(x => x.PersonId), "PersonId"));
        if (link.LegacyUserId.HasValue)
            resolved.Add(RequireOne(people.Where(x => x.LegacyUserId == link.LegacyUserId).Select(x => x.PersonId)
                .Concat(employments.Where(x => x.LegacyUserId == link.LegacyUserId).Select(x => x.PersonId)).Distinct(), "LegacyUserId"));
        if (link.OrganizationId.HasValue && !string.IsNullOrWhiteSpace(link.EmployeeNo))
            resolved.Add(RequireOne(employments.Where(x => x.OrganizationId == link.OrganizationId &&
                x.EmployeeNo == link.EmployeeNo).Select(x => x.PersonId), "OrganizationId + EmployeeNo"));
        if (resolved.Count == 0)
            throw new InvalidOperationException("Person identity requires an explicit stable identifier; name and email are not identity keys.");
        if (resolved.Distinct().Count() != 1)
            throw new InvalidOperationException("Stable person identifiers resolve to different people.");
        return resolved[0];
    }

    private static long RequireOne(IEnumerable<long> candidates, string key)
    {
        var values = candidates.Distinct().Take(2).ToArray();
        return values.Length == 1 ? values[0] : throw new InvalidOperationException(
            values.Length == 0 ? $"No Person matches {key}." : $"Ambiguous Person match for {key}.");
    }
}

public static class V180AsOfRules
{
    public static bool IsEffective(DateOnly from, DateOnly? to, DateOnly asOf) =>
        from <= asOf && (!to.HasValue || asOf <= to.Value);

    public static EmploymentStatusPeriod? EmploymentStatus(IEnumerable<EmploymentStatusPeriod> rows, DateOnly asOf) =>
        RequireZeroOrOne(rows.Where(x => IsEffective(x.EffectiveFrom, x.EffectiveTo, asOf)), "employment status");

    public static IReadOnlyList<EmploymentRoleAssignment> EmploymentRoles(IEnumerable<EmploymentRoleAssignment> rows, DateOnly asOf) =>
        RequireUnique(rows.Where(x => IsEffective(x.EffectiveFrom, x.EffectiveTo, asOf)), x => x.RoleId, "employment role");

    public static IReadOnlyList<TeamMembership> TeamMemberships(IEnumerable<TeamMembership> rows, DateOnly asOf) =>
        RequireUnique(rows.Where(x => IsEffective(x.EffectiveFrom, x.EffectiveTo, asOf)), x => x.TeamId, "team membership");

    public static TeamMembership? PrimaryTeam(IEnumerable<TeamMembership> rows, DateOnly asOf) =>
        RequireZeroOrOne(TeamMemberships(rows, asOf).Where(x => x.IsPrimary), "primary team");

    public static IReadOnlyList<TeamLeaderAssignment> TeamLeaders(IEnumerable<TeamLeaderAssignment> rows, DateOnly asOf) =>
        RequireUnique(rows.Where(x => IsEffective(x.EffectiveFrom, x.EffectiveTo, asOf)), x => x.EmploymentId, "team leader");

    public static TeamLeaderDelegation? DelegatedLeader(IEnumerable<TeamLeaderDelegation> rows, DateOnly asOf) =>
        RequireZeroOrOne(rows.Where(x => IsEffective(x.EffectiveFrom, x.EffectiveTo, asOf)), "leader delegation");

    public static TeamCenterAssignment? TeamCenter(IEnumerable<TeamCenterAssignment> rows, DateOnly asOf) =>
        RequireZeroOrOne(rows.Where(x => IsEffective(x.EffectiveFrom, x.EffectiveTo, asOf)), "team-center assignment");

    private static T? RequireZeroOrOne<T>(IEnumerable<T> source, string label) where T : class
    {
        var rows = source.Take(2).ToArray();
        return rows.Length switch { 0 => null, 1 => rows[0], _ => throw new InvalidOperationException($"Ambiguous current {label} records.") };
    }

    private static IReadOnlyList<T> RequireUnique<T, TKey>(IEnumerable<T> source,
        Func<T, TKey> key, string label) where TKey : notnull
    {
        var rows = source.ToList();
        if (rows.GroupBy(key).Any(x => x.Count() > 1))
            throw new InvalidOperationException($"Ambiguous current {label} records.");
        return rows;
    }
}
