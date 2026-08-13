using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// v1.7 People/Access admin read model.
///
/// This module is intentionally separated from V160FinalRepository so that
/// large people-management features do not continue growing the v1.6
/// transaction/query repository.
/// </summary>
public sealed class V170PeopleAdminRepository(
    AppDbContext db)
    : IV170PeopleAdminRepository
{
    public async Task<PagedResult<V170PeopleRowDto>> QueryAsync(
        CurrentUserDto admin,
        V170PeopleQueryRequest request,
        CancellationToken ct)
    {
        var orgId =
            admin.OrganizationId
            ?? throw new InvalidOperationException(
                "目前管理者缺少 OrganizationId。");

        var today = BusinessTime.Today;

        var q = db.Users
            .AsNoTracking()
            .Where(x => x.OrganizationId == orgId);

        if (!string.IsNullOrWhiteSpace(
                request.Keyword))
        {
            var keyword = request.Keyword;

            q = q.Where(
                x =>
                    x.DisplayName.Contains(keyword)
                    || (x.EmployeeNo != null
                        && x.EmployeeNo.Contains(keyword))
                    || (x.Email != null
                        && x.Email.Contains(keyword))
                    || db.UserIdentityProfiles.Any(
                        p =>
                            p.UserId == x.UserId
                            && p.UserCode.Contains(
                                keyword)));
        }

        if (!string.IsNullOrWhiteSpace(
                request.UserType))
        {
            var userType = request.UserType;

            q = q.Where(
                x => db.UserIdentityProfiles.Any(
                    p =>
                        p.UserId == x.UserId
                        && p.UserType == userType));
        }

        if (!string.IsNullOrWhiteSpace(
                request.EmploymentStatus))
        {
            var status =
                request.EmploymentStatus;

            q = q.Where(
                x => db.UserEmploymentPeriods.Any(
                    e =>
                        e.UserId == x.UserId
                        && e.EmploymentStatus == status
                        && e.EffectiveFrom <= today
                        && (!e.EffectiveTo.HasValue
                            || e.EffectiveTo >= today)));
        }

        if (!string.IsNullOrWhiteSpace(
                request.Role))
        {
            var role = request.Role;

            q = q.Where(
                x => db.UserRoleAssignments.Any(
                    a =>
                        a.UserId == x.UserId
                        && a.EffectiveFrom <= today
                        && (!a.EffectiveTo.HasValue
                            || a.EffectiveTo >= today)
                        && db.Roles.Any(
                            r =>
                                r.RoleId == a.RoleId
                                && r.IsActive
                                && (
                                    r.RoleCode == role
                                    || (
                                        role == "supervisor"
                                        && r.RoleCode
                                           == "government"
                                    )
                                ))));
        }

        if (request.TeamId.HasValue)
        {
            var teamId =
                request.TeamId.Value;

            q = q.Where(
                x => db.UserTeamAssignments.Any(
                    a =>
                        a.UserId == x.UserId
                        && a.TeamId == teamId
                        && a.EffectiveFrom <= today
                        && (!a.EffectiveTo.HasValue
                            || a.EffectiveTo >= today)));
        }

        if (request.IsEnabled.HasValue)
        {
            var enabled =
                request.IsEnabled.Value;

            q = q.Where(
                x => x.IsActive == enabled);
        }

        var total =
            await q.CountAsync(ct);

        q = request.Sort switch
        {
            "name_desc" =>
                q.OrderByDescending(
                    x => x.DisplayName)
                 .ThenByDescending(
                    x => x.UserId),

            "code_asc" =>
                q.OrderBy(x => x.EmployeeNo)
                 .ThenBy(x => x.DisplayName),

            "code_desc" =>
                q.OrderByDescending(
                    x => x.EmployeeNo)
                 .ThenBy(x => x.DisplayName),

            _ =>
                q.OrderBy(x => x.DisplayName)
                 .ThenBy(x => x.UserId)
        };

        var users =
            await q
                .Skip(
                    (request.Page - 1)
                    * request.PageSize)
                .Take(request.PageSize)
                .ToListAsync(ct);

        if (users.Count == 0)
        {
            return new PagedResult<V170PeopleRowDto>(
                [],
                request.Page,
                request.PageSize,
                total);
        }

        var ids =
            users.Select(x => x.UserId)
                .ToList();

        var identities =
            await db.UserIdentityProfiles
                .AsNoTracking()
                .Where(
                    x => ids.Contains(x.UserId))
                .ToDictionaryAsync(
                    x => x.UserId,
                    ct);

        var employmentRows =
            await db.UserEmploymentPeriods
                .AsNoTracking()
                .Where(
                    x =>
                        ids.Contains(x.UserId)
                        && x.EffectiveFrom <= today
                        && (!x.EffectiveTo.HasValue
                            || x.EffectiveTo >= today))
                .OrderByDescending(
                    x => x.EffectiveFrom)
                .ThenByDescending(
                    x => x.UserEmploymentPeriodId)
                .ToListAsync(ct);

        var currentEmployment =
            employmentRows
                .GroupBy(x => x.UserId)
                .ToDictionary(
                    x => x.Key,
                    x => x.First());

        var roleRows =
            await (
                from a in db.UserRoleAssignments
                    .AsNoTracking()
                join r in db.Roles.AsNoTracking()
                    on a.RoleId equals r.RoleId
                where
                    ids.Contains(a.UserId)
                    && a.EffectiveFrom <= today
                    && (!a.EffectiveTo.HasValue
                        || a.EffectiveTo >= today)
                    && r.IsActive
                select new
                {
                    a.UserId,
                    r.RoleCode
                }
            ).ToListAsync(ct);

        var teamRows =
            await (
                from a in db.UserTeamAssignments
                    .AsNoTracking()
                join t in db.Teams.AsNoTracking()
                    on a.TeamId equals t.TeamId
                where
                    ids.Contains(a.UserId)
                    && a.EffectiveFrom <= today
                    && (!a.EffectiveTo.HasValue
                        || a.EffectiveTo >= today)
                    && t.IsActive
                select new
                {
                    a.UserId,
                    a.TeamId,
                    a.IsPrimary,
                    t.TeamName
                }
            ).ToListAsync(ct);

        var result =
            users.Select(
                user =>
                {
                    identities.TryGetValue(
                        user.UserId,
                        out var identity);

                    currentEmployment.TryGetValue(
                        user.UserId,
                        out var employment);

                    var userType =
                        identity?.UserType
                        ?? UserTypes.Internal;

                    var userCode =
                        identity?.UserCode
                        ?? user.EmployeeNo
                        ?? $"USR-{user.UserId:000000}";

                    var roles =
                        roleRows
                            .Where(
                                x =>
                                    x.UserId
                                    == user.UserId)
                            .Select(
                                x => NormalizeRole(
                                    x.RoleCode))
                            .Distinct(
                                StringComparer
                                    .OrdinalIgnoreCase)
                            .OrderBy(x => x)
                            .ToList();

                    var currentTeams =
                        teamRows
                            .Where(
                                x =>
                                    x.UserId
                                    == user.UserId)
                            .ToList();

                    var primary =
                        currentTeams
                            .FirstOrDefault(
                                x => x.IsPrimary)
                        ?? currentTeams
                            .FirstOrDefault();

                    var actualAccess =
                        V170AccessRules
                            .IsSystemAccessAllowed(
                                user.IsActive,
                                employment?
                                    .EmploymentStatus,
                                userType,
                                identity?
                                    .AuthorizationFrom,
                                identity?
                                    .AuthorizationTo,
                                today);

                    return new V170PeopleRowDto(
                        user.UserId,
                        userCode,
                        userType,
                        user.EmployeeNo,
                        user.DisplayName,
                        user.Email,
                        employment?
                            .EmploymentStatus,
                        user.IsActive,
                        actualAccess,
                        roles,
                        primary?.TeamId,
                        primary?.TeamName,
                        identity?
                            .AuthorizationFrom,
                        identity?
                            .AuthorizationTo);
                })
                .ToList();

        return new PagedResult<V170PeopleRowDto>(
            result,
            request.Page,
            request.PageSize,
            total);
    }

    public async Task<V170PersonDetailDto> GetAsync(
        CurrentUserDto admin,
        int userId,
        CancellationToken ct)
    {
        var orgId =
            admin.OrganizationId
            ?? throw new InvalidOperationException(
                "目前管理者缺少 OrganizationId。");

        var user =
            await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.UserId == userId
                        && x.OrganizationId == orgId,
                    ct)
            ?? throw new KeyNotFoundException(
                "找不到人員。");

        var today =
            BusinessTime.Today;

        var identity =
            await db.UserIdentityProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.UserId == userId,
                    ct);

        var organizationName =
            await db.Organizations
                .AsNoTracking()
                .Where(
                    x =>
                        x.OrganizationId
                        == orgId)
                .Select(x => x.OrganizationName)
                .FirstOrDefaultAsync(ct);

        var employments =
            await db.UserEmploymentPeriods
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(
                    x => x.EffectiveFrom)
                .ThenByDescending(
                    x => x.UserEmploymentPeriodId)
                .ToListAsync(ct);

        var roles =
            await (
                from a in db.UserRoleAssignments
                    .AsNoTracking()
                join r in db.Roles.AsNoTracking()
                    on a.RoleId equals r.RoleId
                where a.UserId == userId
                orderby
                    a.EffectiveFrom descending,
                    a.UserRoleAssignmentId descending
                select new
                {
                    Assignment = a,
                    Role = r
                }
            ).ToListAsync(ct);

        var teams =
            await (
                from a in db.UserTeamAssignments
                    .AsNoTracking()
                join t in db.Teams.AsNoTracking()
                    on a.TeamId equals t.TeamId
                where a.UserId == userId
                orderby
                    a.EffectiveFrom descending,
                    a.IsPrimary descending,
                    a.UserTeamAssignmentId descending
                select new
                {
                    Assignment = a,
                    Team = t
                }
            ).ToListAsync(ct);

        var scopes =
            await db.UserDataScopes
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(
                    x => x.EffectiveFrom)
                .ThenByDescending(
                    x => x.UserDataScopeId)
                .ToListAsync(ct);

        var scopeOrgIds =
            scopes
                .Where(
                    x => x.OrganizationId.HasValue)
                .Select(
                    x => x.OrganizationId!.Value)
                .Distinct()
                .ToList();

        var scopeTeamIds =
            scopes
                .Where(x => x.TeamId.HasValue)
                .Select(x => x.TeamId!.Value)
                .Distinct()
                .ToList();

        var organizations =
            await db.Organizations
                .AsNoTracking()
                .Where(
                    x =>
                        scopeOrgIds.Contains(
                            x.OrganizationId))
                .ToDictionaryAsync(
                    x => x.OrganizationId,
                    ct);

        var scopeTeams =
            await db.Teams
                .AsNoTracking()
                .Where(
                    x =>
                        scopeTeamIds.Contains(
                            x.TeamId))
                .ToDictionaryAsync(
                    x => x.TeamId,
                    ct);

        var capabilities =
            await db.UserCapabilities
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderBy(
                    x => x.CapabilityCode)
                .ThenByDescending(
                    x => x.EffectiveFrom)
                .ToListAsync(ct);

        var currentEmployment =
            employments
                .FirstOrDefault(
                    x =>
                        V170AccessRules.IsEffective(
                            x.EffectiveFrom,
                            x.EffectiveTo,
                            today));

        var userType =
            identity?.UserType
            ?? UserTypes.Internal;

        var userCode =
            identity?.UserCode
            ?? user.EmployeeNo
            ?? $"USR-{user.UserId:000000}";

        var actualAccess =
            V170AccessRules
                .IsSystemAccessAllowed(
                    user.IsActive,
                    currentEmployment?
                        .EmploymentStatus,
                    userType,
                    identity?.AuthorizationFrom,
                    identity?.AuthorizationTo,
                    today);

        return new V170PersonDetailDto(
            user.UserId,
            userCode,
            userType,
            identity?.IdentityProvider
                ?? "Demo",
            user.EmployeeNo,
            user.DisplayName,
            user.Email,
            user.OrganizationId,
            organizationName,
            user.IsActive,
            actualAccess,
            currentEmployment?
                .EmploymentStatus,
            identity?
                .ExternalOrganization,
            identity?
                .ExternalTitle,
            identity?
                .AuthorizationFrom,
            identity?
                .AuthorizationTo,

            employments.Select(
                x => new V170EmploymentPeriodDto(
                    x.UserEmploymentPeriodId,
                    x.EmploymentStatus,
                    x.EffectiveFrom,
                    x.EffectiveTo,
                    x.SourceType,
                    x.SourceReference,
                    V170AccessRules.IsEffective(
                        x.EffectiveFrom,
                        x.EffectiveTo,
                        today)))
                .ToList(),

            roles.Select(
                x => new V170RoleAssignmentDto(
                    x.Assignment
                        .UserRoleAssignmentId,
                    x.Role.RoleId,
                    NormalizeRole(
                        x.Role.RoleCode),
                    x.Role.RoleName,
                    x.Assignment.EffectiveFrom,
                    x.Assignment.EffectiveTo,
                    V170AccessRules.IsEffective(
                        x.Assignment
                            .EffectiveFrom,
                        x.Assignment
                            .EffectiveTo,
                        today)))
                .ToList(),

            teams.Select(
                x => new V170TeamAssignmentDto(
                    x.Assignment
                        .UserTeamAssignmentId,
                    x.Team.TeamId,
                    x.Team.TeamCode,
                    x.Team.TeamName,
                    x.Assignment.IsPrimary,
                    x.Assignment.EffectiveFrom,
                    x.Assignment.EffectiveTo,
                    V170AccessRules.IsEffective(
                        x.Assignment
                            .EffectiveFrom,
                        x.Assignment
                            .EffectiveTo,
                        today)))
                .ToList(),

            scopes.Select(
                x =>
                {
                    Organization? scopeOrg = null;
                    Team? scopeTeam = null;

                    if (x.OrganizationId.HasValue)
                    {
                        organizations.TryGetValue(
                            x.OrganizationId.Value,
                            out scopeOrg);
                    }

                    if (x.TeamId.HasValue)
                    {
                        scopeTeams.TryGetValue(
                            x.TeamId.Value,
                            out scopeTeam);
                    }

                    return new V170DataScopeDto(
                        x.UserDataScopeId,
                        x.ScopeType,
                        x.OrganizationId,
                        scopeOrg?.OrganizationName,
                        x.TeamId,
                        scopeTeam?.TeamCode,
                        scopeTeam?.TeamName,
                        x.EffectiveFrom,
                        x.EffectiveTo,
                        V170AccessRules.IsEffective(
                            x.EffectiveFrom,
                            x.EffectiveTo,
                            today));
                })
                .ToList(),

            capabilities.Select(
                x => new V170CapabilityDto(
                    x.UserCapabilityId,
                    x.CapabilityCode,
                    x.IsAllowed,
                    x.EffectiveFrom,
                    x.EffectiveTo,
                    V170AccessRules.IsEffective(
                        x.EffectiveFrom,
                        x.EffectiveTo,
                        today)))
                .ToList());
    }

    private static string NormalizeRole(
        string role)
        => role.Trim()
            .ToLowerInvariant() switch
        {
            "government" => "supervisor",
            var value => value
        };
}
