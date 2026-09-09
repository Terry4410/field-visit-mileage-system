using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180OrganizationPeopleReader(AppDbContext db) : IV180OrganizationPeopleReader
{
    public async Task<PagedResult<V180PersonRowDto>> SearchPeopleAsync(CurrentUserDto user,
        V180AdminAsOfQuery input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(user);
        var q = Normalize(input);
        var asOf = q.AsOf!.Value;
        var source = db.Employments.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (!string.IsNullOrEmpty(q.Keyword))
        {
            var keyword = q.Keyword;
            source = source.Where(x => (x.EmployeeNo != null && x.EmployeeNo.Contains(keyword)) ||
                (x.Email != null && x.Email.Contains(keyword)) ||
                db.Persons.Any(p => p.PersonId == x.PersonId && p.DisplayName.Contains(keyword)));
        }
        if (!q.IncludeInactive)
            source = source.Where(x => db.EmploymentStatusPeriods.Any(s => s.EmploymentId == x.EmploymentId &&
                s.EmploymentStatus == EmploymentStatuses.Active && s.EffectiveFrom <= asOf &&
                (!s.EffectiveTo.HasValue || asOf <= s.EffectiveTo.Value)));
        if (q.TeamId.HasValue)
        {
            var teamId = q.TeamId.Value;
            source = source.Where(x => db.TeamMemberships.Any(m => m.EmploymentId == x.EmploymentId &&
                m.TeamId == teamId && m.EffectiveFrom <= asOf &&
                (!m.EffectiveTo.HasValue || asOf <= m.EffectiveTo.Value)));
        }
        if (!string.IsNullOrEmpty(q.Role))
        {
            var roleCodes = q.Role == "supervisor"
                ? new[] { "supervisor", "government" }
                : new[] { q.Role };
            source = source.Where(x => db.EmploymentRoleAssignments.Any(a =>
                a.EmploymentId == x.EmploymentId && a.EffectiveFrom <= asOf &&
                (!a.EffectiveTo.HasValue || asOf <= a.EffectiveTo.Value) &&
                db.Roles.Any(r => r.RoleId == a.RoleId && r.IsActive &&
                    roleCodes.Contains(r.RoleCode.ToLower()))));
        }
        if (q.InternalOnly)
        {
            var externalRoleCodes = new[] { "supervisor", "government" };
            source = source.Where(x => !db.EmploymentRoleAssignments.Any(a =>
                a.EmploymentId == x.EmploymentId && a.EffectiveFrom <= asOf &&
                (!a.EffectiveTo.HasValue || asOf <= a.EffectiveTo.Value) &&
                db.Roles.Any(r => r.RoleId == a.RoleId && r.IsActive &&
                    externalRoleCodes.Contains(r.RoleCode.ToLower()))));
        }
        if (q.AdminEnabled.HasValue)
        {
            var adminEnabled = q.AdminEnabled.Value;
            source = source.Where(x =>
                (x.LegacyUserId.HasValue && db.Users.Any(u =>
                    u.UserId == x.LegacyUserId.Value && u.IsActive == adminEnabled)) ||
                (!x.LegacyUserId.HasValue && db.Persons.Any(p =>
                    p.PersonId == x.PersonId && p.LegacyUserId.HasValue && db.Users.Any(u =>
                        u.UserId == p.LegacyUserId.Value && u.IsActive == adminEnabled))));
        }

        var total = await source.CountAsync(ct);
        var rows = await source.OrderBy(x => x.EmployeeNo).ThenBy(x => x.EmploymentId)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        return new(await MapPeopleAsync(rows, asOf, ct), q.Page, q.PageSize, total);
    }

    public async Task<V180PersonRowDto?> GetPersonAsync(CurrentUserDto user, long employmentId,
        DateOnly? asOf, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(user);
        var employment = await db.Employments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.EmploymentId == employmentId && x.OrganizationId == organizationId, ct);
        if (employment is null) return null;
        return (await MapPeopleAsync([employment], asOf ?? BusinessTime.Today, ct)).Single();
    }

    public async Task<PagedResult<V180TeamAdminDto>> SearchTeamsAsync(CurrentUserDto user,
        V180AdminAsOfQuery input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(user);
        var q = Normalize(input);
        var asOf = q.AsOf!.Value;
        var source = db.Teams.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (!string.IsNullOrEmpty(q.Keyword))
        {
            var keyword = q.Keyword;
            source = source.Where(x => x.TeamCode.Contains(keyword) || x.TeamName.Contains(keyword));
        }
        if (!q.IncludeInactive)
            source = source.Where(x => x.IsActive && (!x.EffectiveFrom.HasValue || x.EffectiveFrom <= asOf) &&
                (!x.EffectiveTo.HasValue || asOf <= x.EffectiveTo.Value));

        var total = await source.CountAsync(ct);
        var teams = await source.OrderBy(x => x.TeamCode).ThenBy(x => x.TeamId)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        var teamIds = teams.Select(x => x.TeamId).ToArray();
        var organization = await OrganizationAsync(organizationId, ct);
        var centerAssignments = await db.TeamCenterAssignments.AsNoTracking().Where(x => teamIds.Contains(x.TeamId)).ToListAsync(ct);
        var centerIds = centerAssignments.Select(x => x.CenterId).Distinct().ToArray();
        var centers = await db.Centers.AsNoTracking().Where(x => centerIds.Contains(x.CenterId)).ToDictionaryAsync(x => x.CenterId, ct);
        var memberships = await db.TeamMemberships.AsNoTracking().Where(x => teamIds.Contains(x.TeamId)).ToListAsync(ct);
        var leaders = await db.TeamLeaderAssignments.AsNoTracking().Where(x => teamIds.Contains(x.TeamId)).ToListAsync(ct);
        var leaderIds = leaders.Select(x => x.TeamLeaderAssignmentId).ToArray();
        var delegations = await db.TeamLeaderDelegations.AsNoTracking().Where(x => leaderIds.Contains(x.TeamLeaderAssignmentId)).ToListAsync(ct);
        var employmentIds = leaders.Select(x => x.EmploymentId).Concat(delegations.Select(x => x.DelegateEmploymentId)).Distinct().ToArray();
        var employments = await db.Employments.AsNoTracking().Where(x => employmentIds.Contains(x.EmploymentId)).ToListAsync(ct);
        var personIds = employments.Select(x => x.PersonId).Distinct().ToArray();
        var people = await db.Persons.AsNoTracking().Where(x => personIds.Contains(x.PersonId)).ToDictionaryAsync(x => x.PersonId, ct);
        var employmentById = employments.ToDictionary(x => x.EmploymentId);

        var result = new List<V180TeamAdminDto>();
        foreach (var team in teams)
        {
            var centerAssignment = V180AsOfRules.TeamCenter(centerAssignments.Where(x => x.TeamId == team.TeamId), asOf);
            Center? center = centerAssignment is null ? null : centers[centerAssignment.CenterId];
            var activeMemberships = memberships.Where(x => x.TeamId == team.TeamId &&
                V180AsOfRules.IsEffective(x.EffectiveFrom, x.EffectiveTo, asOf)).ToList();
            if (activeMemberships.GroupBy(x => x.EmploymentId).Any(x => x.Count() > 1))
                throw new InvalidOperationException("Ambiguous current team membership records.");
            var leaderDtos = new List<V180LeaderDto>();
            foreach (var leader in V180AsOfRules.TeamLeaders(leaders.Where(x => x.TeamId == team.TeamId), asOf))
            {
                var employment = employmentById[leader.EmploymentId];
                var delegation = V180AsOfRules.DelegatedLeader(
                    delegations.Where(x => x.TeamLeaderAssignmentId == leader.TeamLeaderAssignmentId), asOf);
                Employment? delegateEmployment = delegation is null ? null : employmentById[delegation.DelegateEmploymentId];
                leaderDtos.Add(new(leader.EmploymentId, people[employment.PersonId].DisplayName,
                    delegateEmployment?.EmploymentId,
                    delegateEmployment is null ? null : people[delegateEmployment.PersonId].DisplayName));
            }
            result.Add(new(team.TeamId, team.TeamCode, team.TeamName, organization,
                team.EffectiveFrom, team.EffectiveTo, team.IsActive,
                center?.CenterId, center?.CenterCode, center?.CenterName,
                activeMemberships.Select(x => x.EmploymentId).Distinct().Count(), leaderDtos,
                Convert.ToBase64String(team.RowVersion)));
        }
        return new(result, q.Page, q.PageSize, total);
    }

    public async Task<PagedResult<V180CenterAdminDto>> SearchCentersAsync(CurrentUserDto user,
        V180AdminAsOfQuery input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(user);
        var q = Normalize(input);
        var asOf = q.AsOf!.Value;
        var source = db.Centers.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (!string.IsNullOrEmpty(q.Keyword))
        {
            var keyword = q.Keyword;
            source = source.Where(x => x.CenterCode.Contains(keyword) || x.CenterName.Contains(keyword));
        }
        if (!q.IncludeInactive)
            source = source.Where(x => x.IsActive && x.EffectiveFrom <= asOf &&
                (!x.EffectiveTo.HasValue || asOf <= x.EffectiveTo.Value));
        var total = await source.CountAsync(ct);
        var rows = await source.OrderBy(x => x.CenterCode).ThenBy(x => x.CenterId)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        var organization = await OrganizationAsync(organizationId, ct);
        return new(rows.Select(x => new V180CenterAdminDto(x.CenterId, x.CenterCode, x.CenterName,
            organization, x.EffectiveFrom, x.EffectiveTo, x.IsActive, Convert.ToBase64String(x.RowVersion))).ToList(),
            q.Page, q.PageSize, total);
    }

    private async Task<IReadOnlyList<V180PersonRowDto>> MapPeopleAsync(IReadOnlyList<Employment> employments,
        DateOnly asOf, CancellationToken ct)
    {
        var employmentIds = employments.Select(x => x.EmploymentId).ToArray();
        var personIds = employments.Select(x => x.PersonId).ToArray();
        var organizationIds = employments.Select(x => x.OrganizationId).Distinct().ToArray();
        var people = await db.Persons.AsNoTracking().Where(x => personIds.Contains(x.PersonId)).ToDictionaryAsync(x => x.PersonId, ct);
        var organizations = await db.Organizations.AsNoTracking().Where(x => organizationIds.Contains(x.OrganizationId)).ToDictionaryAsync(x => x.OrganizationId, ct);
        var statuses = await db.EmploymentStatusPeriods.AsNoTracking().Where(x => employmentIds.Contains(x.EmploymentId)).ToListAsync(ct);
        var assignments = await db.EmploymentRoleAssignments.AsNoTracking().Where(x => employmentIds.Contains(x.EmploymentId)).ToListAsync(ct);
        var roleIds = assignments.Select(x => x.RoleId).Distinct().ToArray();
        var roles = await db.Roles.AsNoTracking().Where(x => roleIds.Contains(x.RoleId)).ToDictionaryAsync(x => x.RoleId, ct);
        var memberships = await db.TeamMemberships.AsNoTracking().Where(x => employmentIds.Contains(x.EmploymentId)).ToListAsync(ct);
        var teamIds = memberships.Select(x => x.TeamId).Distinct().ToArray();
        var teams = await db.Teams.AsNoTracking().Where(x => teamIds.Contains(x.TeamId)).ToDictionaryAsync(x => x.TeamId, ct);
        var legacyUserIds = employments
            .Select(x => ResolveLegacyUserId(x, people[x.PersonId]))
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(x => legacyUserIds.Contains(x.UserId))
            .ToDictionaryAsync(x => x.UserId, ct);

        return employments.Select(employment =>
        {
            var person = people[employment.PersonId];
            var organization = organizations[employment.OrganizationId];
            var legacyUserId = ResolveLegacyUserId(employment, person);
            bool? adminEnabled = null;
            if (legacyUserId.HasValue)
            {
                if (!users.TryGetValue(legacyUserId.Value, out var loginUser))
                    throw new InvalidOperationException("LegacyUserId 對應不到 User 登入帳號；不得推測 AdminEnabled。");
                adminEnabled = loginUser.IsActive;
            }
            var currentRoles = V180AsOfRules.EmploymentRoles(assignments.Where(x => x.EmploymentId == employment.EmploymentId), asOf);
            var currentMemberships = V180AsOfRules.TeamMemberships(memberships.Where(x => x.EmploymentId == employment.EmploymentId), asOf);
            var primary = V180AsOfRules.PrimaryTeam(currentMemberships, asOf);
            var membershipDtos = currentMemberships.Select(x => new V180TeamMembershipDto(x.TeamId,
                teams[x.TeamId].TeamCode, teams[x.TeamId].TeamName, x.IsPrimary)).ToList();
            return new V180PersonRowDto(person.PersonId, employment.EmploymentId,
                legacyUserId, employment.EmployeeNo, person.DisplayName,
                employment.Email, ToDto(organization),
                V180AsOfRules.EmploymentStatus(statuses.Where(x => x.EmploymentId == employment.EmploymentId), asOf)?.EmploymentStatus,
                currentRoles.Select(x => new V180RoleDto(x.RoleId, roles[x.RoleId].RoleCode, roles[x.RoleId].RoleName)).ToList(),
                membershipDtos, primary is null ? null : membershipDtos.Single(x => x.TeamId == primary.TeamId),
                adminEnabled, Convert.ToBase64String(employment.RowVersion));
        }).ToList();
    }

    private static int? ResolveLegacyUserId(Employment employment, Person person)
    {
        if (employment.LegacyUserId.HasValue && person.LegacyUserId.HasValue &&
            employment.LegacyUserId.Value != person.LegacyUserId.Value)
            throw new InvalidOperationException("IDENTITY_BRIDGE_MISMATCH：Employment 與 Person 的 LegacyUserId 不一致。");
        return employment.LegacyUserId ?? person.LegacyUserId;
    }

    private async Task<V180OrganizationDto> OrganizationAsync(int id, CancellationToken ct) =>
        ToDto(await db.Organizations.AsNoTracking().SingleAsync(x => x.OrganizationId == id, ct));

    private static V180OrganizationDto ToDto(Organization x) => new(x.OrganizationId, x.OrganizationCode, x.OrganizationName);

    private static V180AdminAsOfQuery Normalize(V180AdminAsOfQuery input)
    {
        var keyword = string.IsNullOrWhiteSpace(input.Keyword) ? null : input.Keyword.Trim();
        if (keyword?.Length > 200) throw new InvalidOperationException("搜尋關鍵字不可超過 200 字。");
        var role = string.IsNullOrWhiteSpace(input.Role) ? null : input.Role.Trim().ToLowerInvariant();
        role = role == "government" ? "supervisor" : role;
        if (role is not null && role is not ("visitor" or "leader" or "admin" or "supervisor"))
            throw new InvalidOperationException("Role 篩選值不正確。");
        return input with { AsOf = input.AsOf ?? BusinessTime.Today, Keyword = keyword, Role = role,
            Page = Math.Max(1, input.Page), PageSize = Math.Clamp(input.PageSize, 1, 100) };
    }

    private static int RequireAdminOrganization(CurrentUserDto user)
    {
        if (!user.Roles.Any(x => x.Equals("admin", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("只有管理者可以查詢 v1.8 組織與人員主檔。");
        return user.OrganizationId ?? throw new InvalidOperationException("目前帳號缺少 OrganizationId。");
    }
}
