using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180DeploymentSiteWriter(AppDbContext db) : IV180DeploymentSiteWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<V180DeploymentSiteWriteResult> CreateSiteAsync(
        CurrentUserDto admin, V180CreateDeploymentSiteRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var center = await RequireCenterAsync(input.CenterId, organizationId, ct);
        var code = V180TeamCenterLifecycleRules.NormalizeCode(input.Code, "派駐點");
        var name = V180TeamCenterLifecycleRules.NormalizeName(input.Name, "派駐點");
        var notes = V180TeamCenterLifecycleRules.NormalizeNotes(input.Notes, 1000, "備註");
        ValidatePeriod(input.EffectiveFrom, input.EffectiveTo, "派駐點有效期間");
        EnsureWithin(input.EffectiveFrom, input.EffectiveTo, center.EffectiveFrom, center.EffectiveTo,
            "DEPLOYMENT_SITE_CENTER_PERIOD_CONFLICT：派駐點有效期間必須位於 Center 有效期間內。");
        if (input.IsActive && !center.IsActive)
            throw new InvalidOperationException("派駐點不可建立於已停用的 Center。");
        EnsureInactiveHasEnd(input.IsActive, input.EffectiveTo, "派駐點");
        if (await db.Set<DeploymentSite>().AnyAsync(x => x.CenterId == input.CenterId && x.SiteCode == code, ct))
            throw new InvalidOperationException("同一 Center 的派駐點代碼已存在。");

        var row = new DeploymentSite
        {
            CenterId = input.CenterId, SiteCode = code, SiteName = name,
            EffectiveFrom = input.EffectiveFrom, EffectiveTo = input.EffectiveTo,
            IsActive = input.IsActive, Notes = notes, CreatedAt = DateTime.UtcNow,
            CreatedByUserId = admin.UserId,
            InactivatedAt = input.IsActive ? null : DateTime.UtcNow,
            InactivatedByUserId = input.IsActive ? null : admin.UserId
        };
        db.Set<DeploymentSite>().Add(row);
        AddAudit(admin.UserId, "DeploymentSite", null, "V180DeploymentSiteCreate", input);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<V180DeploymentSiteWriteResult> UpdateSiteAsync(
        CurrentUserDto admin, int deploymentSiteId, V180UpdateDeploymentSiteRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var row = await RequireSiteAsync(deploymentSiteId, organizationId, ct);
        EnsureVersion(row.RowVersion, input.Version);
        var center = await RequireCenterAsync(input.CenterId, organizationId, ct);
        var code = V180TeamCenterLifecycleRules.NormalizeCode(input.Code, "派駐點");
        var name = V180TeamCenterLifecycleRules.NormalizeName(input.Name, "派駐點");
        var notes = V180TeamCenterLifecycleRules.NormalizeNotes(input.Notes, 1000, "備註");
        ValidatePeriod(input.EffectiveFrom, input.EffectiveTo, "派駐點有效期間");
        EnsureWithin(input.EffectiveFrom, input.EffectiveTo, center.EffectiveFrom, center.EffectiveTo,
            "DEPLOYMENT_SITE_CENTER_PERIOD_CONFLICT：派駐點有效期間必須位於 Center 有效期間內。");
        EnsureInactiveHasEnd(input.IsActive, input.EffectiveTo, "派駐點");
        if (input.IsActive && !center.IsActive)
            throw new InvalidOperationException("啟用中的派駐點不可隸屬已停用 Center。");
        if (await db.Set<DeploymentSite>().AnyAsync(x => x.CenterId == input.CenterId &&
            x.DeploymentSiteId != deploymentSiteId && x.SiteCode == code, ct))
            throw new InvalidOperationException("同一 Center 的派駐點代碼已存在。");

        await EnsureSiteRelationshipsAsync(row.DeploymentSiteId, organizationId, center,
            input.EffectiveFrom, input.EffectiveTo, ct);
        if (row.IsActive && !input.IsActive)
            await EnsureSiteCanDeactivateAsync(row.DeploymentSiteId, input.EffectiveTo!.Value, ct);

        var before = new { row.CenterId, row.SiteCode, row.SiteName, row.EffectiveFrom, row.EffectiveTo, row.IsActive, row.Notes };
        row.CenterId = input.CenterId; row.SiteCode = code; row.SiteName = name;
        row.EffectiveFrom = input.EffectiveFrom; row.EffectiveTo = input.EffectiveTo;
        row.IsActive = input.IsActive; row.Notes = notes; row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedByUserId = admin.UserId;
        row.InactivatedAt = input.IsActive ? null : DateTime.UtcNow;
        row.InactivatedByUserId = input.IsActive ? null : admin.UserId;
        AddAudit(admin.UserId, "DeploymentSite", deploymentSiteId.ToString(), "V180DeploymentSiteUpdate", new { before, input });
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<V180DeploymentSiteWriteResult> DeactivateSiteAsync(
        CurrentUserDto admin, int deploymentSiteId, V180DeactivateRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var row = await RequireSiteAsync(deploymentSiteId, organizationId, ct);
        EnsureVersion(row.RowVersion, input.Version);
        ValidatePeriod(row.EffectiveFrom, input.EffectiveTo, "派駐點有效期間");
        await EnsureSiteCanDeactivateAsync(row.DeploymentSiteId, input.EffectiveTo, ct);
        row.EffectiveTo = input.EffectiveTo; row.IsActive = false; row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedByUserId = admin.UserId; row.InactivatedAt = DateTime.UtcNow;
        row.InactivatedByUserId = admin.UserId;
        AddAudit(admin.UserId, "DeploymentSite", deploymentSiteId.ToString(), "V180DeploymentSiteDeactivate", input);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<V180DeploymentAssignmentWriteResult> CreateLocationAssignmentAsync(
        CurrentUserDto admin, V180CreateDeploymentSiteLocationAssignmentRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var site = await RequireSiteAsync(input.DeploymentSiteId, organizationId, ct);
        var location = await db.Locations.SingleOrDefaultAsync(x => x.LocationId == input.LocationId, ct)
            ?? throw new KeyNotFoundException("找不到 Location。");
        EnsureLocationOrganization(location, organizationId);
        ValidateAssignmentPeriod(site, input.EffectiveFrom, input.EffectiveTo, "Site-Location");
        await EnsureNoLocationOverlapAsync(input.DeploymentSiteId, input.EffectiveFrom, input.EffectiveTo, null, ct);
        var row = new DeploymentSiteLocationAssignment
        {
            DeploymentSiteId = input.DeploymentSiteId, LocationId = input.LocationId,
            EffectiveFrom = input.EffectiveFrom, EffectiveTo = input.EffectiveTo,
            ChangeReason = V180TeamCenterLifecycleRules.NormalizeNotes(input.ChangeReason, 500, "異動原因"),
            CreatedAt = DateTime.UtcNow, CreatedByUserId = admin.UserId
        };
        db.Set<DeploymentSiteLocationAssignment>().Add(row);
        AddAudit(admin.UserId, "DeploymentSiteLocationAssignment", null, "V180DeploymentLocationCreate", input);
        await db.SaveChangesAsync(ct);
        return Map(row.DeploymentSiteLocationAssignmentId, row.EffectiveFrom, row.EffectiveTo, row.RowVersion);
    }

    public async Task<V180DeploymentAssignmentWriteResult> UpdateLocationAssignmentAsync(
        CurrentUserDto admin, long assignmentId, V180UpdateDeploymentSiteLocationAssignmentRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var row = await RequireLocationAssignmentAsync(assignmentId, organizationId, ct);
        EnsureVersion(row.RowVersion, input.Version);
        ValidateAssignmentPeriod(row.DeploymentSite, input.EffectiveFrom, input.EffectiveTo, "Site-Location");
        await EnsureNoLocationOverlapAsync(row.DeploymentSiteId, input.EffectiveFrom, input.EffectiveTo, assignmentId, ct);
        row.EffectiveFrom = input.EffectiveFrom; row.EffectiveTo = input.EffectiveTo;
        row.ChangeReason = V180TeamCenterLifecycleRules.NormalizeNotes(input.ChangeReason, 500, "異動原因");
        AddAudit(admin.UserId, "DeploymentSiteLocationAssignment", assignmentId.ToString(), "V180DeploymentLocationUpdate", input);
        await db.SaveChangesAsync(ct);
        return Map(row.DeploymentSiteLocationAssignmentId, row.EffectiveFrom, row.EffectiveTo, row.RowVersion);
    }

    public async Task<V180DeploymentAssignmentWriteResult> EndLocationAssignmentAsync(
        CurrentUserDto admin, long assignmentId, V180DeactivateRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var row = await RequireLocationAssignmentAsync(assignmentId, organizationId, ct);
        EnsureVersion(row.RowVersion, input.Version);
        ValidateAssignmentPeriod(row.DeploymentSite, row.EffectiveFrom, input.EffectiveTo, "Site-Location");
        row.EffectiveTo = input.EffectiveTo;
        AddAudit(admin.UserId, "DeploymentSiteLocationAssignment", assignmentId.ToString(), "V180DeploymentLocationEnd", input);
        await db.SaveChangesAsync(ct);
        return Map(row.DeploymentSiteLocationAssignmentId, row.EffectiveFrom, row.EffectiveTo, row.RowVersion);
    }

    public async Task<V180DeploymentAssignmentWriteResult> CreateTeamAssignmentAsync(
        CurrentUserDto admin, V180CreateTeamDeploymentSiteAssignmentRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var site = await RequireSiteAsync(input.DeploymentSiteId, organizationId, ct);
        var team = await RequireTeamAsync(input.TeamId, organizationId, ct);
        await ValidateTeamAssignmentAsync(team, site, input.EffectiveFrom, input.EffectiveTo, ct);
        await EnsureNoTeamSiteOverlapAsync(input.TeamId, input.DeploymentSiteId,
            input.EffectiveFrom, input.EffectiveTo, null, ct);
        var row = new TeamDeploymentSiteAssignment
        {
            TeamId = input.TeamId, DeploymentSiteId = input.DeploymentSiteId,
            EffectiveFrom = input.EffectiveFrom, EffectiveTo = input.EffectiveTo,
            CreatedAt = DateTime.UtcNow, CreatedByUserId = admin.UserId
        };
        db.Set<TeamDeploymentSiteAssignment>().Add(row);
        AddAudit(admin.UserId, "TeamDeploymentSiteAssignment", null, "V180TeamDeploymentCreate", input);
        await db.SaveChangesAsync(ct);
        return Map(row.TeamDeploymentSiteAssignmentId, row.EffectiveFrom, row.EffectiveTo, row.RowVersion);
    }

    public async Task<V180DeploymentAssignmentWriteResult> UpdateTeamAssignmentAsync(
        CurrentUserDto admin, long assignmentId, V180UpdateTeamDeploymentSiteAssignmentRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var row = await RequireTeamAssignmentAsync(assignmentId, organizationId, ct);
        EnsureVersion(row.RowVersion, input.Version);
        var team = await RequireTeamAsync(row.TeamId, organizationId, ct);
        await ValidateTeamAssignmentAsync(team, row.DeploymentSite, input.EffectiveFrom, input.EffectiveTo, ct);
        await EnsureNoTeamSiteOverlapAsync(row.TeamId, row.DeploymentSiteId,
            input.EffectiveFrom, input.EffectiveTo, assignmentId, ct);
        row.EffectiveFrom = input.EffectiveFrom; row.EffectiveTo = input.EffectiveTo;
        AddAudit(admin.UserId, "TeamDeploymentSiteAssignment", assignmentId.ToString(), "V180TeamDeploymentUpdate", input);
        await db.SaveChangesAsync(ct);
        return Map(row.TeamDeploymentSiteAssignmentId, row.EffectiveFrom, row.EffectiveTo, row.RowVersion);
    }

    public async Task<V180DeploymentAssignmentWriteResult> EndTeamAssignmentAsync(
        CurrentUserDto admin, long assignmentId, V180DeactivateRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var row = await RequireTeamAssignmentAsync(assignmentId, organizationId, ct);
        EnsureVersion(row.RowVersion, input.Version);
        var team = await RequireTeamAsync(row.TeamId, organizationId, ct);
        await ValidateTeamAssignmentAsync(team, row.DeploymentSite, row.EffectiveFrom, input.EffectiveTo, ct);
        row.EffectiveTo = input.EffectiveTo;
        AddAudit(admin.UserId, "TeamDeploymentSiteAssignment", assignmentId.ToString(), "V180TeamDeploymentEnd", input);
        await db.SaveChangesAsync(ct);
        return Map(row.TeamDeploymentSiteAssignmentId, row.EffectiveFrom, row.EffectiveTo, row.RowVersion);
    }

    public async Task<V180DeploymentAssignmentWriteResult> CreateEmploymentAssignmentAsync(
        CurrentUserDto admin, V180CreateEmploymentDeploymentSiteAssignmentRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var site = await RequireSiteAsync(input.DeploymentSiteId, organizationId, ct);
        var employment = await RequireEmploymentAsync(input.EmploymentId, organizationId, ct);
        ValidateAssignmentPeriod(site, input.EffectiveFrom, input.EffectiveTo, "Employment-Site");
        if (input.IsPrimary)
            await EnsureNoPrimaryEmploymentOverlapAsync(input.EmploymentId, input.EffectiveFrom, input.EffectiveTo, null, ct);
        var row = new EmploymentDeploymentSiteAssignment
        {
            EmploymentId = employment.EmploymentId, DeploymentSiteId = site.DeploymentSiteId,
            IsPrimary = input.IsPrimary, EffectiveFrom = input.EffectiveFrom, EffectiveTo = input.EffectiveTo,
            CreatedAt = DateTime.UtcNow, CreatedByUserId = admin.UserId
        };
        db.Set<EmploymentDeploymentSiteAssignment>().Add(row);
        AddAudit(admin.UserId, "EmploymentDeploymentSiteAssignment", null, "V180EmploymentDeploymentCreate", input);
        await db.SaveChangesAsync(ct);
        return Map(row.EmploymentDeploymentSiteAssignmentId, row.EffectiveFrom, row.EffectiveTo, row.RowVersion);
    }

    public async Task<V180DeploymentAssignmentWriteResult> UpdateEmploymentAssignmentAsync(
        CurrentUserDto admin, long assignmentId, V180UpdateEmploymentDeploymentSiteAssignmentRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var row = await RequireEmploymentAssignmentAsync(assignmentId, organizationId, ct);
        EnsureVersion(row.RowVersion, input.Version);
        await RequireEmploymentAsync(row.EmploymentId, organizationId, ct);
        ValidateAssignmentPeriod(row.DeploymentSite, input.EffectiveFrom, input.EffectiveTo, "Employment-Site");
        if (input.IsPrimary)
            await EnsureNoPrimaryEmploymentOverlapAsync(row.EmploymentId, input.EffectiveFrom, input.EffectiveTo, assignmentId, ct);
        row.IsPrimary = input.IsPrimary; row.EffectiveFrom = input.EffectiveFrom; row.EffectiveTo = input.EffectiveTo;
        AddAudit(admin.UserId, "EmploymentDeploymentSiteAssignment", assignmentId.ToString(), "V180EmploymentDeploymentUpdate", input);
        await db.SaveChangesAsync(ct);
        return Map(row.EmploymentDeploymentSiteAssignmentId, row.EffectiveFrom, row.EffectiveTo, row.RowVersion);
    }

    public async Task<V180DeploymentAssignmentWriteResult> EndEmploymentAssignmentAsync(
        CurrentUserDto admin, long assignmentId, V180DeactivateRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var row = await RequireEmploymentAssignmentAsync(assignmentId, organizationId, ct);
        EnsureVersion(row.RowVersion, input.Version);
        ValidateAssignmentPeriod(row.DeploymentSite, row.EffectiveFrom, input.EffectiveTo, "Employment-Site");
        row.EffectiveTo = input.EffectiveTo;
        AddAudit(admin.UserId, "EmploymentDeploymentSiteAssignment", assignmentId.ToString(), "V180EmploymentDeploymentEnd", input);
        await db.SaveChangesAsync(ct);
        return Map(row.EmploymentDeploymentSiteAssignmentId, row.EffectiveFrom, row.EffectiveTo, row.RowVersion);
    }

    private async Task EnsureSiteRelationshipsAsync(int siteId, int organizationId, Center center,
        DateOnly from, DateOnly? to, CancellationToken ct)
    {
        var locations = await db.Set<DeploymentSiteLocationAssignment>().AsNoTracking()
            .Where(x => x.DeploymentSiteId == siteId).ToListAsync(ct);
        if (locations.Any(x => !V180DeploymentSiteRules.IsWithin(x.EffectiveFrom, x.EffectiveTo, from, to)))
            throw new InvalidOperationException("DEPLOYMENT_SITE_LIFECYCLE_CONFLICT：既有或未來 Location assignment 超出派駐點有效期間。");
        var teams = await db.Set<TeamDeploymentSiteAssignment>().AsNoTracking()
            .Where(x => x.DeploymentSiteId == siteId).ToListAsync(ct);
        foreach (var assignment in teams)
        {
            var team = await RequireTeamAsync(assignment.TeamId, organizationId, ct);
            if (!V180DeploymentSiteRules.IsWithin(assignment.EffectiveFrom, assignment.EffectiveTo, from, to))
                throw new InvalidOperationException("DEPLOYMENT_SITE_LIFECYCLE_CONFLICT：既有或未來 Team assignment 超出派駐點有效期間。");
            if (!await HasTeamCenterCoverageAsync(team.TeamId, center.CenterId,
                assignment.EffectiveFrom, assignment.EffectiveTo, ct))
                throw new InvalidOperationException("TEAM_SITE_CENTER_CONFLICT：調整 Center 後既有 Team-Site 缺少完整 Team-Center coverage。");
        }
        var employments = await db.Set<EmploymentDeploymentSiteAssignment>().AsNoTracking()
            .Where(x => x.DeploymentSiteId == siteId).ToListAsync(ct);
        foreach (var assignment in employments)
        {
            await RequireEmploymentAsync(assignment.EmploymentId, organizationId, ct);
            if (!V180DeploymentSiteRules.IsWithin(assignment.EffectiveFrom, assignment.EffectiveTo, from, to))
                throw new InvalidOperationException("DEPLOYMENT_SITE_LIFECYCLE_CONFLICT：既有或未來 Employment assignment 超出派駐點有效期間。");
        }
    }

    private async Task EnsureSiteCanDeactivateAsync(int siteId, DateOnly end, CancellationToken ct)
    {
        if (await db.Set<DeploymentSiteLocationAssignment>().AnyAsync(x => x.DeploymentSiteId == siteId &&
            (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= end), ct))
            throw new InvalidOperationException("DEPLOYMENT_SITE_DEACTIVATION_BLOCKED：仍有生效或未來 Location assignment。");
        if (await db.Set<TeamDeploymentSiteAssignment>().AnyAsync(x => x.DeploymentSiteId == siteId &&
            (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= end), ct))
            throw new InvalidOperationException("DEPLOYMENT_SITE_DEACTIVATION_BLOCKED：仍有生效或未來 Team assignment。");
        if (await db.Set<EmploymentDeploymentSiteAssignment>().AnyAsync(x => x.DeploymentSiteId == siteId &&
            (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= end), ct))
            throw new InvalidOperationException("DEPLOYMENT_SITE_DEACTIVATION_BLOCKED：仍有生效或未來 Employment assignment。");
    }

    private async Task ValidateTeamAssignmentAsync(Team team, DeploymentSite site,
        DateOnly from, DateOnly? to, CancellationToken ct)
    {
        if (!team.IsActive)
            throw new InvalidOperationException("停用 Team 不可新增或延長派駐點關聯。");
        ValidateAssignmentPeriod(site, from, to, "Team-Site");
        var teamFrom = team.EffectiveFrom ?? DateOnly.MinValue;
        EnsureWithin(from, to, teamFrom, team.EffectiveTo,
            "TEAM_SITE_TEAM_PERIOD_CONFLICT：Team-Site period 必須位於 Team 有效期間內。");
        if (!await HasTeamCenterCoverageAsync(team.TeamId, site.CenterId, from, to, ct))
            throw new InvalidOperationException("TEAM_SITE_CENTER_CONFLICT：Team-Site 必須有相同 Center 且完整涵蓋期間的 Team-Center assignment。");
    }

    private async Task<bool> HasTeamCenterCoverageAsync(int teamId, int centerId,
        DateOnly from, DateOnly? to, CancellationToken ct)
    {
        var rows = await db.TeamCenterAssignments.AsNoTracking()
            .Where(x => x.TeamId == teamId && x.CenterId == centerId).ToListAsync(ct);
        return rows.Any(x => V180TeamCenterLifecycleRules.IsWithin(from, to, x.EffectiveFrom, x.EffectiveTo));
    }

    private async Task EnsureNoLocationOverlapAsync(int siteId, DateOnly from, DateOnly? to,
        long? excludedId, CancellationToken ct)
    {
        var rows = await db.Set<DeploymentSiteLocationAssignment>().AsNoTracking()
            .Where(x => x.DeploymentSiteId == siteId &&
                (!excludedId.HasValue || x.DeploymentSiteLocationAssignmentId != excludedId.Value)).ToListAsync(ct);
        if (rows.Any(x => V180DeploymentSiteRules.Overlaps(from, to, x.EffectiveFrom, x.EffectiveTo)))
            throw new InvalidOperationException("DEPLOYMENT_LOCATION_OVERLAP：同一派駐點 Location periods 不得重疊。");
    }

    private async Task EnsureNoTeamSiteOverlapAsync(int teamId, int siteId, DateOnly from, DateOnly? to,
        long? excludedId, CancellationToken ct)
    {
        var rows = await db.Set<TeamDeploymentSiteAssignment>().AsNoTracking()
            .Where(x => x.TeamId == teamId && x.DeploymentSiteId == siteId &&
                (!excludedId.HasValue || x.TeamDeploymentSiteAssignmentId != excludedId.Value)).ToListAsync(ct);
        if (rows.Any(x => V180DeploymentSiteRules.Overlaps(from, to, x.EffectiveFrom, x.EffectiveTo)))
            throw new InvalidOperationException("TEAM_SITE_OVERLAP：相同 Team/Site effective periods 不得重疊。");
    }

    private async Task EnsureNoPrimaryEmploymentOverlapAsync(long employmentId, DateOnly from, DateOnly? to,
        long? excludedId, CancellationToken ct)
    {
        var rows = await db.Set<EmploymentDeploymentSiteAssignment>().AsNoTracking()
            .Where(x => x.EmploymentId == employmentId && x.IsPrimary &&
                (!excludedId.HasValue || x.EmploymentDeploymentSiteAssignmentId != excludedId.Value)).ToListAsync(ct);
        if (rows.Any(x => V180DeploymentSiteRules.Overlaps(from, to, x.EffectiveFrom, x.EffectiveTo)))
            throw new InvalidOperationException("EMPLOYMENT_SITE_PRIMARY_OVERLAP：同一 Employment 同期間只能有一個 Primary Deployment Site。");
    }

    private static void ValidateAssignmentPeriod(DeploymentSite site, DateOnly from, DateOnly? to, string label)
    {
        ValidatePeriod(from, to, $"{label} 有效期間");
        EnsureWithin(from, to, site.EffectiveFrom, site.EffectiveTo,
            $"{label.ToUpperInvariant()}_PERIOD_CONFLICT：assignment period 必須位於派駐點有效期間內。");
    }

    private static void ValidatePeriod(DateOnly from, DateOnly? to, string label) =>
        V180TeamCenterLifecycleRules.ValidatePeriod(from, to, label);

    private static void EnsureWithin(DateOnly from, DateOnly? to, DateOnly ownerFrom, DateOnly? ownerTo, string message)
    {
        if (!V180DeploymentSiteRules.IsWithin(from, to, ownerFrom, ownerTo))
            throw new InvalidOperationException(message);
    }

    private static void EnsureInactiveHasEnd(bool isActive, DateOnly? to, string label)
    {
        if (!isActive && !to.HasValue)
            throw new InvalidOperationException($"{label}停用時必須提供 EffectiveTo。");
    }

    private async Task<Center> RequireCenterAsync(int centerId, int organizationId, CancellationToken ct) =>
        await db.Centers.SingleOrDefaultAsync(x => x.CenterId == centerId && x.OrganizationId == organizationId, ct)
        ?? throw new KeyNotFoundException("找不到 Center。");

    private async Task<DeploymentSite> RequireSiteAsync(int siteId, int organizationId, CancellationToken ct) =>
        await db.Set<DeploymentSite>().Include(x => x.Center)
            .SingleOrDefaultAsync(x => x.DeploymentSiteId == siteId && x.Center.OrganizationId == organizationId, ct)
        ?? throw new KeyNotFoundException("找不到派駐點。");

    private async Task<Team> RequireTeamAsync(int teamId, int organizationId, CancellationToken ct) =>
        await db.Teams.SingleOrDefaultAsync(x => x.TeamId == teamId && x.OrganizationId == organizationId, ct)
        ?? throw new KeyNotFoundException("找不到 Team。");

    private async Task<Employment> RequireEmploymentAsync(long employmentId, int organizationId, CancellationToken ct) =>
        await db.Employments.SingleOrDefaultAsync(x => x.EmploymentId == employmentId && x.OrganizationId == organizationId, ct)
        ?? throw new KeyNotFoundException("找不到 Employment。");

    private async Task<DeploymentSiteLocationAssignment> RequireLocationAssignmentAsync(
        long id, int organizationId, CancellationToken ct) =>
        await db.Set<DeploymentSiteLocationAssignment>().Include(x => x.DeploymentSite).ThenInclude(x => x.Center)
            .SingleOrDefaultAsync(x => x.DeploymentSiteLocationAssignmentId == id &&
                x.DeploymentSite.Center.OrganizationId == organizationId, ct)
        ?? throw new KeyNotFoundException("找不到 Site-Location assignment。");

    private async Task<TeamDeploymentSiteAssignment> RequireTeamAssignmentAsync(
        long id, int organizationId, CancellationToken ct) =>
        await db.Set<TeamDeploymentSiteAssignment>().Include(x => x.DeploymentSite).ThenInclude(x => x.Center)
            .SingleOrDefaultAsync(x => x.TeamDeploymentSiteAssignmentId == id &&
                x.DeploymentSite.Center.OrganizationId == organizationId, ct)
        ?? throw new KeyNotFoundException("找不到 Team-Site assignment。");

    private async Task<EmploymentDeploymentSiteAssignment> RequireEmploymentAssignmentAsync(
        long id, int organizationId, CancellationToken ct) =>
        await db.Set<EmploymentDeploymentSiteAssignment>().Include(x => x.DeploymentSite).ThenInclude(x => x.Center)
            .SingleOrDefaultAsync(x => x.EmploymentDeploymentSiteAssignmentId == id &&
                x.DeploymentSite.Center.OrganizationId == organizationId, ct)
        ?? throw new KeyNotFoundException("找不到 Employment-Site assignment。");

    private static void EnsureLocationOrganization(Location location, int organizationId)
    {
        if (location.OrganizationId.HasValue && location.OrganizationId.Value != organizationId)
            throw new InvalidOperationException("DEPLOYMENT_LOCATION_ORGANIZATION_MISMATCH：Location 不屬於派駐點 Organization。");
    }

    private static void EnsureVersion(byte[] current, string expected)
    {
        var decoded = V180DeploymentSiteRules.DecodeVersion(expected);
        if (!current.SequenceEqual(decoded))
            throw new InvalidOperationException("ROWVERSION_CONFLICT：資料已由其他使用者更新，請重新載入。");
    }

    private static V180DeploymentSiteWriteResult Map(DeploymentSite row) =>
        new(row.DeploymentSiteId, row.CenterId, row.SiteCode, row.SiteName,
            row.EffectiveFrom, row.EffectiveTo, row.IsActive, row.Notes,
            Convert.ToBase64String(row.RowVersion));

    private static V180DeploymentAssignmentWriteResult Map(long id, DateOnly from, DateOnly? to, byte[] version) =>
        new(id, from, to, Convert.ToBase64String(version));

    private void AddAudit(int userId, string entityType, string? entityId, string action, object payload) =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId, EntityType = entityType, EntityId = entityId, Action = action,
            NewValues = JsonSerializer.Serialize(payload, JsonOptions), CreatedAt = DateTime.UtcNow
        });

    private static int RequireAdminOrganization(CurrentUserDto user)
    {
        if (!user.Roles.Any(x => x.Equals("admin", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("只有管理者可以維護 v1.8 派駐點主檔。");
        return user.OrganizationId ?? throw new InvalidOperationException("目前帳號缺少 OrganizationId。");
    }
}
