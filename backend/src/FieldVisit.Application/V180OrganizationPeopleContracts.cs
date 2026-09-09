using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public sealed record V180AdminAsOfQuery(DateOnly? AsOf = null, string? Keyword = null,
    bool IncludeInactive = false, int Page = 1, int PageSize = 50,
    string? Role = null, bool? AdminEnabled = null, int? TeamId = null, bool InternalOnly = false);

public sealed record V180OrganizationDto(int OrganizationId, string Code, string Name);
public sealed record V180RoleDto(int RoleId, string Code, string Name);
public sealed record V180TeamMembershipDto(int TeamId, string Code, string Name, bool IsPrimary);
public sealed record V180PersonRowDto(long PersonId, long EmploymentId, int? LegacyUserId,
    string? EmployeeNo, string DisplayName, string? Email, V180OrganizationDto Organization,
    string? EmploymentStatus, IReadOnlyList<V180RoleDto> Roles,
    IReadOnlyList<V180TeamMembershipDto> TeamMemberships, V180TeamMembershipDto? PrimaryTeam,
    bool? AdminEnabled, string Version);
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

public sealed record V180TeamMembershipWriteDto(int TeamId, bool IsPrimary);
public sealed record V180UpdatePeopleAccessRequest(
    IReadOnlyList<string> Roles,
    IReadOnlyList<V180TeamMembershipWriteDto> TeamMemberships,
    bool AdminEnabled,
    DateOnly ChangeEffectiveFrom,
    bool ConfirmRetroactive,
    string Version);
public sealed record V180PeopleAccessWriteResult(long PersonId, long EmploymentId,
    int LegacyUserId, string Version);
public sealed record V180ExternalIdentityWriteResult(long PersonId, long EmploymentId);

public interface IV180OrganizationPeopleWriter
{
    Task<long> ResolveEmploymentIdAsync(int legacyUserId, CancellationToken ct);
    Task<string> GetVersionAsync(long employmentId, CancellationToken ct);
    Task<V180PeopleAccessWriteResult> UpdateAccessAsync(CurrentUserDto admin, long employmentId,
        V180UpdatePeopleAccessRequest request, CancellationToken ct);
    Task<V180PeopleAccessWriteResult> UpdateAccessFromLegacyAsync(CurrentUserDto admin, long employmentId,
        V180UpdatePeopleAccessRequest request, V170IdentityBindingInput identity, CancellationToken ct);
    Task<V180ExternalIdentityWriteResult> CreateExternalIdentityAsync(CurrentUserDto admin, User user,
        SaveExternalSupervisorRequest request, int supervisorRoleId, DateTime now, CancellationToken ct);
    Task<V180ExternalIdentityWriteResult> UpdateExternalIdentityAsync(CurrentUserDto admin, User user, UserIdentityProfile profile,
        UpdateExternalSupervisorRequest request, int supervisorRoleId, DateTime now, CancellationToken ct);
    Task ProjectExternalCompatibilityAsync(CurrentUserDto admin, User user, long employmentId,
        DateTime now, CancellationToken ct);
}

public sealed record V180CreateTeamRequest(string Code, string Name, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null, string? Notes = null, bool IsActive = true);
public sealed record V180UpdateTeamRequest(string Code, string Name, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, string? Notes, bool IsActive, string Version);
public sealed record V180DeactivateRequest(DateOnly EffectiveTo, string Version);
public sealed record V180TeamWriteResult(int TeamId, int OrganizationId, string Code, string Name,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, string? Notes, string Version);

public sealed record V180CreateCenterRequest(string Code, string Name, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null, string? Notes = null, bool IsActive = true);
public sealed record V180UpdateCenterRequest(string Code, string Name, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, string? Notes, bool IsActive, string Version);
public sealed record V180CenterWriteResult(int CenterId, int OrganizationId, string Code, string Name,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, string? Notes, string Version);

public sealed record V180CreateTeamCenterAssignmentRequest(int TeamId, int CenterId,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo = null, string? ChangeReason = null);
public sealed record V180UpdateTeamCenterAssignmentRequest(int CenterId, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, string? ChangeReason, string Version);
public sealed record V180EndTeamCenterAssignmentRequest(DateOnly EffectiveTo, string Version);
public sealed record V180TeamCenterAssignmentWriteResult(long TeamCenterAssignmentId, int TeamId,
    int CenterId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? ChangeReason, string Version);

public interface IV180TeamCenterLifecycleWriter
{
    Task<string> GetTeamVersionAsync(int teamId, int organizationId, CancellationToken ct);
    Task<V180TeamWriteResult> CreateTeamAsync(CurrentUserDto admin, V180CreateTeamRequest request, CancellationToken ct);
    Task<V180TeamWriteResult> UpdateTeamAsync(CurrentUserDto admin, int teamId, V180UpdateTeamRequest request, CancellationToken ct);
    Task<V180TeamWriteResult> DeactivateTeamAsync(CurrentUserDto admin, int teamId, V180DeactivateRequest request, CancellationToken ct);
    Task<V180CenterWriteResult> CreateCenterAsync(CurrentUserDto admin, V180CreateCenterRequest request, CancellationToken ct);
    Task<V180CenterWriteResult> UpdateCenterAsync(CurrentUserDto admin, int centerId, V180UpdateCenterRequest request, CancellationToken ct);
    Task<V180CenterWriteResult> DeactivateCenterAsync(CurrentUserDto admin, int centerId, V180DeactivateRequest request, CancellationToken ct);
    Task<V180TeamCenterAssignmentWriteResult> CreateTeamCenterAssignmentAsync(CurrentUserDto admin,
        V180CreateTeamCenterAssignmentRequest request, CancellationToken ct);
    Task<V180TeamCenterAssignmentWriteResult> UpdateTeamCenterAssignmentAsync(CurrentUserDto admin, long assignmentId,
        V180UpdateTeamCenterAssignmentRequest request, CancellationToken ct);
    Task<V180TeamCenterAssignmentWriteResult> EndTeamCenterAssignmentAsync(CurrentUserDto admin, long assignmentId,
        V180EndTeamCenterAssignmentRequest request, CancellationToken ct);
}

public static class V180TeamCenterLifecycleRules
{
    public static string NormalizeCode(string? value, string label)
    {
        var normalized = (value ?? "").Trim().ToUpperInvariant();
        if (normalized.Length == 0) throw new InvalidOperationException($"{label}代碼必填。");
        if (normalized.Length > 50) throw new InvalidOperationException($"{label}代碼不可超過 50 個字元。");
        return normalized;
    }

    public static string NormalizeName(string? value, string label)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length == 0) throw new InvalidOperationException($"{label}名稱必填。");
        if (normalized.Length > 200) throw new InvalidOperationException($"{label}名稱不可超過 200 個字元。");
        return normalized;
    }

    public static string? NormalizeNotes(string? value, int maxLength, string label)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength) throw new InvalidOperationException($"{label}不可超過 {maxLength} 個字元。");
        return normalized;
    }

    public static void ValidatePeriod(DateOnly from, DateOnly? to, string label)
    {
        if (to.HasValue && to.Value < from)
            throw new InvalidOperationException($"{label}結束日不得早於開始日。");
    }

    public static bool Overlaps(DateOnly leftFrom, DateOnly? leftTo, DateOnly rightFrom, DateOnly? rightTo) =>
        leftFrom <= (rightTo ?? DateOnly.MaxValue) && rightFrom <= (leftTo ?? DateOnly.MaxValue);

    public static bool IsWithin(DateOnly from, DateOnly? to, DateOnly? ownerFrom, DateOnly? ownerTo) =>
        (!ownerFrom.HasValue || ownerFrom.Value <= from) &&
        (!ownerTo.HasValue || (to.HasValue && to.Value <= ownerTo.Value));

    public static byte[] DecodeVersion(string version) => V180PeopleAccessRules.DecodeVersion(version);
}

public static class V180PeopleAccessRules
{
    private static readonly HashSet<string> InternalRoles =
        new(["visitor", "leader", "admin"], StringComparer.OrdinalIgnoreCase);

    public static V180UpdatePeopleAccessRequest Normalize(
        V180UpdatePeopleAccessRequest request, DateOnly today)
    {
        var roles = (request.Roles ?? []).Select(V170InternalUserAccessRules.NormalizeRole)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        if (roles.Count == 0 || roles.Any(x => !InternalRoles.Contains(x)))
            throw new InvalidOperationException("Internal Employment 只允許 visitor、leader、admin，且至少需要一個角色。");
        var teams = (request.TeamMemberships ?? []).ToList();
        if (teams.Any(x => x.TeamId <= 0) || teams.Select(x => x.TeamId).Distinct().Count() != teams.Count)
            throw new InvalidOperationException("TeamMemberships 包含無效或重複 TeamId。");
        if (teams.Count > 0 && teams.Count(x => x.IsPrimary) != 1)
            throw new InvalidOperationException("有 TeamMembership 時必須且只能指定一個 Primary Team。");
        if (teams.Count == 0 && roles.Any(x => x is "visitor" or "leader"))
            throw new InvalidOperationException("Visitor 或 Leader 至少需要一個 TeamMembership。");
        if (request.ChangeEffectiveFrom < today && !request.ConfirmRetroactive)
            throw new InvalidOperationException("異動生效日早於今天，請二次確認回溯異動。");
        DecodeVersion(request.Version);
        return request with { Roles = roles, TeamMemberships = teams };
    }

    public static byte[] DecodeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("Version 必填。");
        try
        {
            var bytes = Convert.FromBase64String(version);
            if (bytes.Length != 8) throw new FormatException();
            return bytes;
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Version 必須是有效的 SQL rowversion Base64 token。");
        }
    }
}

public static class V180IdentityBridgeRules
{
    public static long ResolveEmploymentId(
        IReadOnlyCollection<long> profileEmploymentIds,
        IReadOnlyCollection<long> legacyEmploymentIds)
    {
        var profiles = profileEmploymentIds.Distinct().Take(2).ToArray();
        var legacy = legacyEmploymentIds.Distinct().Take(2).ToArray();
        if (profiles.Length > 1 || legacy.Length > 1)
            throw new InvalidOperationException("AMBIGUOUS_IDENTITY_BRIDGE：UserId 對應多個 Employment。");
        if (profiles.Length == 1 && legacy.Length == 1 && profiles[0] != legacy[0])
            throw new InvalidOperationException("IDENTITY_BRIDGE_MISMATCH：UserIdentityProfile 與 Employment.LegacyUserId 不一致。");
        if (profiles.Length == 1) return profiles[0];
        if (legacy.Length == 1) return legacy[0];
        throw new InvalidOperationException("找不到 UserId 對應的 Employment；不得以姓名或 Email 推測。");
    }
}

public static class V180LeaderAssignmentRules
{
    public static IReadOnlyList<int> DeriveTeamIds(
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<V180TeamMembershipWriteDto> memberships) =>
        roles.Contains("leader", StringComparer.OrdinalIgnoreCase)
            ? memberships.Select(x => x.TeamId).Distinct().OrderBy(x => x).ToList()
            : [];
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
