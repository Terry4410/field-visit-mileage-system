using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180DeploymentSiteReader(AppDbContext db) : IV180DeploymentSiteReader
{
    public async Task<PagedResult<V180DeploymentSiteAdminDto>> SearchAsync(
        CurrentUserDto user, V180DeploymentSiteQuery input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(user);
        var query = Normalize(input);
        var asOf = query.AsOf!.Value;
        var source = db.Set<DeploymentSite>().AsNoTracking()
            .Where(x => x.Center.OrganizationId == organizationId);
        if (query.CenterId.HasValue)
            source = source.Where(x => x.CenterId == query.CenterId.Value);
        if (!string.IsNullOrEmpty(query.Keyword))
        {
            var keyword = query.Keyword;
            source = source.Where(x => x.SiteCode.Contains(keyword) || x.SiteName.Contains(keyword));
        }
        if (!query.IncludeInactive)
            source = source.Where(x => x.IsActive && x.EffectiveFrom <= asOf &&
                (!x.EffectiveTo.HasValue || asOf <= x.EffectiveTo.Value));

        var total = await source.CountAsync(ct);
        var rows = await source.Include(x => x.Center)
            .OrderBy(x => x.Center.CenterCode).ThenBy(x => x.SiteCode).ThenBy(x => x.DeploymentSiteId)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(await MapSitesAsync(rows, organizationId, asOf, ct), query.Page, query.PageSize, total);
    }

    public async Task<V180DeploymentSiteAdminDto?> GetAsync(
        CurrentUserDto user, int deploymentSiteId, DateOnly? asOf, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(user);
        var row = await db.Set<DeploymentSite>().AsNoTracking().Include(x => x.Center)
            .SingleOrDefaultAsync(x => x.DeploymentSiteId == deploymentSiteId &&
                x.Center.OrganizationId == organizationId, ct);
        if (row is null) return null;
        return (await MapSitesAsync([row], organizationId, asOf ?? BusinessTime.Today, ct)).Single();
    }

    public async Task<IReadOnlyList<V180DeploymentSiteLocationAssignmentDto>> LocationAssignmentsAsync(
        CurrentUserDto user, int deploymentSiteId, bool includeHistory, DateOnly? asOf, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(user);
        await RequireSiteAsync(deploymentSiteId, organizationId, ct);
        var date = asOf ?? BusinessTime.Today;
        var source = db.Set<DeploymentSiteLocationAssignment>().AsNoTracking()
            .Where(x => x.DeploymentSiteId == deploymentSiteId);
        if (!includeHistory)
            source = source.Where(x => !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= date);
        var rows = await source.OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.DeploymentSiteLocationAssignmentId).ToListAsync(ct);
        var locationIds = rows.Select(x => x.LocationId).Distinct().ToArray();
        var locations = await db.Locations.AsNoTracking().Where(x => locationIds.Contains(x.LocationId))
            .ToDictionaryAsync(x => x.LocationId, ct);
        return rows.Select(x =>
        {
            if (!locations.TryGetValue(x.LocationId, out var location))
                throw new InvalidOperationException("DEPLOYMENT_LOCATION_MISSING：派駐點 Location 關聯不存在。");
            EnsureLocationOrganization(location, organizationId);
            return new V180DeploymentSiteLocationAssignmentDto(
                x.DeploymentSiteLocationAssignmentId, x.DeploymentSiteId, x.LocationId,
                location.LocationCode, location.LocationName, location.Address,
                x.EffectiveFrom, x.EffectiveTo, x.ChangeReason, Convert.ToBase64String(x.RowVersion));
        }).ToList();
    }

    public async Task<IReadOnlyList<V180TeamDeploymentSiteAssignmentDto>> TeamAssignmentsAsync(
        CurrentUserDto user, int deploymentSiteId, bool includeHistory, DateOnly? asOf, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(user);
        var site = await RequireSiteAsync(deploymentSiteId, organizationId, ct);
        var date = asOf ?? BusinessTime.Today;
        var source = db.Set<TeamDeploymentSiteAssignment>().AsNoTracking()
            .Where(x => x.DeploymentSiteId == deploymentSiteId);
        if (!includeHistory)
            source = source.Where(x => !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= date);
        var rows = await source.OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.TeamDeploymentSiteAssignmentId).ToListAsync(ct);
        var teamIds = rows.Select(x => x.TeamId).Distinct().ToArray();
        var teams = await db.Teams.AsNoTracking().Where(x => teamIds.Contains(x.TeamId))
            .ToDictionaryAsync(x => x.TeamId, ct);
        return rows.Select(x =>
        {
            if (!teams.TryGetValue(x.TeamId, out var team) || team.OrganizationId != organizationId)
                throw new InvalidOperationException("TEAM_SITE_ORGANIZATION_MISMATCH：Team 不屬於派駐點 Organization。");
            return new V180TeamDeploymentSiteAssignmentDto(
                x.TeamDeploymentSiteAssignmentId, x.TeamId, team.TeamCode, team.TeamName,
                site.DeploymentSiteId, site.SiteCode, site.SiteName,
                x.EffectiveFrom, x.EffectiveTo, Convert.ToBase64String(x.RowVersion));
        }).ToList();
    }

    public async Task<IReadOnlyList<V180EmploymentDeploymentSiteAssignmentDto>> EmploymentAssignmentsAsync(
        CurrentUserDto user, int deploymentSiteId, bool includeHistory, DateOnly? asOf, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(user);
        var site = await RequireSiteAsync(deploymentSiteId, organizationId, ct);
        var date = asOf ?? BusinessTime.Today;
        var source = db.Set<EmploymentDeploymentSiteAssignment>().AsNoTracking()
            .Where(x => x.DeploymentSiteId == deploymentSiteId);
        if (!includeHistory)
            source = source.Where(x => !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= date);
        var rows = await source.OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.EmploymentDeploymentSiteAssignmentId).ToListAsync(ct);
        var employmentIds = rows.Select(x => x.EmploymentId).Distinct().ToArray();
        var employments = await db.Employments.AsNoTracking().Where(x => employmentIds.Contains(x.EmploymentId))
            .ToDictionaryAsync(x => x.EmploymentId, ct);
        var personIds = employments.Values.Select(x => x.PersonId).Distinct().ToArray();
        var people = await db.Persons.AsNoTracking().Where(x => personIds.Contains(x.PersonId))
            .ToDictionaryAsync(x => x.PersonId, ct);
        return rows.Select(x =>
        {
            if (!employments.TryGetValue(x.EmploymentId, out var employment) || employment.OrganizationId != organizationId)
                throw new InvalidOperationException("EMPLOYMENT_SITE_ORGANIZATION_MISMATCH：Employment 不屬於派駐點 Organization。");
            if (!people.TryGetValue(employment.PersonId, out var person))
                throw new InvalidOperationException("Employment 對應不到 Person。");
            return new V180EmploymentDeploymentSiteAssignmentDto(
                x.EmploymentDeploymentSiteAssignmentId, x.EmploymentId, employment.EmployeeNo,
                person.DisplayName, site.DeploymentSiteId, site.SiteCode, site.SiteName,
                x.IsPrimary, x.EffectiveFrom, x.EffectiveTo, Convert.ToBase64String(x.RowVersion));
        }).ToList();
    }

    private async Task<IReadOnlyList<V180DeploymentSiteAdminDto>> MapSitesAsync(
        IReadOnlyList<DeploymentSite> sites, int organizationId, DateOnly asOf, CancellationToken ct)
    {
        if (sites.Count == 0) return [];
        var siteIds = sites.Select(x => x.DeploymentSiteId).ToArray();
        var locationRows = await db.Set<DeploymentSiteLocationAssignment>().AsNoTracking()
            .Where(x => siteIds.Contains(x.DeploymentSiteId) && x.EffectiveFrom <= asOf &&
                (!x.EffectiveTo.HasValue || asOf <= x.EffectiveTo.Value)).ToListAsync(ct);
        if (locationRows.GroupBy(x => x.DeploymentSiteId).Any(x => x.Count() > 1))
            throw new InvalidOperationException("AMBIGUOUS_DEPLOYMENT_LOCATION：同一派駐點目前有多個 Location。");
        var locationIds = locationRows.Select(x => x.LocationId).Distinct().ToArray();
        var locations = await db.Locations.AsNoTracking().Where(x => locationIds.Contains(x.LocationId))
            .ToDictionaryAsync(x => x.LocationId, ct);
        var teamRows = await db.Set<TeamDeploymentSiteAssignment>().AsNoTracking()
            .Where(x => siteIds.Contains(x.DeploymentSiteId) && x.EffectiveFrom <= asOf &&
                (!x.EffectiveTo.HasValue || asOf <= x.EffectiveTo.Value)).ToListAsync(ct);
        var employmentRows = await db.Set<EmploymentDeploymentSiteAssignment>().AsNoTracking()
            .Where(x => siteIds.Contains(x.DeploymentSiteId) && x.EffectiveFrom <= asOf &&
                (!x.EffectiveTo.HasValue || asOf <= x.EffectiveTo.Value)).ToListAsync(ct);

        return sites.Select(site =>
        {
            var locationAssignment = locationRows.SingleOrDefault(x => x.DeploymentSiteId == site.DeploymentSiteId);
            V180DeploymentSiteLocationSummary? locationDto = null;
            if (locationAssignment is not null)
            {
                if (!locations.TryGetValue(locationAssignment.LocationId, out var location))
                    throw new InvalidOperationException("DEPLOYMENT_LOCATION_MISSING：派駐點 Location 關聯不存在。");
                EnsureLocationOrganization(location, organizationId);
                locationDto = new(location.LocationId, location.LocationCode, location.LocationName, location.Address);
            }
            return new V180DeploymentSiteAdminDto(
                site.DeploymentSiteId, site.CenterId, site.Center.CenterCode, site.Center.CenterName,
                site.SiteCode, site.SiteName, site.EffectiveFrom, site.EffectiveTo, site.IsActive, site.Notes,
                locationDto,
                teamRows.Count(x => x.DeploymentSiteId == site.DeploymentSiteId),
                employmentRows.Count(x => x.DeploymentSiteId == site.DeploymentSiteId),
                Convert.ToBase64String(site.RowVersion));
        }).ToList();
    }

    private async Task<DeploymentSite> RequireSiteAsync(int siteId, int organizationId, CancellationToken ct) =>
        await db.Set<DeploymentSite>().AsNoTracking().Include(x => x.Center)
            .SingleOrDefaultAsync(x => x.DeploymentSiteId == siteId && x.Center.OrganizationId == organizationId, ct)
        ?? throw new KeyNotFoundException("找不到派駐點。");

    private static void EnsureLocationOrganization(Location location, int organizationId)
    {
        if (location.OrganizationId.HasValue && location.OrganizationId.Value != organizationId)
            throw new InvalidOperationException("DEPLOYMENT_LOCATION_ORGANIZATION_MISMATCH：Location 不屬於派駐點 Organization。");
    }

    private static V180DeploymentSiteQuery Normalize(V180DeploymentSiteQuery input)
    {
        var keyword = string.IsNullOrWhiteSpace(input.Keyword) ? null : input.Keyword.Trim();
        if (keyword?.Length > 200) throw new InvalidOperationException("搜尋關鍵字不可超過 200 字。");
        return input with
        {
            AsOf = input.AsOf ?? BusinessTime.Today,
            Keyword = keyword,
            Page = Math.Max(1, input.Page),
            PageSize = Math.Clamp(input.PageSize, 1, 100)
        };
    }

    private static int RequireAdminOrganization(CurrentUserDto user)
    {
        if (!user.Roles.Any(x => x.Equals("admin", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("只有管理者可以查詢 v1.8 派駐點主檔。");
        return user.OrganizationId ?? throw new InvalidOperationException("目前帳號缺少 OrganizationId。");
    }
}
