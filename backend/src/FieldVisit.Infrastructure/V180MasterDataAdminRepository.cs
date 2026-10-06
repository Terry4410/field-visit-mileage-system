using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FieldVisit.Infrastructure;

public sealed class V180MasterDataAdminRepository(AppDbContext db) : IV180MasterDataAdminRepository
{
    public async Task<V180MasterDataReadinessDto> GetReadinessAsync(
        CurrentUserDto admin,
        CancellationToken ct)
    {
        var org = Org(admin);
        var today = BusinessTime.Today;

        var visitorRoleIds = db.Roles
            .Where(x => x.RoleCode.ToLower() == "visitor")
            .Select(x => x.RoleId);

        var activeVisitorUserIds = db.UserRoleAssignments
            .Where(x => visitorRoleIds.Contains(x.RoleId)
                && x.EffectiveFrom <= today
                && (!x.EffectiveTo.HasValue || today <= x.EffectiveTo.Value))
            .Select(x => x.UserId)
            .Distinct();

        var targetEmploymentIds = db.UserIdentityProfiles
            .Where(x => x.UserType == UserTypes.Internal
                && x.EmploymentId.HasValue
                && activeVisitorUserIds.Contains(x.UserId)
                && db.Users.Any(u => u.UserId == x.UserId
                    && u.IsActive
                    && u.OrganizationId == org))
            .Select(x => x.EmploymentId!.Value)
            .Distinct();

        var targetEmployments = db.Employments
            .Where(x => x.OrganizationId == org
                && targetEmploymentIds.Contains(x.EmploymentId));
        var targetEmploymentCount = await targetEmployments.CountAsync(ct);

        var employmentStatusMissing = await targetEmployments.CountAsync(
            e => !db.EmploymentStatusPeriods.Any(s =>
                s.EmploymentId == e.EmploymentId
                && s.EmploymentStatus == EmploymentStatuses.Active
                && s.EffectiveFrom <= today
                && (!s.EffectiveTo.HasValue || today <= s.EffectiveTo.Value)),
            ct);

        var currentTeamIds = (
            from membership in db.TeamMemberships
            join team in db.Teams on membership.TeamId equals team.TeamId
            where targetEmploymentIds.Contains(membership.EmploymentId)
                && membership.EffectiveFrom <= today
                && (!membership.EffectiveTo.HasValue || today <= membership.EffectiveTo.Value)
                && team.OrganizationId == org
                && team.IsActive
                && (!team.EffectiveFrom.HasValue || team.EffectiveFrom.Value <= today)
                && (!team.EffectiveTo.HasValue || today <= team.EffectiveTo.Value)
            select membership.TeamId)
            .Distinct();

        var teamCenterMissing = await currentTeamIds.CountAsync(
            teamId => !db.TeamCenterAssignments.Any(x =>
                x.TeamId == teamId
                && x.EffectiveFrom <= today
                && (!x.EffectiveTo.HasValue || today <= x.EffectiveTo.Value)),
            ct);

        var eligibleSites = db.DeploymentSites
            .Where(s => s.IsActive
                && s.EffectiveFrom <= today
                && (!s.EffectiveTo.HasValue || today <= s.EffectiveTo.Value)
                && db.Centers.Any(c => c.CenterId == s.CenterId
                    && c.OrganizationId == org
                    && c.IsActive
                    && c.EffectiveFrom <= today
                    && (!c.EffectiveTo.HasValue || today <= c.EffectiveTo.Value))
                && db.DeploymentSiteLocationAssignments.Any(a =>
                    a.DeploymentSiteId == s.DeploymentSiteId
                    && a.EffectiveFrom <= today
                    && (!a.EffectiveTo.HasValue || today <= a.EffectiveTo.Value)
                    && db.Locations.Any(l => l.LocationId == a.LocationId
                        && l.OrganizationId == org
                        && l.IsActive
                        && l.ApprovalStatus == "Approved")));

        var deploymentSiteCount = await eligibleSites.CountAsync(ct);

        var teamSiteMissing = await currentTeamIds.CountAsync(
            teamId => !db.TeamDeploymentSiteAssignments.Any(x =>
                x.TeamId == teamId
                && x.EffectiveFrom <= today
                && (!x.EffectiveTo.HasValue || today <= x.EffectiveTo.Value)
                && eligibleSites.Any(s =>
                    s.DeploymentSiteId == x.DeploymentSiteId
                    && db.TeamCenterAssignments.Any(center =>
                        center.TeamId == teamId
                        && center.CenterId == s.CenterId
                        && center.EffectiveFrom <= today
                        && (!center.EffectiveTo.HasValue || today <= center.EffectiveTo.Value)))),
            ct);

        var activeEmploymentIds = targetEmployments
            .Where(e => db.EmploymentStatusPeriods.Any(s =>
                s.EmploymentId == e.EmploymentId
                && s.EmploymentStatus == EmploymentStatuses.Active
                && s.EffectiveFrom <= today
                && (!s.EffectiveTo.HasValue || today <= s.EffectiveTo.Value)))
            .Select(e => e.EmploymentId);

        var employmentSiteMissing = await activeEmploymentIds.CountAsync(
            employmentId => !db.EmploymentDeploymentSiteAssignments.Any(es =>
                es.EmploymentId == employmentId
                && es.EffectiveFrom <= today
                && (!es.EffectiveTo.HasValue || today <= es.EffectiveTo.Value)
                && eligibleSites.Any(s => s.DeploymentSiteId == es.DeploymentSiteId)
                && db.TeamMemberships.Any(m =>
                    m.EmploymentId == employmentId
                    && m.EffectiveFrom <= today
                    && (!m.EffectiveTo.HasValue || today <= m.EffectiveTo.Value)
                    && db.Teams.Any(team =>
                        team.TeamId == m.TeamId
                        && team.OrganizationId == org
                        && team.IsActive
                        && (!team.EffectiveFrom.HasValue || team.EffectiveFrom.Value <= today)
                        && (!team.EffectiveTo.HasValue || today <= team.EffectiveTo.Value))
                    && eligibleSites.Any(site =>
                        site.DeploymentSiteId == es.DeploymentSiteId
                        && db.TeamCenterAssignments.Any(center =>
                            center.TeamId == m.TeamId
                            && center.CenterId == site.CenterId
                            && center.EffectiveFrom <= today
                            && (!center.EffectiveTo.HasValue || today <= center.EffectiveTo.Value))
                        && db.TeamDeploymentSiteAssignments.Any(ts =>
                            ts.TeamId == m.TeamId
                            && ts.DeploymentSiteId == es.DeploymentSiteId
                            && ts.EffectiveFrom <= today
                            && (!ts.EffectiveTo.HasValue || today <= ts.EffectiveTo.Value))))),
            ct);

        return new(
            employmentStatusMissing,
            teamCenterMissing,
            deploymentSiteCount,
            teamSiteMissing,
            employmentSiteMissing,
            targetEmploymentCount > 0
                && employmentStatusMissing == 0
                && teamCenterMissing == 0
                && deploymentSiteCount > 0
                && teamSiteMissing == 0
                && employmentSiteMissing == 0);
    }

    public async Task<IReadOnlyList<V180MasterDataRow>> ListAsync(
        CurrentUserDto admin,
        string kind,
        CancellationToken ct)
    {
        var org = Org(admin);
        kind = kind.Trim().ToLowerInvariant();

        return kind switch
        {
            "employment-status" => await (
                from x in db.EmploymentStatusPeriods
                join e in db.Employments on x.EmploymentId equals e.EmploymentId
                where e.OrganizationId == org
                select new V180MasterDataRow(
                    x.EmploymentStatusPeriodId,
                    e.EmployeeNo ?? "",
                    x.EmploymentStatus,
                    x.EmploymentStatus,
                    x.EffectiveFrom,
                    x.EffectiveTo,
                    null,
                    null,
                    B64(x.RowVersion),
                    null)).ToListAsync(ct),

            "centers" => await db.Centers
                .Where(x => x.OrganizationId == org)
                .Select(x => new V180MasterDataRow(
                    x.CenterId,
                    x.CenterCode,
                    null,
                    x.CenterName,
                    x.EffectiveFrom,
                    x.EffectiveTo,
                    x.IsActive,
                    null,
                    B64(x.RowVersion),
                    null))
                .ToListAsync(ct),

            "team-centers" => await (
                from x in db.TeamCenterAssignments
                join t in db.Teams on x.TeamId equals t.TeamId
                join c in db.Centers on x.CenterId equals c.CenterId
                where t.OrganizationId == org && c.OrganizationId == org
                select new V180MasterDataRow(
                    x.TeamCenterAssignmentId,
                    t.TeamCode,
                    c.CenterCode,
                    x.ChangeReason,
                    x.EffectiveFrom,
                    x.EffectiveTo,
                    null,
                    null,
                    B64(x.RowVersion),
                    null)).ToListAsync(ct),

            "deployment-sites" => await (
                from x in db.DeploymentSites
                join c in db.Centers on x.CenterId equals c.CenterId
                where c.OrganizationId == org
                let locationCode = (
                    from a in db.DeploymentSiteLocationAssignments
                    join l in db.Locations on a.LocationId equals l.LocationId
                    where a.DeploymentSiteId == x.DeploymentSiteId
                        && a.EffectiveFrom <= x.EffectiveFrom
                        && (!x.EffectiveTo.HasValue
                            ? !a.EffectiveTo.HasValue
                            : !a.EffectiveTo.HasValue || a.EffectiveTo.Value >= x.EffectiveTo.Value)
                    orderby a.EffectiveFrom descending
                    select l.LocationCode).FirstOrDefault()
                select new V180MasterDataRow(
                    x.DeploymentSiteId,
                    x.SiteCode,
                    c.CenterCode,
                    x.SiteName,
                    x.EffectiveFrom,
                    x.EffectiveTo,
                    x.IsActive,
                    null,
                    B64(x.RowVersion),
                    locationCode)).ToListAsync(ct),

            "team-sites" => await (
                from x in db.TeamDeploymentSiteAssignments
                join t in db.Teams on x.TeamId equals t.TeamId
                join s in db.DeploymentSites on x.DeploymentSiteId equals s.DeploymentSiteId
                join c in db.Centers on s.CenterId equals c.CenterId
                where t.OrganizationId == org && c.OrganizationId == org
                select new V180MasterDataRow(
                    x.TeamDeploymentSiteAssignmentId,
                    t.TeamCode,
                    s.SiteCode,
                    null,
                    x.EffectiveFrom,
                    x.EffectiveTo,
                    null,
                    null,
                    B64(x.RowVersion),
                    null)).ToListAsync(ct),

            "employment-sites" => await (
                from x in db.EmploymentDeploymentSiteAssignments
                join e in db.Employments on x.EmploymentId equals e.EmploymentId
                join s in db.DeploymentSites on x.DeploymentSiteId equals s.DeploymentSiteId
                join c in db.Centers on s.CenterId equals c.CenterId
                where e.OrganizationId == org && c.OrganizationId == org
                select new V180MasterDataRow(
                    x.EmploymentDeploymentSiteAssignmentId,
                    e.EmployeeNo ?? "",
                    s.SiteCode,
                    null,
                    x.EffectiveFrom,
                    x.EffectiveTo,
                    null,
                    x.IsPrimary,
                    B64(x.RowVersion),
                    null)).ToListAsync(ct),

            _ => throw new InvalidOperationException("UNKNOWN_MASTER_DATA_TYPE")
        };
    }

    public async Task<V180MasterDataRow> SaveEmploymentStatusAsync(
        CurrentUserDto admin,
        long? id,
        V180EmploymentStatusInput input,
        CancellationToken ct)
    {
        V180MasterDataValidationService.Period(input.EffectiveFrom, input.EffectiveTo);
        V180MasterDataValidationService.Status(input.Status);
        var employment = await Employment(admin, input.EmployeeNo, ct);

        return await ExecuteEmploymentStatusSiteInvariantAsync(
            employment.EmploymentId,
            () => SaveEmploymentStatusCoreAsync(
                admin,
                employment.EmploymentId,
                employment.EmployeeNo ?? string.Empty,
                id,
                input,
                ct),
            ct);
    }

    private async Task<V180MasterDataRow> SaveEmploymentStatusCoreAsync(
        CurrentUserDto admin,
        long employmentId,
        string employeeNo,
        long? id,
        V180EmploymentStatusInput input,
        CancellationToken ct)
    {
        var query = db.EmploymentStatusPeriods
            .Where(x => x.EmploymentId == employmentId);

        if (await query.AnyAsync(x =>
                (!id.HasValue || x.EmploymentStatusPeriodId != id.Value)
                && x.EffectiveFrom <= (input.EffectiveTo ?? DateOnly.MaxValue)
                && (!x.EffectiveTo.HasValue || input.EffectiveFrom <= x.EffectiveTo.Value),
                ct))
            throw new InvalidOperationException("OVERLAPPING_EMPLOYMENT_STATUS");

        EmploymentStatusPeriod row;
        if (id.HasValue)
        {
            row = await query.SingleOrDefaultAsync(
                x => x.EmploymentStatusPeriodId == id.Value,
                ct) ?? throw new InvalidOperationException("UNKNOWN_EMPLOYMENT_STATUS_PERIOD");
            V180MasterDataValidationService.RowVersion(input.RowVersion, row.RowVersion);

            var changesExistingActiveCoverage = row.EmploymentStatus == EmploymentStatuses.Active
                && (input.Status.Trim() != EmploymentStatuses.Active
                    || row.EffectiveFrom != input.EffectiveFrom
                    || row.EffectiveTo != input.EffectiveTo);
            if (changesExistingActiveCoverage)
            {
                var assignments = await db.EmploymentDeploymentSiteAssignments
                    .Where(x => x.EmploymentId == employmentId)
                    .Select(x => new { x.EffectiveFrom, x.EffectiveTo })
                    .ToListAsync(ct);
                var otherActivePeriods = await query
                    .Where(x => x.EmploymentStatusPeriodId != row.EmploymentStatusPeriodId
                        && x.EmploymentStatus == EmploymentStatuses.Active)
                    .Select(x => new { x.EffectiveFrom, x.EffectiveTo })
                    .ToListAsync(ct);

                foreach (var assignment in assignments)
                {
                    var coveredByProposed = input.Status.Trim() == EmploymentStatuses.Active
                        && V180MasterDataValidationService.Covers(
                            input.EffectiveFrom,
                            input.EffectiveTo,
                            assignment.EffectiveFrom,
                            assignment.EffectiveTo);
                    var coveredByOther = otherActivePeriods.Any(period =>
                        V180MasterDataValidationService.Covers(
                            period.EffectiveFrom,
                            period.EffectiveTo,
                            assignment.EffectiveFrom,
                            assignment.EffectiveTo));
                    if (!coveredByProposed && !coveredByOther)
                        throw new InvalidOperationException(
                            "EMPLOYMENT_STATUS_CHANGE_BREAKS_EMPLOYMENT_SITE");
                }
            }
        }
        else
        {
            row = new EmploymentStatusPeriod
            {
                EmploymentId = employmentId,
                SourceType = "UAT-Admin"
            };
            db.EmploymentStatusPeriods.Add(row);
        }

        row.EmploymentStatus = input.Status.Trim();
        row.EffectiveFrom = input.EffectiveFrom;
        row.EffectiveTo = input.EffectiveTo;

        AddAudit(
            admin,
            "EmploymentStatusPeriod",
            $"{employeeNo}:{input.EffectiveFrom:yyyy-MM-dd}",
            id.HasValue ? "Update" : "Create",
            new { input.Status, input.EffectiveFrom, input.EffectiveTo });

        await db.SaveChangesAsync(ct);
        return Row(
            row.EmploymentStatusPeriodId,
            employeeNo,
            row.EmploymentStatus,
            row.EmploymentStatus,
            row.EffectiveFrom,
            row.EffectiveTo,
            null,
            null,
            row.RowVersion);
    }

    public async Task<V180MasterDataRow> SaveCenterAsync(
        CurrentUserDto admin,
        int? id,
        V180CenterInput input,
        CancellationToken ct)
    {
        V180MasterDataValidationService.Period(input.EffectiveFrom, input.EffectiveTo);
        return await ExecuteTeamSiteCoverageInvariantAsync(
            () => SaveCenterCoreAsync(admin, id, input, ct), ct);
    }

    private async Task<V180MasterDataRow> SaveCenterCoreAsync(
        CurrentUserDto admin,
        int? id,
        V180CenterInput input,
        CancellationToken ct)
    {
        V180MasterDataValidationService.Period(input.EffectiveFrom, input.EffectiveTo);
        var org = Org(admin);
        var code = Required(input.CenterCode, "CENTER_CODE_REQUIRED");
        var name = Required(input.CenterName, "CENTER_NAME_REQUIRED");

        Center row;
        if (id.HasValue)
        {
            row = await db.Centers.SingleOrDefaultAsync(
                x => x.CenterId == id.Value && x.OrganizationId == org,
                ct) ?? throw new InvalidOperationException("UNKNOWN_CENTER_CODE");
            V180MasterDataValidationService.RowVersion(input.RowVersion, row.RowVersion);

            var breaksTeamCenter = await db.TeamCenterAssignments.AnyAsync(x =>
                x.CenterId == row.CenterId
                && (x.EffectiveFrom < input.EffectiveFrom
                    || (input.EffectiveTo.HasValue
                        && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value > input.EffectiveTo.Value))),
                ct);
            var breaksSites = await db.DeploymentSites.AnyAsync(x =>
                x.CenterId == row.CenterId
                && (x.EffectiveFrom < input.EffectiveFrom
                    || (input.EffectiveTo.HasValue
                        && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value > input.EffectiveTo.Value))),
                ct);
            if (breaksTeamCenter || breaksSites)
                throw new InvalidOperationException("CENTER_PERIOD_HAS_DEPENDENCIES");
        }
        else
        {
            row = new Center { OrganizationId = org };
            db.Centers.Add(row);
        }

        if (await db.Centers.AnyAsync(x =>
                x.OrganizationId == org
                && x.CenterCode == code
                && (!id.HasValue || x.CenterId != id.Value),
                ct))
            throw new InvalidOperationException("DUPLICATE_CENTER_CODE");

        row.CenterCode = code;
        row.CenterName = name;
        row.EffectiveFrom = input.EffectiveFrom;
        row.EffectiveTo = input.EffectiveTo;
        row.IsActive = input.IsActive;

        AddAudit(
            admin,
            "Center",
            code,
            id.HasValue ? "Update" : "Create",
            new { code, name, input.EffectiveFrom, input.EffectiveTo, input.IsActive });

        await db.SaveChangesAsync(ct);
        return Row(
            row.CenterId,
            row.CenterCode,
            null,
            row.CenterName,
            row.EffectiveFrom,
            row.EffectiveTo,
            row.IsActive,
            null,
            row.RowVersion);
    }

    public async Task<V180MasterDataRow> SaveTeamCenterAsync(
        CurrentUserDto admin,
        long? id,
        V180TeamCenterInput input,
        CancellationToken ct)
    {
        V180MasterDataValidationService.Period(input.EffectiveFrom, input.EffectiveTo);
        return await ExecuteTeamSiteCoverageInvariantAsync(
            () => SaveTeamCenterCoreAsync(admin, id, input, ct), ct);
    }

    private async Task<V180MasterDataRow> SaveTeamCenterCoreAsync(
        CurrentUserDto admin,
        long? id,
        V180TeamCenterInput input,
        CancellationToken ct)
    {
        V180MasterDataValidationService.Period(input.EffectiveFrom, input.EffectiveTo);
        var team = await Team(admin, input.TeamCode, ct);
        var center = await Center(admin, input.CenterCode, ct);

        if ((team.EffectiveFrom.HasValue && input.EffectiveFrom < team.EffectiveFrom.Value)
            || (team.EffectiveTo.HasValue
                && (!input.EffectiveTo.HasValue || input.EffectiveTo.Value > team.EffectiveTo.Value))
            || input.EffectiveFrom < center.EffectiveFrom
            || (center.EffectiveTo.HasValue
                && (!input.EffectiveTo.HasValue || input.EffectiveTo.Value > center.EffectiveTo.Value)))
            throw new InvalidOperationException("TEAM_CENTER_ORGANIZATION_MISMATCH");

        var query = db.TeamCenterAssignments.Where(x => x.TeamId == team.TeamId);
        if (await query.AnyAsync(x =>
                (!id.HasValue || x.TeamCenterAssignmentId != id.Value)
                && x.EffectiveFrom <= (input.EffectiveTo ?? DateOnly.MaxValue)
                && (!x.EffectiveTo.HasValue || input.EffectiveFrom <= x.EffectiveTo.Value),
                ct))
            throw new InvalidOperationException("OVERLAPPING_TEAM_CENTER");

        TeamCenterAssignment row;
        if (id.HasValue)
        {
            row = await query.SingleOrDefaultAsync(
                x => x.TeamCenterAssignmentId == id.Value,
                ct) ?? throw new InvalidOperationException("UNKNOWN_TEAM_CENTER_ASSIGNMENT");
            V180MasterDataValidationService.RowVersion(input.RowVersion, row.RowVersion);
        }
        else
        {
            row = new TeamCenterAssignment
            {
                TeamId = team.TeamId,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = admin.UserId
            };
            db.TeamCenterAssignments.Add(row);
        }

        var dependentSites = await (
            from assignment in db.TeamDeploymentSiteAssignments
            join site in db.DeploymentSites on assignment.DeploymentSiteId equals site.DeploymentSiteId
            where assignment.TeamId == team.TeamId
            select new
            {
                site.CenterId,
                assignment.EffectiveFrom,
                assignment.EffectiveTo
            }).ToListAsync(ct);

        var otherCoverage = await query
            .Where(x => !id.HasValue || x.TeamCenterAssignmentId != id.Value)
            .Select(x => new
            {
                x.CenterId,
                x.EffectiveFrom,
                x.EffectiveTo
            })
            .ToListAsync(ct);

        foreach (var dependent in dependentSites)
        {
            var coveredByProposed = dependent.CenterId == center.CenterId
                && V180MasterDataValidationService.Covers(
                    input.EffectiveFrom,
                    input.EffectiveTo,
                    dependent.EffectiveFrom,
                    dependent.EffectiveTo);
            var coveredByOther = otherCoverage.Any(x =>
                x.CenterId == dependent.CenterId
                && V180MasterDataValidationService.Covers(
                    x.EffectiveFrom,
                    x.EffectiveTo,
                    dependent.EffectiveFrom,
                    dependent.EffectiveTo));
            if (!coveredByProposed && !coveredByOther)
                throw new InvalidOperationException("TEAM_CENTER_CHANGE_BREAKS_TEAM_SITE");
        }

        row.CenterId = center.CenterId;
        row.EffectiveFrom = input.EffectiveFrom;
        row.EffectiveTo = input.EffectiveTo;

        AddAudit(
            admin,
            "TeamCenterAssignment",
            $"{team.TeamCode}:{center.CenterCode}:{input.EffectiveFrom:yyyy-MM-dd}",
            id.HasValue ? "Update" : "Create",
            new { team.TeamCode, center.CenterCode, input.EffectiveFrom, input.EffectiveTo });

        await db.SaveChangesAsync(ct);
        return Row(
            row.TeamCenterAssignmentId,
            team.TeamCode,
            center.CenterCode,
            row.ChangeReason,
            row.EffectiveFrom,
            row.EffectiveTo,
            null,
            null,
            row.RowVersion);
    }

    public async Task<V180MasterDataRow> SaveDeploymentSiteAsync(
        CurrentUserDto admin,
        int? id,
        V180DeploymentSiteInput input,
        CancellationToken ct)
    {
        V180MasterDataValidationService.Period(input.EffectiveFrom, input.EffectiveTo);
        return await ExecuteTeamSiteCoverageInvariantAsync(
            () => SaveDeploymentSiteCoreAsync(admin, id, input, ct), ct);
    }

    public async Task<V180LocationOfficialSiteDto> GetLocationOfficialSiteAsync(
        CurrentUserDto admin,
        int locationId,
        CancellationToken ct)
    {
        if (locationId <= 0)
            throw new InvalidOperationException("LOCATION_ID_REQUIRED");

        var org = Org(admin);
        var location = await db.Locations.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.LocationId == locationId && x.OrganizationId == org,
                ct)
            ?? throw new InvalidOperationException("UNKNOWN_LOCATION");

        var rows = await (
            from assignment in db.DeploymentSiteLocationAssignments.AsNoTracking()
            join site in db.DeploymentSites.AsNoTracking()
                on assignment.DeploymentSiteId equals site.DeploymentSiteId
            join center in db.Centers.AsNoTracking()
                on site.CenterId equals center.CenterId
            where assignment.LocationId == locationId
                && center.OrganizationId == org
                && !assignment.EffectiveTo.HasValue
            orderby assignment.EffectiveFrom descending
            select new
            {
                site.DeploymentSiteId,
                site.SiteCode,
                site.SiteName,
                center.CenterCode,
                center.CenterName,
                site.EffectiveFrom,
                site.EffectiveTo,
                site.IsActive
            }).ToListAsync(ct);

        if (rows.Count > 1)
            throw new InvalidOperationException(
                "AMBIGUOUS_LOCATION_OFFICIAL_SITE");

        var current = rows.SingleOrDefault();
        return new V180LocationOfficialSiteDto(
            location.LocationId,
            location.LocationCode ?? string.Empty,
            location.LocationName,
            current is not null,
            current?.DeploymentSiteId,
            current?.SiteCode,
            current?.SiteName,
            current?.CenterCode,
            current?.CenterName,
            current?.EffectiveFrom,
            current?.EffectiveTo,
            current?.IsActive);
    }

    public Task<V180LocationOfficialSiteDto> EnsureLocationOfficialSiteAsync(
        CurrentUserDto admin,
        V180LocationOfficialSiteInput input,
        CancellationToken ct)
        => ExecuteTeamSiteCoverageInvariantAsync(
            () => EnsureLocationOfficialSiteCoreAsync(admin, input, ct),
            ct);

    private async Task<V180LocationOfficialSiteDto> EnsureLocationOfficialSiteCoreAsync(
        CurrentUserDto admin,
        V180LocationOfficialSiteInput input,
        CancellationToken ct)
    {
        if (input.LocationId <= 0)
            throw new InvalidOperationException("LOCATION_ID_REQUIRED");

        var existing = await GetLocationOfficialSiteAsync(
            admin,
            input.LocationId,
            ct);

        var requestedCenterCode = Required(
            input.CenterCode,
            "CENTER_CODE_REQUIRED");

        var requestedSiteName = string.IsNullOrWhiteSpace(input.SiteName)
            ? existing.LocationName
            : input.SiteName.Trim();

        if (requestedSiteName.Length > 200)
            throw new InvalidOperationException("SITE_NAME_TOO_LONG");

        if (existing.IsOfficialSite)
        {
            if (string.Equals(
                    existing.CenterCode,
                    requestedCenterCode,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    existing.SiteName,
                    requestedSiteName,
                    StringComparison.Ordinal))
                return existing;

            throw new InvalidOperationException(
                "LOCATION_OFFICIAL_SITE_ALREADY_EXISTS_USE_ADVANCED_MAINTENANCE");
        }

        var org = Org(admin);
        var location = await db.Locations.SingleOrDefaultAsync(
            x => x.LocationId == input.LocationId
                && x.OrganizationId == org,
            ct) ?? throw new InvalidOperationException("UNKNOWN_LOCATION");

        if (location.IsTemporary)
            throw new InvalidOperationException(
                "LOCATION_MUST_BE_FORMAL");
        if (location.ApprovalStatus != "Approved")
            throw new InvalidOperationException(
                "LOCATION_NOT_APPROVED");
        if (!location.IsActive)
            throw new InvalidOperationException(
                "LOCATION_NOT_ACTIVE");
        if (string.IsNullOrWhiteSpace(location.LocationCode))
            throw new InvalidOperationException(
                "LOCATION_CODE_REQUIRED");

        var hasOfficialSiteHistory = await (
            from assignment in db.DeploymentSiteLocationAssignments
            join site in db.DeploymentSites
                on assignment.DeploymentSiteId equals site.DeploymentSiteId
            join historyCenter in db.Centers
                on site.CenterId equals historyCenter.CenterId
            where assignment.LocationId == location.LocationId
                && historyCenter.OrganizationId == org
            select assignment).AnyAsync(ct);
        if (hasOfficialSiteHistory)
            throw new InvalidOperationException(
                "LOCATION_HAS_OFFICIAL_SITE_HISTORY_USE_ADVANCED_MAINTENANCE");

        var center = await Center(
            admin,
            requestedCenterCode,
            ct);
        if (!center.IsActive)
            throw new InvalidOperationException(
                "CENTER_NOT_ACTIVE");
        if (input.EffectiveFrom < center.EffectiveFrom
            || (center.EffectiveTo.HasValue
                && input.EffectiveFrom > center.EffectiveTo.Value))
            throw new InvalidOperationException(
                "OFFICIAL_SITE_START_OUTSIDE_CENTER_PERIOD");

        var siteCode = $"AUTO-S-{location.LocationId}";
        var siteCodeCollision = await (
            from site in db.DeploymentSites
            join c in db.Centers on site.CenterId equals c.CenterId
            where c.OrganizationId == org
                && site.SiteCode == siteCode
            select site).AnyAsync(ct);
        if (siteCodeCollision)
            throw new InvalidOperationException(
                "AUTO_SITE_CODE_COLLISION");

        var saved = await SaveDeploymentSiteCoreAsync(
            admin,
            null,
            new V180DeploymentSiteInput(
                center.CenterCode,
                siteCode,
                requestedSiteName,
                location.LocationCode,
                input.EffectiveFrom,
                center.EffectiveTo,
                true),
            ct);

        return new V180LocationOfficialSiteDto(
            location.LocationId,
            location.LocationCode,
            location.LocationName,
            true,
            checked((int)saved.Id),
            saved.Key,
            requestedSiteName,
            center.CenterCode,
            center.CenterName,
            input.EffectiveFrom,
            center.EffectiveTo,
            true);
    }

    private async Task<V180MasterDataRow> SaveDeploymentSiteCoreAsync(
        CurrentUserDto admin,
        int? id,
        V180DeploymentSiteInput input,
        CancellationToken ct)
    {
        V180MasterDataValidationService.Period(input.EffectiveFrom, input.EffectiveTo);
        var org = Org(admin);
        var center = await Center(admin, input.CenterCode, ct);
        if (!V180MasterDataValidationService.Covers(
                center.EffectiveFrom,
                center.EffectiveTo,
                input.EffectiveFrom,
                input.EffectiveTo))
            throw new InvalidOperationException("SITE_OUTSIDE_CENTER_PERIOD");

        var location = await Location(admin, input.LocationCode, ct);
        if (location.ApprovalStatus != "Approved")
            throw new InvalidOperationException("LOCATION_NOT_APPROVED");
        if (!location.IsActive)
            throw new InvalidOperationException("LOCATION_NOT_ACTIVE");

        var siteCode = Required(input.SiteCode, "SITE_CODE_REQUIRED");
        var siteName = Required(input.SiteName, "SITE_NAME_REQUIRED");

        if (await (
                from s in db.DeploymentSites
                join c in db.Centers on s.CenterId equals c.CenterId
                where c.OrganizationId == org
                    && s.SiteCode == siteCode
                    && (!id.HasValue || s.DeploymentSiteId != id.Value)
                select s).AnyAsync(ct))
            throw new InvalidOperationException("DUPLICATE_SITE_CODE");

        DeploymentSite row;
        if (id.HasValue)
        {
            row = await (
                from s in db.DeploymentSites
                join c in db.Centers on s.CenterId equals c.CenterId
                where s.DeploymentSiteId == id.Value && c.OrganizationId == org
                select s).SingleOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("UNKNOWN_SITE_CODE");
            V180MasterDataValidationService.RowVersion(input.RowVersion, row.RowVersion);

            if (row.CenterId != center.CenterId
                && await db.TeamDeploymentSiteAssignments.AnyAsync(
                    x => x.DeploymentSiteId == row.DeploymentSiteId,
                    ct))
                throw new InvalidOperationException("DEPLOYMENT_SITE_CENTER_CHANGE_HAS_DEPENDENCIES");
            if (row.CenterId != center.CenterId
                && await db.EmploymentDeploymentSiteAssignments.AnyAsync(
                    x => x.DeploymentSiteId == row.DeploymentSiteId,
                    ct))
                throw new InvalidOperationException("DEPLOYMENT_SITE_CENTER_CHANGE_HAS_DEPENDENCIES");

            var breaksTeamSite = await db.TeamDeploymentSiteAssignments.AnyAsync(x =>
                x.DeploymentSiteId == row.DeploymentSiteId
                && (x.EffectiveFrom < input.EffectiveFrom
                    || (input.EffectiveTo.HasValue
                        && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value > input.EffectiveTo.Value))),
                ct);
            var breaksEmploymentSite = await db.EmploymentDeploymentSiteAssignments.AnyAsync(x =>
                x.DeploymentSiteId == row.DeploymentSiteId
                && (x.EffectiveFrom < input.EffectiveFrom
                    || (input.EffectiveTo.HasValue
                        && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value > input.EffectiveTo.Value))),
                ct);
            if (breaksTeamSite || breaksEmploymentSite)
                throw new InvalidOperationException("DEPLOYMENT_SITE_PERIOD_HAS_DEPENDENCIES");

            var hasRequestedLocation = await db.DeploymentSiteLocationAssignments.AnyAsync(
                x => x.DeploymentSiteId == row.DeploymentSiteId
                    && x.LocationId == location.LocationId,
                ct);
            if (!hasRequestedLocation)
                throw new InvalidOperationException("DEPLOYMENT_SITE_LOCATION_CHANGE_REQUIRES_RELOCATION_FLOW");

            var hasRequestedLocationCoverage = await db.DeploymentSiteLocationAssignments.AnyAsync(
                x => x.DeploymentSiteId == row.DeploymentSiteId
                    && x.LocationId == location.LocationId
                    && x.EffectiveFrom <= input.EffectiveFrom
                    && (!input.EffectiveTo.HasValue
                        ? !x.EffectiveTo.HasValue
                        : !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= input.EffectiveTo.Value),
                ct);
            if (!hasRequestedLocationCoverage)
                throw new InvalidOperationException("DEPLOYMENT_SITE_LOCATION_COVERAGE_REQUIRED");
        }
        else
        {
            row = new DeploymentSite
            {
                CenterId = center.CenterId,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = admin.UserId
            };
            db.DeploymentSites.Add(row);
        }

        row.CenterId = center.CenterId;
        row.SiteCode = siteCode;
        row.SiteName = siteName;
        row.EffectiveFrom = input.EffectiveFrom;
        row.EffectiveTo = input.EffectiveTo;
        row.IsActive = input.IsActive;
        row.UpdatedAt = id.HasValue ? DateTime.UtcNow : null;
        row.UpdatedByUserId = id.HasValue ? admin.UserId : null;
        if (!input.IsActive && id.HasValue)
        {
            row.InactivatedAt ??= DateTime.UtcNow;
            row.InactivatedByUserId ??= admin.UserId;
        }

        if (!id.HasValue)
        {
            db.DeploymentSiteLocationAssignments.Add(
                new DeploymentSiteLocationAssignment
                {
                    DeploymentSite = row,
                    LocationId = location.LocationId,
                    EffectiveFrom = input.EffectiveFrom,
                    EffectiveTo = input.EffectiveTo,
                    ChangeReason = "UAT Business Admin initial assignment",
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = admin.UserId
                });
        }

        AddAudit(
            admin,
            "DeploymentSite",
            siteCode,
            id.HasValue ? "Update" : "Create",
            new
            {
                center.CenterCode,
                siteCode,
                siteName,
                location.LocationCode,
                input.EffectiveFrom,
                input.EffectiveTo,
                input.IsActive
            });

        await db.SaveChangesAsync(ct);
        return Row(
            row.DeploymentSiteId,
            row.SiteCode,
            center.CenterCode,
            row.SiteName,
            row.EffectiveFrom,
            row.EffectiveTo,
            row.IsActive,
            null,
            row.RowVersion);
    }

    public async Task<V180MasterDataRow> SaveTeamSiteAsync(
        CurrentUserDto admin,
        long? id,
        V180TeamSiteInput input,
        CancellationToken ct)
    {
        V180MasterDataValidationService.Period(input.EffectiveFrom, input.EffectiveTo);
        return await ExecuteTeamSiteCoverageInvariantAsync(
            () => SaveTeamSiteCoreAsync(admin, id, input, ct), ct);
    }

    private async Task<V180MasterDataRow> SaveTeamSiteCoreAsync(
        CurrentUserDto admin,
        long? id,
        V180TeamSiteInput input,
        CancellationToken ct)
    {
        V180MasterDataValidationService.Period(input.EffectiveFrom, input.EffectiveTo);
        var team = await Team(admin, input.TeamCode, ct);
        var site = await Site(admin, input.SiteCode, ct);

        if (!site.IsActive
            || !V180MasterDataValidationService.Covers(
                site.EffectiveFrom,
                site.EffectiveTo,
                input.EffectiveFrom,
                input.EffectiveTo))
            throw new InvalidOperationException("TEAM_SITE_OUTSIDE_SITE_PERIOD");

        var centerCovered = await db.TeamCenterAssignments.AnyAsync(x =>
            x.TeamId == team.TeamId
            && x.CenterId == site.CenterId
            && x.EffectiveFrom <= input.EffectiveFrom
            && (!input.EffectiveTo.HasValue
                ? !x.EffectiveTo.HasValue
                : !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= input.EffectiveTo.Value),
            ct);
        if (!centerCovered)
            throw new InvalidOperationException("TEAM_SITE_WITHOUT_TEAM_CENTER_COVERAGE");

        var query = db.TeamDeploymentSiteAssignments
            .Where(x => x.TeamId == team.TeamId);
        if (await query.AnyAsync(x =>
                x.DeploymentSiteId == site.DeploymentSiteId
                && (!id.HasValue || x.TeamDeploymentSiteAssignmentId != id.Value)
                && x.EffectiveFrom <= (input.EffectiveTo ?? DateOnly.MaxValue)
                && (!x.EffectiveTo.HasValue || input.EffectiveFrom <= x.EffectiveTo.Value),
                ct))
            throw new InvalidOperationException("OVERLAPPING_TEAM_SITE");

        TeamDeploymentSiteAssignment row;
        if (id.HasValue)
        {
            row = await query.SingleOrDefaultAsync(
                x => x.TeamDeploymentSiteAssignmentId == id.Value,
                ct) ?? throw new InvalidOperationException("UNKNOWN_TEAM_SITE_ASSIGNMENT");
            V180MasterDataValidationService.RowVersion(input.RowVersion, row.RowVersion);

            var changedMaterially = row.DeploymentSiteId != site.DeploymentSiteId
                || row.EffectiveFrom != input.EffectiveFrom
                || row.EffectiveTo != input.EffectiveTo;
            if (changedMaterially)
            {
                var oldSiteId = row.DeploymentSiteId;
                var hasDependentEmployment = await (
                    from es in db.EmploymentDeploymentSiteAssignments
                    join m in db.TeamMemberships on es.EmploymentId equals m.EmploymentId
                    where es.DeploymentSiteId == oldSiteId
                        && m.TeamId == team.TeamId
                        && m.EffectiveFrom <= (es.EffectiveTo ?? DateOnly.MaxValue)
                        && (!m.EffectiveTo.HasValue || es.EffectiveFrom <= m.EffectiveTo.Value)
                    select es).AnyAsync(ct);
                if (hasDependentEmployment)
                    throw new InvalidOperationException("TEAM_SITE_CHANGE_HAS_DEPENDENCIES");
            }
        }
        else
        {
            row = new TeamDeploymentSiteAssignment
            {
                TeamId = team.TeamId,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = admin.UserId
            };
            db.TeamDeploymentSiteAssignments.Add(row);
        }

        row.DeploymentSiteId = site.DeploymentSiteId;
        row.EffectiveFrom = input.EffectiveFrom;
        row.EffectiveTo = input.EffectiveTo;

        AddAudit(
            admin,
            "TeamDeploymentSiteAssignment",
            $"{team.TeamCode}:{site.SiteCode}:{input.EffectiveFrom:yyyy-MM-dd}",
            id.HasValue ? "Update" : "Create",
            new { team.TeamCode, site.SiteCode, input.EffectiveFrom, input.EffectiveTo });

        await db.SaveChangesAsync(ct);
        return Row(
            row.TeamDeploymentSiteAssignmentId,
            team.TeamCode,
            site.SiteCode,
            null,
            row.EffectiveFrom,
            row.EffectiveTo,
            null,
            null,
            row.RowVersion);
    }

    public async Task<V180MasterDataRow> SaveEmploymentSiteAsync(
        CurrentUserDto admin,
        long? id,
        V180EmploymentSiteInput input,
        CancellationToken ct)
    {
        V180MasterDataValidationService.Period(input.EffectiveFrom, input.EffectiveTo);
        var employment = await Employment(admin, input.EmployeeNo, ct);

        return await ExecuteEmploymentSiteInvariantsAsync(
            employment.EmploymentId,
            () => SaveEmploymentSiteCoreAsync(
                admin,
                employment.EmploymentId,
                employment.EmployeeNo ?? string.Empty,
                id,
                input,
                ct),
            ct);
    }

    private async Task<V180MasterDataRow> SaveEmploymentSiteCoreAsync(
        CurrentUserDto admin,
        long employmentId,
        string employeeNo,
        long? id,
        V180EmploymentSiteInput input,
        CancellationToken ct)
    {
        var site = await Site(admin, input.SiteCode, ct);

        if (!site.IsActive
            || !V180MasterDataValidationService.Covers(
                site.EffectiveFrom,
                site.EffectiveTo,
                input.EffectiveFrom,
                input.EffectiveTo))
            throw new InvalidOperationException("EMPLOYMENT_SITE_OUTSIDE_SITE_PERIOD");

        var hasLocationCoverage = await db.DeploymentSiteLocationAssignments.AnyAsync(x =>
            x.DeploymentSiteId == site.DeploymentSiteId
            && x.EffectiveFrom <= input.EffectiveFrom
            && (!input.EffectiveTo.HasValue
                ? !x.EffectiveTo.HasValue
                : !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= input.EffectiveTo.Value)
            && db.Locations.Any(l => l.LocationId == x.LocationId
                && l.OrganizationId == Org(admin)
                && l.IsActive
                && l.ApprovalStatus == "Approved"),
            ct);
        if (!hasLocationCoverage)
            throw new InvalidOperationException("EMPLOYMENT_SITE_WITHOUT_EFFECTIVE_LOCATION");

        var hasActiveEmployment = await db.EmploymentStatusPeriods.AnyAsync(x =>
            x.EmploymentId == employmentId
            && x.EmploymentStatus == EmploymentStatuses.Active
            && x.EffectiveFrom <= input.EffectiveFrom
            && (!input.EffectiveTo.HasValue
                ? !x.EffectiveTo.HasValue
                : !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= input.EffectiveTo.Value),
            ct);
        if (!hasActiveEmployment)
            throw new InvalidOperationException("EMPLOYMENT_SITE_WITHOUT_ACTIVE_EMPLOYMENT");

        var membershipTeamIds = await db.TeamMemberships
            .Where(x => x.EmploymentId == employmentId
                && x.EffectiveFrom <= input.EffectiveFrom
                && (!input.EffectiveTo.HasValue
                    ? !x.EffectiveTo.HasValue
                    : !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= input.EffectiveTo.Value))
            .Select(x => x.TeamId)
            .Distinct()
            .ToListAsync(ct);
        if (membershipTeamIds.Count == 0)
            throw new InvalidOperationException("EMPLOYMENT_SITE_WITHOUT_TEAM_MEMBERSHIP");

        var hasTeamSiteCoverage = await db.TeamDeploymentSiteAssignments.AnyAsync(x =>
            membershipTeamIds.Contains(x.TeamId)
            && x.DeploymentSiteId == site.DeploymentSiteId
            && x.EffectiveFrom <= input.EffectiveFrom
            && (!input.EffectiveTo.HasValue
                ? !x.EffectiveTo.HasValue
                : !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= input.EffectiveTo.Value),
            ct);
        if (!hasTeamSiteCoverage)
            throw new InvalidOperationException("EMPLOYMENT_SITE_WITHOUT_TEAM_SITE_COVERAGE");

        var query = db.EmploymentDeploymentSiteAssignments
            .Where(x => x.EmploymentId == employmentId);

        if (await query.AnyAsync(x =>
                x.DeploymentSiteId == site.DeploymentSiteId
                && (!id.HasValue || x.EmploymentDeploymentSiteAssignmentId != id.Value)
                && x.EffectiveFrom <= (input.EffectiveTo ?? DateOnly.MaxValue)
                && (!x.EffectiveTo.HasValue || input.EffectiveFrom <= x.EffectiveTo.Value),
                ct))
            throw new InvalidOperationException("OVERLAPPING_EMPLOYMENT_SITE");

        if (input.IsPrimary && await query.AnyAsync(x =>
                x.IsPrimary
                && (!id.HasValue || x.EmploymentDeploymentSiteAssignmentId != id.Value)
                && x.EffectiveFrom <= (input.EffectiveTo ?? DateOnly.MaxValue)
                && (!x.EffectiveTo.HasValue || input.EffectiveFrom <= x.EffectiveTo.Value),
                ct))
            throw new InvalidOperationException("MULTIPLE_PRIMARY_EMPLOYMENT_SITE");

        EmploymentDeploymentSiteAssignment row;
        if (id.HasValue)
        {
            row = await query.SingleOrDefaultAsync(
                x => x.EmploymentDeploymentSiteAssignmentId == id.Value,
                ct) ?? throw new InvalidOperationException("UNKNOWN_EMPLOYMENT_SITE_ASSIGNMENT");
            V180MasterDataValidationService.RowVersion(input.RowVersion, row.RowVersion);
        }
        else
        {
            row = new EmploymentDeploymentSiteAssignment
            {
                EmploymentId = employmentId,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = admin.UserId
            };
            db.EmploymentDeploymentSiteAssignments.Add(row);
        }

        row.DeploymentSiteId = site.DeploymentSiteId;
        row.IsPrimary = input.IsPrimary;
        row.EffectiveFrom = input.EffectiveFrom;
        row.EffectiveTo = input.EffectiveTo;

        AddAudit(
            admin,
            "EmploymentDeploymentSiteAssignment",
            $"{employeeNo}:{site.SiteCode}:{input.EffectiveFrom:yyyy-MM-dd}",
            id.HasValue ? "Update" : "Create",
            new
            {
                employeeNo,
                site.SiteCode,
                input.IsPrimary,
                input.EffectiveFrom,
                input.EffectiveTo
            });

        await db.SaveChangesAsync(ct);
        return Row(
            row.EmploymentDeploymentSiteAssignmentId,
            employeeNo,
            site.SiteCode,
            null,
            row.EffectiveFrom,
            row.EffectiveTo,
            null,
            row.IsPrimary,
            row.RowVersion);
    }

    private async Task<T> ExecuteEmploymentStatusSiteInvariantAsync<T>(
        long employmentId,
        Func<Task<T>> operation,
        CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return await operation();

        if (db.Database.CurrentTransaction is not null)
        {
            await AcquireEmploymentStatusSiteInvariantLockAsync(employmentId, ct);
            return await operation();
        }

        var strategy = db.Database.CreateExecutionStrategy();
        T? result = default;
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await AcquireEmploymentStatusSiteInvariantLockAsync(employmentId, ct);
            result = await operation();
            await transaction.CommitAsync(ct);
        });
        return result!;
    }

    private async Task<T> ExecuteTeamSiteCoverageInvariantAsync<T>(
        Func<Task<T>> operation,
        CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return await operation();

        if (db.Database.CurrentTransaction is not null)
        {
            await AcquireTeamSiteCoverageInvariantLockAsync(ct);
            return await operation();
        }

        var strategy = db.Database.CreateExecutionStrategy();
        T? result = default;
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await AcquireTeamSiteCoverageInvariantLockAsync(ct);
            result = await operation();
            await transaction.CommitAsync(ct);
        });
        return result!;
    }

    private async Task<T> ExecuteEmploymentSiteInvariantsAsync<T>(
        long employmentId,
        Func<Task<T>> operation,
        CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return await operation();

        if (db.Database.CurrentTransaction is not null)
        {
            await AcquireTeamSiteCoverageInvariantLockAsync(ct);
            await AcquireEmploymentStatusSiteInvariantLockAsync(employmentId, ct);
            return await operation();
        }

        var strategy = db.Database.CreateExecutionStrategy();
        T? result = default;
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await AcquireTeamSiteCoverageInvariantLockAsync(ct);
            await AcquireEmploymentStatusSiteInvariantLockAsync(employmentId, ct);
            result = await operation();
            await transaction.CommitAsync(ct);
        });
        return result!;
    }

    private async Task AcquireTeamSiteCoverageInvariantLockAsync(CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = 'FieldVisit.TeamSiteCoverageInvariant',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 10000;
            SELECT @result;
            """;
        var lockResult = await command.ExecuteScalarAsync(ct);
        if (lockResult is null || Convert.ToInt32(lockResult) < 0)
            throw new InvalidOperationException(
                "TEAM_SITE_COVERAGE_INVARIANT_LOCK_FAILED");
    }

    private async Task AcquireEmploymentStatusSiteInvariantLockAsync(
        long employmentId,
        CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 10000;
            SELECT @result;
            """;
        var resource = command.CreateParameter();
        resource.ParameterName = "@resource";
        resource.Value = $"FieldVisit.E1.EmploymentStatusSite:{employmentId}";
        command.Parameters.Add(resource);

        var lockResult = await command.ExecuteScalarAsync(ct);
        if (lockResult is null || Convert.ToInt32(lockResult) < 0)
            throw new InvalidOperationException(
                "EMPLOYMENT_STATUS_SITE_INVARIANT_LOCK_FAILED");
    }

    private async Task<Employment> Employment(
        CurrentUserDto admin,
        string employeeNo,
        CancellationToken ct)
    {
        var key = Required(employeeNo, "EMPLOYEE_NO_REQUIRED");
        return await db.Employments.SingleOrDefaultAsync(
            x => x.OrganizationId == Org(admin) && x.EmployeeNo == key,
            ct) ?? throw new InvalidOperationException("UNKNOWN_EMPLOYEE_NO");
    }

    private async Task<Team> Team(
        CurrentUserDto admin,
        string teamCode,
        CancellationToken ct)
    {
        var key = Required(teamCode, "TEAM_CODE_REQUIRED");
        return await db.Teams.SingleOrDefaultAsync(
            x => x.OrganizationId == Org(admin) && x.TeamCode == key,
            ct) ?? throw new InvalidOperationException("UNKNOWN_TEAM_CODE");
    }

    private async Task<Center> Center(
        CurrentUserDto admin,
        string centerCode,
        CancellationToken ct)
    {
        var key = Required(centerCode, "CENTER_CODE_REQUIRED");
        return await db.Centers.SingleOrDefaultAsync(
            x => x.OrganizationId == Org(admin) && x.CenterCode == key,
            ct) ?? throw new InvalidOperationException("UNKNOWN_CENTER_CODE");
    }

    private async Task<DeploymentSite> Site(
        CurrentUserDto admin,
        string siteCode,
        CancellationToken ct)
    {
        var key = Required(siteCode, "SITE_CODE_REQUIRED");
        var rows = await (
            from site in db.DeploymentSites
            join center in db.Centers on site.CenterId equals center.CenterId
            where center.OrganizationId == Org(admin) && site.SiteCode == key
            select site).ToListAsync(ct);
        return rows.Count switch
        {
            0 => throw new InvalidOperationException("UNKNOWN_SITE_CODE"),
            1 => rows[0],
            _ => throw new InvalidOperationException("AMBIGUOUS_SITE_CODE")
        };
    }

    private async Task<Location> Location(
        CurrentUserDto admin,
        string locationCode,
        CancellationToken ct)
    {
        var key = Required(locationCode, "LOCATION_CODE_REQUIRED");
        return await db.Locations.SingleOrDefaultAsync(
            x => x.OrganizationId == Org(admin) && x.LocationCode == key,
            ct) ?? throw new InvalidOperationException("UNKNOWN_LOCATION_CODE");
    }

    private void AddAudit(
        CurrentUserDto admin,
        string entityType,
        string entityId,
        string action,
        object newValues)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = admin.UserId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            NewValues = JsonSerializer.Serialize(newValues),
            CreatedAt = DateTime.UtcNow
        });
    }

    private static int Org(CurrentUserDto admin) =>
        admin.OrganizationId
        ?? throw new InvalidOperationException("目前管理者缺少 OrganizationId。");

    private static string Required(string value, string code)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(code);
        return value.Trim();
    }

    private static string? B64(byte[] value) =>
        value.Length == 0 ? null : Convert.ToBase64String(value);

    private static V180MasterDataRow Row(
        long id,
        string key,
        string? parentKey,
        string? detail,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        bool? isActive,
        bool? isPrimary,
        byte[] rowVersion) =>
        new(
            id,
            key,
            parentKey,
            detail,
            effectiveFrom,
            effectiveTo,
            isActive,
            isPrimary,
            B64(rowVersion));
}
