using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180TripContextReader(AppDbContext db) : IV180TripContextReader
{
    public async Task<V180TripContextDto> ResolveAsync(
        CurrentUserDto user, DateOnly visitDate, int? teamId, CancellationToken ct)
    {
        if (!user.Roles.Any(x => x.Equals("visitor", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("只有外訪員可以取得行程派駐點 context。");

        var organizationId = user.OrganizationId
            ?? throw new InvalidOperationException("TRIP_CONTEXT_ORGANIZATION_MISSING：目前帳號缺少 OrganizationId。");
        var profile = await db.UserIdentityProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == user.UserId, ct);
        if (profile is null || !profile.EmploymentId.HasValue)
            throw new InvalidOperationException("TRIP_CONTEXT_EMPLOYMENT_MISSING：目前 UserId 沒有明確的 EmploymentId 綁定。");
        if (!profile.UserType.Equals(UserTypes.Internal, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("External Supervisor 不適用 v1.8 Trip Context。");

        var employment = await db.Employments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.EmploymentId == profile.EmploymentId.Value, ct)
            ?? throw new InvalidOperationException("TRIP_CONTEXT_EMPLOYMENT_MISSING：Identity 綁定的 Employment 不存在。");
        if (employment.OrganizationId != organizationId)
            throw new InvalidOperationException("TRIP_CONTEXT_ORGANIZATION_MISMATCH：Employment 不屬於目前使用者 Organization。");

        var statuses = await db.EmploymentStatusPeriods.AsNoTracking()
            .Where(x => x.EmploymentId == employment.EmploymentId && x.EffectiveFrom <= visitDate &&
                (!x.EffectiveTo.HasValue || visitDate <= x.EffectiveTo.Value))
            .ToListAsync(ct);
        if (statuses.Count > 1)
            throw new InvalidOperationException("AMBIGUOUS_EMPLOYMENT_STATUS：VisitDate 有多筆有效 EmploymentStatusPeriod。");
        if (statuses.Count == 0 || !statuses[0].EmploymentStatus.Equals(EmploymentStatuses.Active, StringComparison.Ordinal))
            return Empty(employment.EmploymentId, visitDate, "EMPLOYMENT_NOT_ACTIVE", "外訪日期當日不是有效在職狀態。");

        var memberships = await db.TeamMemberships.AsNoTracking()
            .Where(x => x.EmploymentId == employment.EmploymentId && x.EffectiveFrom <= visitDate &&
                (!x.EffectiveTo.HasValue || visitDate <= x.EffectiveTo.Value))
            .ToListAsync(ct);
        if (memberships.GroupBy(x => x.TeamId).Any(x => x.Count() > 1))
            throw new InvalidOperationException("AMBIGUOUS_TEAM_MEMBERSHIP：VisitDate 的 TeamMembership 重疊。");

        var teamIds = memberships.Select(x => x.TeamId).Distinct().ToArray();
        var teamsById = await db.Teams.AsNoTracking().Where(x => teamIds.Contains(x.TeamId))
            .ToDictionaryAsync(x => x.TeamId, ct);
        foreach (var membership in memberships)
        {
            if (!teamsById.TryGetValue(membership.TeamId, out var team) ||
                team.OrganizationId != organizationId || !team.IsActive ||
                (team.EffectiveFrom.HasValue && visitDate < team.EffectiveFrom.Value) ||
                (team.EffectiveTo.HasValue && team.EffectiveTo.Value < visitDate))
                throw new InvalidOperationException("TRIP_CONTEXT_TEAM_INVALID：TeamMembership 對應不到同 Organization 的有效 Team。");
        }

        var teamDtos = memberships
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => teamsById[x.TeamId].TeamCode).ThenBy(x => x.TeamId)
            .Select(x => new V180TripContextTeamDto(
                x.TeamId, teamsById[x.TeamId].TeamCode, teamsById[x.TeamId].TeamName, x.IsPrimary))
            .ToList();

        var primaryMemberships = memberships.Where(x => x.IsPrimary).ToList();
        if (primaryMemberships.Count > 1)
            throw new InvalidOperationException("AMBIGUOUS_PRIMARY_TEAM：VisitDate 有多個 Primary Team。");

        int? selectedTeamId;
        if (teamId.HasValue)
        {
            if (!memberships.Any(x => x.TeamId == teamId.Value))
                throw new InvalidOperationException("TRIP_CONTEXT_TEAM_NOT_ASSIGNED：指定 Team 不是 VisitDate 的有效 TeamMembership。");
            selectedTeamId = teamId.Value;
        }
        else
        {
            selectedTeamId = primaryMemberships.Count == 1
                ? primaryMemberships[0].TeamId
                : memberships.Count == 1 ? memberships[0].TeamId : null;
        }

        if (!selectedTeamId.HasValue)
        {
            var code = memberships.Count == 0 ? "NO_TEAM_MEMBERSHIP" : "TEAM_SELECTION_REQUIRED";
            var message = memberships.Count == 0
                ? "外訪日期當日沒有有效 TeamMembership。"
                : "有多個可用 Team，請明確選擇後再取得派駐點。";
            return new(employment.EmploymentId, visitDate, false, code, message,
                teamDtos, null, [], null, null, null);
        }

        var employmentAssignments = await db.EmploymentDeploymentSiteAssignments.AsNoTracking()
            .Where(x => x.EmploymentId == employment.EmploymentId && x.EffectiveFrom <= visitDate &&
                (!x.EffectiveTo.HasValue || visitDate <= x.EffectiveTo.Value))
            .ToListAsync(ct);
        if (employmentAssignments.GroupBy(x => x.DeploymentSiteId).Any(x => x.Count() > 1))
            throw new InvalidOperationException("AMBIGUOUS_EMPLOYMENT_SITE：VisitDate 的 Employment-Site assignment 重疊。");
        var primaryRows = employmentAssignments.Where(x => x.IsPrimary).ToList();
        if (primaryRows.Count > 1)
            throw new InvalidOperationException("AMBIGUOUS_PRIMARY_DEPLOYMENT_SITE：VisitDate 有多個 Primary Deployment Site。");

        var teamAssignments = await db.TeamDeploymentSiteAssignments.AsNoTracking()
            .Where(x => x.TeamId == selectedTeamId.Value && x.EffectiveFrom <= visitDate &&
                (!x.EffectiveTo.HasValue || visitDate <= x.EffectiveTo.Value))
            .ToListAsync(ct);
        if (teamAssignments.GroupBy(x => x.DeploymentSiteId).Any(x => x.Count() > 1))
            throw new InvalidOperationException("AMBIGUOUS_TEAM_SITE：VisitDate 的 Team-Site assignment 重疊。");

        var employmentSiteIds = employmentAssignments.Select(x => x.DeploymentSiteId).Distinct().ToArray();
        var assignedSites = await db.DeploymentSites.AsNoTracking()
            .Where(x => employmentSiteIds.Contains(x.DeploymentSiteId)).ToListAsync(ct);
        var centerIds = assignedSites.Select(x => x.CenterId).Distinct().ToArray();
        var centers = await db.Centers.AsNoTracking().Where(x => centerIds.Contains(x.CenterId))
            .ToDictionaryAsync(x => x.CenterId, ct);
        foreach (var site in assignedSites)
        {
            if (!centers.TryGetValue(site.CenterId, out var center) || center.OrganizationId != organizationId)
                throw new InvalidOperationException("TRIP_CONTEXT_SITE_ORGANIZATION_MISMATCH：Employment-Site 不屬於目前 Organization。");
        }

        var teamSiteIds = teamAssignments.Select(x => x.DeploymentSiteId).ToHashSet();
        var candidateIds = employmentSiteIds.Where(teamSiteIds.Contains).ToHashSet();
        var candidateSites = assignedSites.Where(x => candidateIds.Contains(x.DeploymentSiteId) && x.IsActive &&
            x.EffectiveFrom <= visitDate && (!x.EffectiveTo.HasValue || visitDate <= x.EffectiveTo.Value)).ToList();

        var locationAssignments = await db.DeploymentSiteLocationAssignments.AsNoTracking()
            .Where(x => candidateIds.Contains(x.DeploymentSiteId) && x.EffectiveFrom <= visitDate &&
                (!x.EffectiveTo.HasValue || visitDate <= x.EffectiveTo.Value))
            .ToListAsync(ct);
        if (locationAssignments.GroupBy(x => x.DeploymentSiteId).Any(x => x.Count() > 1))
            throw new InvalidOperationException("AMBIGUOUS_DEPLOYMENT_LOCATION：VisitDate 的派駐點有多個有效 Location。");
        var locationsBySite = locationAssignments.ToDictionary(x => x.DeploymentSiteId);
        var locationIds = locationAssignments.Select(x => x.LocationId).Distinct().ToArray();
        var locations = await db.Locations.AsNoTracking().Where(x => locationIds.Contains(x.LocationId))
            .ToDictionaryAsync(x => x.LocationId, ct);

        var eligibleSites = new List<V180TripContextDeploymentSiteDto>();
        foreach (var site in candidateSites.OrderBy(x => x.SiteCode).ThenBy(x => x.DeploymentSiteId))
        {
            if (!locationsBySite.TryGetValue(site.DeploymentSiteId, out var locationAssignment))
                continue;
            if (!locations.TryGetValue(locationAssignment.LocationId, out var location))
                throw new InvalidOperationException("TRIP_CONTEXT_LOCATION_MISSING：派駐點有效 Location 不存在。");
            if (location.OrganizationId.HasValue && location.OrganizationId.Value != organizationId)
                throw new InvalidOperationException("TRIP_CONTEXT_LOCATION_ORGANIZATION_MISMATCH：派駐點 Location 不屬於目前 Organization。");
            var center = centers[site.CenterId];
            eligibleSites.Add(new(site.DeploymentSiteId, center.CenterId, center.CenterCode, center.CenterName,
                site.SiteCode, site.SiteName, location.LocationId, location.LocationCode,
                location.LocationName, location.Address,
                primaryRows.Count == 1 && primaryRows[0].DeploymentSiteId == site.DeploymentSiteId));
        }

        var primarySiteId = eligibleSites.SingleOrDefault(x => x.IsPrimary)?.DeploymentSiteId;
        var eligible = eligibleSites.Count > 0;
        return new(employment.EmploymentId, visitDate, eligible,
            eligible ? "OK" : "NO_ELIGIBLE_DEPLOYMENT_SITE",
            eligible ? "行程 context 已解析。" : "外訪日期與所選 Team 沒有共同且具有效 Location 的派駐點。",
            teamDtos, selectedTeamId, eligibleSites, primarySiteId, primarySiteId, primarySiteId);
    }

    private static V180TripContextDto Empty(long employmentId, DateOnly visitDate, string code, string message) =>
        new(employmentId, visitDate, false, code, message, [], null, [], null, null, null);
}
