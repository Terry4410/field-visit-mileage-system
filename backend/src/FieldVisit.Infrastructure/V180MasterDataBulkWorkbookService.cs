using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// v1.8 master-data import deliberately stops at preview staging.  It does not
/// share the v1.6 row-by-row confirm implementation, because E2-B owns the
/// atomic confirm boundary.
/// </summary>
public sealed class V180MasterDataBulkWorkbookService(AppDbContext db) : IV180MasterDataBulkWorkbookService
{
    private const string ImportType = "v180-master-data";
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SheetSpec[] Specs =
    [
        new("EmploymentStatus", ["EmployeeNo", "Status", "EffectiveFrom", "EffectiveTo"]),
        new("Centers", ["CenterCode", "CenterName", "EffectiveFrom", "EffectiveTo", "IsActive"]),
        new("TeamCenters", ["TeamCode", "CenterCode", "EffectiveFrom", "EffectiveTo"]),
        new("DeploymentSites", ["CenterCode", "SiteCode", "SiteName", "LocationCode", "EffectiveFrom", "EffectiveTo", "IsActive"]),
        new("TeamSites", ["TeamCode", "SiteCode", "EffectiveFrom", "EffectiveTo"]),
        new("EmploymentSites", ["EmployeeNo", "SiteCode", "IsPrimary", "EffectiveFrom", "EffectiveTo"])
    ];

    public Task<ReportExportContext> CreateTemplateAsync(CurrentUserDto admin, CancellationToken ct)
    {
        RequireAdmin(admin);
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbook = doc.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var sheets = workbook.Workbook.AppendChild(new Sheets());
            uint id = 1;
            foreach (var spec in Specs)
            {
                var example = Example(spec.Name);
                AddSheet(workbook, sheets, id++, spec.Name, [spec.Headers, example]);
            }
            workbook.Workbook.Save();
        }
        return Task.FromResult(new ReportExportContext("v180-master-data-template.xlsx", stream.ToArray(), ExcelContentType));
    }

    public async Task<ImportPreviewDto> PreviewAsync(CurrentUserDto admin, byte[] content, CancellationToken ct)
    {
        var orgId = RequireAdmin(admin);
        if (content.Length == 0) throw new InvalidOperationException("BULK_WORKBOOK_REQUIRED");
        if (content.Length > 10 * 1024 * 1024) throw new InvalidOperationException("BULK_WORKBOOK_TOO_LARGE");

        Dictionary<string, List<WorkbookRow>> workbook;
        try { workbook = ParseWorkbook(content); }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) { throw new InvalidOperationException("BULK_XLSX_INVALID", ex); }

        var batch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(), ImportType = ImportType, OrganizationId = orgId,
            RequestedByUserId = admin.UserId, Status = "Previewed", CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(4)
        };
        var staged = new List<StagedRow>();
        var projection = new Projection(orgId);

        // This order is deliberate: only earlier valid rows enter projection.
        foreach (var spec in Specs)
        {
            foreach (var row in workbook[spec.Name])
            {
                var item = await ValidateAsync(spec.Name, row, projection, ct);
                staged.Add(item);
                if (item.Status == "Valid") projection.Add(item);
            }
        }
        if (staged.Count == 0) throw new InvalidOperationException("BULK_WORKBOOK_EMPTY");

        batch.TotalCount = staged.Count;
        batch.ValidCount = staged.Count(x => x.Status == "Valid");
        batch.ErrorCount = staged.Count - batch.ValidCount;
        db.ImportBatches.Add(batch);
        foreach (var item in staged)
        {
            db.ImportBatchItems.Add(new ImportBatchItem
            {
                ImportBatchId = batch.ImportBatchId, RowNumber = item.RowNumber, EntityType = item.EntityType,
                Action = item.Action, Status = item.Status, DisplayKey = item.DisplayKey,
                DataJson = JsonSerializer.Serialize(item.Data, JsonOptions), ErrorMessage = item.ErrorMessage,
                CreatedAt = DateTime.UtcNow
            });
        }
        db.AuditLogs.Add(new AuditLog
        {
            UserId = admin.UserId, EntityType = "ImportBatch", EntityId = batch.ImportBatchId.ToString(),
            Action = "V180MasterDataBulkPreview",
            NewValues = JsonSerializer.Serialize(new { batch.TotalCount, batch.ValidCount, batch.ErrorCount }),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return new ImportPreviewDto(batch.ImportBatchId, ImportType, batch.TotalCount, batch.ValidCount, batch.ErrorCount,
            staged.Select(x => new ImportPreviewItemDto(x.RowNumber, x.EntityType, x.Action, x.Status, x.DisplayKey, x.ErrorMessage)).ToList());
    }

    private async Task<StagedRow> ValidateAsync(string sheet, WorkbookRow row, Projection p, CancellationToken ct)
    {
        try
        {
            return sheet switch
            {
                "EmploymentStatus" => await EmploymentStatusAsync(row, p, ct),
                "Centers" => await CenterAsync(row, p, ct),
                "TeamCenters" => await TeamCenterAsync(row, p, ct),
                "DeploymentSites" => await DeploymentSiteAsync(row, p, ct),
                "TeamSites" => await TeamSiteAsync(row, p, ct),
                "EmploymentSites" => await EmploymentSiteAsync(row, p, ct),
                _ => Error(row, sheet, "NoChange", "", "BULK_SHEET_INVALID")
            };
        }
        catch (InvalidOperationException ex) { return Error(row, EntityType(sheet), "NoChange", DisplayKey(sheet, row), ex.Message); }
    }

    private async Task<StagedRow> EmploymentStatusAsync(WorkbookRow row, Projection p, CancellationToken ct)
    {
        var employeeNo = Required(row, "EmployeeNo", "EMPLOYEE_NO_REQUIRED");
        var status = Required(row, "Status", "EMPLOYMENT_STATUS_REQUIRED");
        V180MasterDataValidationService.Status(status);
        var from = Date(row, "EffectiveFrom", true); var to = Date(row, "EffectiveTo", false);
        V180MasterDataValidationService.Period(from!.Value, to);
        var employment = await db.Employments.SingleOrDefaultAsync(x => x.OrganizationId == p.OrgId && x.EmployeeNo == employeeNo, ct)
            ?? throw new InvalidOperationException("UNKNOWN_EMPLOYEE_NO");
        var existing = await db.EmploymentStatusPeriods.SingleOrDefaultAsync(x => x.EmploymentId == employment.EmploymentId && x.EffectiveFrom == from, ct);
        var existingStatusId = existing?.EmploymentStatusPeriodId ?? 0;
        if ((await db.EmploymentStatusPeriods.Where(x => x.EmploymentId == employment.EmploymentId && x.EmploymentStatusPeriodId != existingStatusId).ToListAsync(ct)).Any(x => !p.Shadows("EmploymentStatus", x.EmploymentStatusPeriodId) && V180MasterDataValidationService.Overlaps(from.Value, to, x.EffectiveFrom, x.EffectiveTo))
            || p.Any("EmploymentStatus", x => x.Values["EmployeeNo"]!.Equals(employeeNo, StringComparison.OrdinalIgnoreCase) && Overlap(from.Value, to, x)))
            throw new InvalidOperationException("OVERLAPPING_EMPLOYMENT_STATUS");
        var action = existing is null ? "Create" : Same(existing.EmploymentStatus, status) && existing.EffectiveTo == to ? "NoChange" : "Update";
        if (existing is not null && action == "Update") await EnsureEmploymentStatusDependenciesAsync(existing, status, from.Value, to, ct);
        return Valid(row, "EmploymentStatus", action, $"{employeeNo}|{from:yyyy-MM-dd}", new { EmployeeNo = employeeNo, Status = status, EffectiveFrom = from, EffectiveTo = to, TargetId = existing?.EmploymentStatusPeriodId, ExpectedRowVersion = Version(existing?.RowVersion) }, existing?.EmploymentStatusPeriodId);
    }

    private async Task<StagedRow> CenterAsync(WorkbookRow row, Projection p, CancellationToken ct)
    {
        var code = Required(row, "CenterCode", "CENTER_CODE_REQUIRED"); var name = Required(row, "CenterName", "CENTER_NAME_REQUIRED");
        var from = Date(row, "EffectiveFrom", true); var to = Date(row, "EffectiveTo", false); var active = Bool(row, "IsActive");
        V180MasterDataValidationService.Period(from!.Value, to);
        var existing = await db.Centers.SingleOrDefaultAsync(x => x.OrganizationId == p.OrgId && x.CenterCode == code, ct);
        var action = existing is null ? "Create" : Same(existing.CenterName, name) && existing.EffectiveFrom == from && existing.EffectiveTo == to && existing.IsActive == active ? "NoChange" : "Update";
        if (existing is not null && action == "Update") await EnsureCenterDependenciesAsync(existing.CenterId, from.Value, to, ct);
        return Valid(row, "Center", action, code, new { CenterCode = code, CenterName = name, EffectiveFrom = from, EffectiveTo = to, IsActive = active, TargetId = existing?.CenterId, ExpectedRowVersion = Version(existing?.RowVersion) }, existing?.CenterId);
    }

    private async Task<StagedRow> TeamCenterAsync(WorkbookRow row, Projection p, CancellationToken ct)
    {
        var teamCode = Required(row, "TeamCode", "TEAM_CODE_REQUIRED"); var centerCode = Required(row, "CenterCode", "CENTER_CODE_REQUIRED");
        var from = Date(row, "EffectiveFrom", true); var to = Date(row, "EffectiveTo", false); V180MasterDataValidationService.Period(from!.Value, to);
        var team = await db.Teams.SingleOrDefaultAsync(x => x.OrganizationId == p.OrgId && x.TeamCode == teamCode, ct) ?? throw new InvalidOperationException("UNKNOWN_TEAM_CODE");
        var center = await ResolveCenterAsync(centerCode, p, ct) ?? throw new InvalidOperationException("UNKNOWN_CENTER_CODE");
        if (!Covers(center.EffectiveFrom, center.EffectiveTo, from.Value, to) || (team.EffectiveFrom.HasValue && !Covers(team.EffectiveFrom.Value, team.EffectiveTo, from.Value, to))) throw new InvalidOperationException("TEAM_CENTER_ORGANIZATION_MISMATCH");
        var existing = await db.TeamCenterAssignments.SingleOrDefaultAsync(x => x.TeamId == team.TeamId && x.EffectiveFrom == from, ct);
        var existingTeamCenterId = existing?.TeamCenterAssignmentId ?? 0;
        if ((await db.TeamCenterAssignments.Where(x => x.TeamId == team.TeamId && x.TeamCenterAssignmentId != existingTeamCenterId).ToListAsync(ct)).Any(x => !p.Shadows("TeamCenter", x.TeamCenterAssignmentId) && V180MasterDataValidationService.Overlaps(from.Value, to, x.EffectiveFrom, x.EffectiveTo))
            || p.Any("TeamCenter", x => Same(x.Values["TeamCode"], teamCode) && Overlap(from.Value, to, x))) throw new InvalidOperationException("OVERLAPPING_TEAM_CENTER");
        var action = existing is null ? "Create" : existing.CenterId == center.TargetId && existing.EffectiveTo == to ? "NoChange" : "Update";
        if (existing is not null && action == "Update") await EnsureTeamCenterDependenciesAsync(existing, center.TargetId, from.Value, to, ct);
        return Valid(row, "TeamCenter", action, $"{teamCode}|{from:yyyy-MM-dd}", new { TeamCode = teamCode, CenterCode = centerCode, EffectiveFrom = from, EffectiveTo = to, TargetId = existing?.TeamCenterAssignmentId, ExpectedRowVersion = Version(existing?.RowVersion) }, existing?.TeamCenterAssignmentId);
    }

    private async Task<StagedRow> DeploymentSiteAsync(WorkbookRow row, Projection p, CancellationToken ct)
    {
        var centerCode = Required(row, "CenterCode", "CENTER_CODE_REQUIRED"); var siteCode = Required(row, "SiteCode", "SITE_CODE_REQUIRED");
        var siteName = Required(row, "SiteName", "SITE_NAME_REQUIRED"); var locationCode = Required(row, "LocationCode", "LOCATION_CODE_REQUIRED");
        var from = Date(row, "EffectiveFrom", true); var to = Date(row, "EffectiveTo", false); var active = Bool(row, "IsActive"); V180MasterDataValidationService.Period(from!.Value, to);
        var center = await ResolveCenterAsync(centerCode, p, ct) ?? throw new InvalidOperationException("UNKNOWN_CENTER_CODE");
        if (!Covers(center.EffectiveFrom, center.EffectiveTo, from.Value, to)) throw new InvalidOperationException("SITE_OUTSIDE_CENTER_PERIOD");
        var location = await db.Locations.SingleOrDefaultAsync(x => x.OrganizationId == p.OrgId && x.LocationCode == locationCode, ct) ?? throw new InvalidOperationException("UNKNOWN_LOCATION_CODE");
        if (!Same(location.ApprovalStatus, "Approved")) throw new InvalidOperationException("LOCATION_NOT_APPROVED");
        if (!location.IsActive) throw new InvalidOperationException("LOCATION_NOT_ACTIVE");
        var sites = await (from s in db.DeploymentSites join c in db.Centers on s.CenterId equals c.CenterId where c.OrganizationId == p.OrgId && s.SiteCode == siteCode select s).ToListAsync(ct);
        if (sites.Count > 1) throw new InvalidOperationException("AMBIGUOUS_SITE_CODE");
        var existing = sites.SingleOrDefault();
        if (existing is not null)
        {
            var hasRequestedLocation = await db.DeploymentSiteLocationAssignments.AnyAsync(
                x => x.DeploymentSiteId == existing.DeploymentSiteId && x.LocationId == location.LocationId,
                ct);
            if (!hasRequestedLocation) throw new InvalidOperationException("DEPLOYMENT_SITE_LOCATION_CHANGE_REQUIRES_RELOCATION_FLOW");

            var hasRequestedLocationCoverage = await db.DeploymentSiteLocationAssignments.AnyAsync(
                x => x.DeploymentSiteId == existing.DeploymentSiteId
                    && x.LocationId == location.LocationId
                    && x.EffectiveFrom <= from.Value
                    && (!to.HasValue ? !x.EffectiveTo.HasValue : !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= to.Value),
                ct);
            if (!hasRequestedLocationCoverage) throw new InvalidOperationException("DEPLOYMENT_SITE_LOCATION_COVERAGE_REQUIRED");
        }
        var action = existing is null ? "Create" : existing.CenterId == center.TargetId && Same(existing.SiteName, siteName) && existing.EffectiveFrom == from && existing.EffectiveTo == to && existing.IsActive == active ? "NoChange" : "Update";
        if (existing is not null && action == "Update") await EnsureDeploymentSiteDependenciesAsync(existing, center.TargetId, from.Value, to, ct);
        return Valid(row, "DeploymentSite", action, siteCode, new { CenterCode = centerCode, SiteCode = siteCode, SiteName = siteName, LocationCode = locationCode, EffectiveFrom = from, EffectiveTo = to, IsActive = active, TargetId = existing?.DeploymentSiteId, ExpectedRowVersion = Version(existing?.RowVersion) }, existing?.DeploymentSiteId);
    }

    private async Task<StagedRow> TeamSiteAsync(WorkbookRow row, Projection p, CancellationToken ct)
    {
        var teamCode = Required(row, "TeamCode", "TEAM_CODE_REQUIRED"); var siteCode = Required(row, "SiteCode", "SITE_CODE_REQUIRED");
        var from = Date(row, "EffectiveFrom", true); var to = Date(row, "EffectiveTo", false); V180MasterDataValidationService.Period(from!.Value, to);
        var team = await db.Teams.SingleOrDefaultAsync(x => x.OrganizationId == p.OrgId && x.TeamCode == teamCode, ct) ?? throw new InvalidOperationException("UNKNOWN_TEAM_CODE");
        var site = await ResolveSiteAsync(siteCode, p, ct) ?? throw new InvalidOperationException("UNKNOWN_SITE_CODE");
        if (!site.IsActive || !Covers(site.EffectiveFrom, site.EffectiveTo, from.Value, to)) throw new InvalidOperationException("TEAM_SITE_OUTSIDE_SITE_PERIOD");
        if (!await HasTeamCenterAsync(team.TeamId, site.CenterCode, site.CenterTargetId, from.Value, to, p, ct)) throw new InvalidOperationException("TEAM_SITE_WITHOUT_TEAM_CENTER_COVERAGE");
        var existing = site.TargetId.HasValue ? await db.TeamDeploymentSiteAssignments.SingleOrDefaultAsync(x => x.TeamId == team.TeamId && x.DeploymentSiteId == site.TargetId.Value && x.EffectiveFrom == from, ct) : null;
        var existingTeamSiteId = existing?.TeamDeploymentSiteAssignmentId ?? 0;
        if (site.TargetId.HasValue && (await db.TeamDeploymentSiteAssignments.Where(x => x.TeamId == team.TeamId && x.DeploymentSiteId == site.TargetId.Value && x.TeamDeploymentSiteAssignmentId != existingTeamSiteId).ToListAsync(ct)).Any(x => !p.Shadows("TeamSite", x.TeamDeploymentSiteAssignmentId) && V180MasterDataValidationService.Overlaps(from.Value, to, x.EffectiveFrom, x.EffectiveTo))
            || p.Any("TeamSite", x => Same(x.Values["TeamCode"], teamCode) && Same(x.Values["SiteCode"], siteCode) && Overlap(from.Value, to, x))) throw new InvalidOperationException("OVERLAPPING_TEAM_SITE");
        var action = existing is null ? "Create" : existing.EffectiveTo == to ? "NoChange" : "Update";
        if (existing is not null && action == "Update") await EnsureTeamSiteDependenciesAsync(existing, site.TargetId, from.Value, to, ct);
        return Valid(row, "TeamSite", action, $"{teamCode}|{siteCode}|{from:yyyy-MM-dd}", new { TeamCode = teamCode, SiteCode = siteCode, EffectiveFrom = from, EffectiveTo = to, TargetId = existing?.TeamDeploymentSiteAssignmentId, ExpectedRowVersion = Version(existing?.RowVersion) }, existing?.TeamDeploymentSiteAssignmentId);
    }

    private async Task<StagedRow> EmploymentSiteAsync(WorkbookRow row, Projection p, CancellationToken ct)
    {
        var employeeNo = Required(row, "EmployeeNo", "EMPLOYEE_NO_REQUIRED"); var siteCode = Required(row, "SiteCode", "SITE_CODE_REQUIRED");
        var primary = Bool(row, "IsPrimary"); var from = Date(row, "EffectiveFrom", true); var to = Date(row, "EffectiveTo", false); V180MasterDataValidationService.Period(from!.Value, to);
        var employment = await db.Employments.SingleOrDefaultAsync(x => x.OrganizationId == p.OrgId && x.EmployeeNo == employeeNo, ct) ?? throw new InvalidOperationException("UNKNOWN_EMPLOYEE_NO");
        var site = await ResolveSiteAsync(siteCode, p, ct) ?? throw new InvalidOperationException("UNKNOWN_SITE_CODE");
        if (!site.IsActive || !Covers(site.EffectiveFrom, site.EffectiveTo, from.Value, to)) throw new InvalidOperationException("EMPLOYMENT_SITE_OUTSIDE_SITE_PERIOD");
        if (!await HasEffectiveSiteLocationAsync(site, from.Value, to, p, ct)) throw new InvalidOperationException("EMPLOYMENT_SITE_WITHOUT_EFFECTIVE_LOCATION");
        if (!await HasActiveStatusAsync(employment.EmploymentId, from.Value, to, p, ct)) throw new InvalidOperationException("EMPLOYMENT_SITE_WITHOUT_ACTIVE_EMPLOYMENT");
        var memberships = (await db.TeamMemberships.Where(x => x.EmploymentId == employment.EmploymentId).ToListAsync(ct)).Where(x => V180MasterDataValidationService.Covers(x.EffectiveFrom, x.EffectiveTo, from.Value, to)).ToList();
        if (memberships.Count == 0) throw new InvalidOperationException("EMPLOYMENT_SITE_WITHOUT_TEAM_MEMBERSHIP");
        var hasTeam = false;
        foreach (var membership in memberships)
            if (await HasTeamSiteAsync(membership.TeamId, site.SiteCode, site.TargetId, from.Value, to, p, ct)) { hasTeam = true; break; }
        if (!hasTeam) throw new InvalidOperationException("EMPLOYMENT_SITE_WITHOUT_TEAM_SITE_COVERAGE");
        var existing = site.TargetId.HasValue ? await db.EmploymentDeploymentSiteAssignments.SingleOrDefaultAsync(x => x.EmploymentId == employment.EmploymentId && x.DeploymentSiteId == site.TargetId.Value && x.EffectiveFrom == from, ct) : null;
        var existingEmploymentSiteId = existing?.EmploymentDeploymentSiteAssignmentId ?? 0;
        if (site.TargetId.HasValue && (await db.EmploymentDeploymentSiteAssignments.Where(x => x.EmploymentId == employment.EmploymentId && x.DeploymentSiteId == site.TargetId.Value && x.EmploymentDeploymentSiteAssignmentId != existingEmploymentSiteId).ToListAsync(ct)).Any(x => !p.Shadows("EmploymentSite", x.EmploymentDeploymentSiteAssignmentId) && V180MasterDataValidationService.Overlaps(from.Value, to, x.EffectiveFrom, x.EffectiveTo))
            || p.Any("EmploymentSite", x => Same(x.Values["EmployeeNo"], employeeNo) && Same(x.Values["SiteCode"], siteCode) && Overlap(from.Value, to, x))) throw new InvalidOperationException("OVERLAPPING_EMPLOYMENT_SITE");
        if (primary && ((await db.EmploymentDeploymentSiteAssignments.Where(x => x.EmploymentId == employment.EmploymentId && x.IsPrimary && x.EmploymentDeploymentSiteAssignmentId != existingEmploymentSiteId).ToListAsync(ct)).Any(x => !p.Shadows("EmploymentSite", x.EmploymentDeploymentSiteAssignmentId) && V180MasterDataValidationService.Overlaps(from.Value, to, x.EffectiveFrom, x.EffectiveTo))
            || p.Any("EmploymentSite", x => Same(x.Values["EmployeeNo"], employeeNo) && IsTrue(x.Values["IsPrimary"]) && Overlap(from.Value, to, x)))) throw new InvalidOperationException("MULTIPLE_PRIMARY_EMPLOYMENT_SITE");
        var action = existing is null ? "Create" : existing.IsPrimary == primary && existing.EffectiveTo == to ? "NoChange" : "Update";
        return Valid(row, "EmploymentSite", action, $"{employeeNo}|{siteCode}|{from:yyyy-MM-dd}", new { EmployeeNo = employeeNo, SiteCode = siteCode, IsPrimary = primary, EffectiveFrom = from, EffectiveTo = to, TargetId = existing?.EmploymentDeploymentSiteAssignmentId, ExpectedRowVersion = Version(existing?.RowVersion) }, existing?.EmploymentDeploymentSiteAssignmentId);
    }

    private async Task<ProjectedCenter?> ResolveCenterAsync(string code, Projection p, CancellationToken ct)
    {
        var staged = p.First("Center", "CenterCode", code); if (staged is not null) return new(code, IntTargetId(staged.TargetId), Date(staged.Values, "EffectiveFrom")!.Value, Date(staged.Values, "EffectiveTo"));
        var row = await db.Centers.SingleOrDefaultAsync(x => x.OrganizationId == p.OrgId && x.CenterCode == code, ct);
        return row is null ? null : new(row.CenterCode, row.CenterId, row.EffectiveFrom, row.EffectiveTo);
    }

    private async Task<ProjectedSite?> ResolveSiteAsync(string code, Projection p, CancellationToken ct)
    {
        var staged = p.First("DeploymentSite", "SiteCode", code);
        if (staged is not null)
        {
            var center = await ResolveCenterAsync(staged.Values["CenterCode"]!, p, ct); if (center is null) return null;
            return new(code, IntTargetId(staged.TargetId), staged.Values["CenterCode"]!, center.TargetId, Blank(staged.Values["LocationCode"]), Date(staged.Values, "EffectiveFrom")!.Value, Date(staged.Values, "EffectiveTo"), IsTrue(staged.Values["IsActive"]));
        }
        var rows = await (from s in db.DeploymentSites join c in db.Centers on s.CenterId equals c.CenterId where c.OrganizationId == p.OrgId && s.SiteCode == code select s).ToListAsync(ct);
        if (rows.Count > 1) throw new InvalidOperationException("AMBIGUOUS_SITE_CODE");
        var row = rows.SingleOrDefault();
        if (row is null) return null;
        var centerCode = await db.Centers.Where(x => x.CenterId == row.CenterId).Select(x => x.CenterCode).SingleAsync(ct);
        return new(row.SiteCode, row.DeploymentSiteId, centerCode, row.CenterId, null, row.EffectiveFrom, row.EffectiveTo, row.IsActive);
    }

    private async Task<bool> HasTeamCenterAsync(int teamId, string centerCode, int? centerTargetId, DateOnly from, DateOnly? to, Projection p, CancellationToken ct)
    {
        if (centerTargetId.HasValue && (await db.TeamCenterAssignments.Where(x => x.TeamId == teamId && x.CenterId == centerTargetId.Value).ToListAsync(ct)).Any(x => !p.Shadows("TeamCenter", x.TeamCenterAssignmentId) && V180MasterDataValidationService.Covers(x.EffectiveFrom, x.EffectiveTo, from, to))) return true;
        var teamCode = await TeamCenterCodeAsync(teamId, ct);
        foreach (var row in p.Rows.Where(x => x.EntityType == "TeamCenter" && Same(x.Values["TeamCode"], teamCode) && Same(x.Values["CenterCode"], centerCode) && Covers(Date(x.Values, "EffectiveFrom")!.Value, Date(x.Values, "EffectiveTo"), from, to)))
            return true;
        return false;
    }

    private async Task<bool> HasTeamSiteAsync(int teamId, string siteCode, int? siteTargetId, DateOnly from, DateOnly? to, Projection p, CancellationToken ct)
    {
        if (siteTargetId.HasValue && (await db.TeamDeploymentSiteAssignments.Where(x => x.TeamId == teamId && x.DeploymentSiteId == siteTargetId.Value).ToListAsync(ct)).Any(x => !p.Shadows("TeamSite", x.TeamDeploymentSiteAssignmentId) && V180MasterDataValidationService.Covers(x.EffectiveFrom, x.EffectiveTo, from, to))) return true;
        var teamCode = await TeamCenterCodeAsync(teamId, ct);
        foreach (var row in p.Rows.Where(x => x.EntityType == "TeamSite" && Same(x.Values["TeamCode"], teamCode) && Same(x.Values["SiteCode"], siteCode) && Covers(Date(x.Values, "EffectiveFrom")!.Value, Date(x.Values, "EffectiveTo"), from, to)))
            return true;
        return false;
    }

    private async Task<bool> HasActiveStatusAsync(long employmentId, DateOnly from, DateOnly? to, Projection p, CancellationToken ct)
    {
        if ((await db.EmploymentStatusPeriods.Where(x => x.EmploymentId == employmentId && x.EmploymentStatus == EmploymentStatuses.Active).ToListAsync(ct)).Any(x => !p.Shadows("EmploymentStatus", x.EmploymentStatusPeriodId) && V180MasterDataValidationService.Covers(x.EffectiveFrom, x.EffectiveTo, from, to))) return true;
        var employeeNo = await db.Employments.Where(x => x.EmploymentId == employmentId).Select(x => x.EmployeeNo).SingleOrDefaultAsync(ct);
        return p.Any("EmploymentStatus", x => Same(x.Values["EmployeeNo"], employeeNo) && Same(x.Values["Status"], "Active") && Covers(Date(x.Values, "EffectiveFrom")!.Value, Date(x.Values, "EffectiveTo"), from, to));
    }

    private async Task<bool> HasEffectiveSiteLocationAsync(ProjectedSite site, DateOnly from, DateOnly? to, Projection p, CancellationToken ct)
    {
        if (!site.TargetId.HasValue)
        {
            if (string.IsNullOrWhiteSpace(site.LocationCode)) return false;
            return await db.Locations.AnyAsync(x =>
                x.OrganizationId == p.OrgId
                && x.LocationCode == site.LocationCode
                && x.IsActive
                && x.ApprovalStatus == "Approved",
                ct);
        }

        return await db.DeploymentSiteLocationAssignments.AnyAsync(x =>
            x.DeploymentSiteId == site.TargetId.Value
            && x.EffectiveFrom <= from
            && (!to.HasValue ? !x.EffectiveTo.HasValue : !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= to.Value)
            && db.Locations.Any(l => l.LocationId == x.LocationId
                && l.OrganizationId == p.OrgId
                && l.IsActive
                && l.ApprovalStatus == "Approved"),
            ct);
    }

    // Preview must preserve E1's fail-closed update rules.  These checks use
    // only persisted children: a staged row is not a coordinated repair plan.
    private async Task EnsureEmploymentStatusDependenciesAsync(EmploymentStatusPeriod existing, string proposedStatus, DateOnly from, DateOnly? to, CancellationToken ct)
    {
        if (!Same(existing.EmploymentStatus, EmploymentStatuses.Active) || (Same(proposedStatus, EmploymentStatuses.Active) && existing.EffectiveFrom == from && existing.EffectiveTo == to)) return;
        var assignments = await db.EmploymentDeploymentSiteAssignments.Where(x => x.EmploymentId == existing.EmploymentId).ToListAsync(ct);
        var others = await db.EmploymentStatusPeriods.Where(x => x.EmploymentId == existing.EmploymentId && x.EmploymentStatusPeriodId != existing.EmploymentStatusPeriodId && x.EmploymentStatus == EmploymentStatuses.Active).ToListAsync(ct);
        foreach (var assignment in assignments)
        {
            var coveredByProposed = Same(proposedStatus, EmploymentStatuses.Active) && Covers(from, to, assignment.EffectiveFrom, assignment.EffectiveTo);
            if (!coveredByProposed && !others.Any(x => Covers(x.EffectiveFrom, x.EffectiveTo, assignment.EffectiveFrom, assignment.EffectiveTo))) throw new InvalidOperationException("EMPLOYMENT_STATUS_CHANGE_BREAKS_EMPLOYMENT_SITE");
        }
    }

    private async Task EnsureCenterDependenciesAsync(int centerId, DateOnly from, DateOnly? to, CancellationToken ct)
    {
        var teamCenters = await db.TeamCenterAssignments.Where(x => x.CenterId == centerId).ToListAsync(ct);
        var sites = await db.DeploymentSites.Where(x => x.CenterId == centerId).ToListAsync(ct);
        if (teamCenters.Any(x => !Covers(from, to, x.EffectiveFrom, x.EffectiveTo)) || sites.Any(x => !Covers(from, to, x.EffectiveFrom, x.EffectiveTo))) throw new InvalidOperationException("CENTER_PERIOD_HAS_DEPENDENCIES");
    }

    private async Task EnsureTeamCenterDependenciesAsync(TeamCenterAssignment existing, int? proposedCenterId, DateOnly from, DateOnly? to, CancellationToken ct)
    {
        var children = await (from assignment in db.TeamDeploymentSiteAssignments join site in db.DeploymentSites on assignment.DeploymentSiteId equals site.DeploymentSiteId where assignment.TeamId == existing.TeamId select new { site.CenterId, assignment.EffectiveFrom, assignment.EffectiveTo }).ToListAsync(ct);
        var others = await db.TeamCenterAssignments.Where(x => x.TeamId == existing.TeamId && x.TeamCenterAssignmentId != existing.TeamCenterAssignmentId).ToListAsync(ct);
        foreach (var child in children)
        {
            var proposedCovers = proposedCenterId.HasValue && child.CenterId == proposedCenterId.Value && Covers(from, to, child.EffectiveFrom, child.EffectiveTo);
            if (!proposedCovers && !others.Any(x => x.CenterId == child.CenterId && Covers(x.EffectiveFrom, x.EffectiveTo, child.EffectiveFrom, child.EffectiveTo))) throw new InvalidOperationException("TEAM_CENTER_CHANGE_BREAKS_TEAM_SITE");
        }
    }

    private async Task EnsureDeploymentSiteDependenciesAsync(DeploymentSite existing, int? proposedCenterId, DateOnly from, DateOnly? to, CancellationToken ct)
    {
        var teamChildren = await db.TeamDeploymentSiteAssignments.Where(x => x.DeploymentSiteId == existing.DeploymentSiteId).ToListAsync(ct);
        var employmentChildren = await db.EmploymentDeploymentSiteAssignments.Where(x => x.DeploymentSiteId == existing.DeploymentSiteId).ToListAsync(ct);
        if (proposedCenterId != existing.CenterId && (teamChildren.Count != 0 || employmentChildren.Count != 0)) throw new InvalidOperationException("DEPLOYMENT_SITE_CENTER_CHANGE_HAS_DEPENDENCIES");
        if (teamChildren.Any(x => !Covers(from, to, x.EffectiveFrom, x.EffectiveTo)) || employmentChildren.Any(x => !Covers(from, to, x.EffectiveFrom, x.EffectiveTo))) throw new InvalidOperationException("DEPLOYMENT_SITE_PERIOD_HAS_DEPENDENCIES");
    }

    private async Task EnsureTeamSiteDependenciesAsync(TeamDeploymentSiteAssignment existing, int? proposedSiteId, DateOnly from, DateOnly? to, CancellationToken ct)
    {
        if (proposedSiteId == existing.DeploymentSiteId && existing.EffectiveFrom == from && existing.EffectiveTo == to) return;
        var dependent = await (from employmentSite in db.EmploymentDeploymentSiteAssignments join membership in db.TeamMemberships on employmentSite.EmploymentId equals membership.EmploymentId where employmentSite.DeploymentSiteId == existing.DeploymentSiteId && membership.TeamId == existing.TeamId && membership.EffectiveFrom <= (employmentSite.EffectiveTo ?? DateOnly.MaxValue) && (!membership.EffectiveTo.HasValue || employmentSite.EffectiveFrom <= membership.EffectiveTo.Value) select employmentSite.EmploymentDeploymentSiteAssignmentId).AnyAsync(ct);
        if (dependent) throw new InvalidOperationException("TEAM_SITE_CHANGE_HAS_DEPENDENCIES");
    }

    private async Task<string?> TeamCenterCodeAsync(int teamId, CancellationToken ct) => await db.Teams.Where(x => x.TeamId == teamId).Select(x => x.TeamCode).SingleOrDefaultAsync(ct);

    private static Dictionary<string, List<WorkbookRow>> ParseWorkbook(byte[] content)
    {
        using var stream = new MemoryStream(content); using var doc = SpreadsheetDocument.Open(stream, false);
        var workbook = doc.WorkbookPart ?? throw new InvalidOperationException("BULK_XLSX_INVALID");
        var sheets = workbook.Workbook.Sheets?.Elements<Sheet>().ToList() ?? [];
        var names = sheets.Select(x => x.Name?.Value ?? "").ToList();
        if (names.Count != Specs.Length || Specs.Any(x => !names.Contains(x.Name, StringComparer.OrdinalIgnoreCase)) || names.Any(x => !Specs.Any(s => s.Name.Equals(x, StringComparison.OrdinalIgnoreCase)))) throw new InvalidOperationException("BULK_SHEET_SET_INVALID");
        var result = new Dictionary<string, List<WorkbookRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in Specs)
        {
            var sheet = sheets.Single(x => spec.Name.Equals(x.Name?.Value, StringComparison.OrdinalIgnoreCase));
            var rows = ReadRows(workbook, sheet); if (rows.Count == 0) throw new InvalidOperationException($"BULK_HEADER_REQUIRED:{spec.Name}");
            var headers = rows[0];
            var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++) if (!string.IsNullOrWhiteSpace(headers[i])) { var h = headers[i]!.Trim(); if (!positions.TryAdd(h, i)) throw new InvalidOperationException($"BULK_HEADER_DUPLICATE:{spec.Name}:{h}"); }
            foreach (var required in spec.Headers) if (!positions.ContainsKey(required)) throw new InvalidOperationException($"BULK_HEADER_REQUIRED:{spec.Name}:{required}");
            foreach (var header in positions.Keys) if (!spec.Headers.Contains(header, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException($"BULK_HEADER_UNKNOWN:{spec.Name}:{header}");
            var data = new List<WorkbookRow>();
            for (var r = 1; r < rows.Count; r++)
            {
                var values = spec.Headers.ToDictionary(x => x, x => positions[x] < rows[r].Length ? Blank(rows[r][positions[x]]) : null, StringComparer.OrdinalIgnoreCase);
                if (values.Values.All(string.IsNullOrWhiteSpace)) continue;
                data.Add(new WorkbookRow(r + 1, values, false));
            }
            var duplicateKeys = data.GroupBy(x => DuplicateKey(spec.Name, x.Values), StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            result[spec.Name] = data.Select(x => x with { Duplicate = duplicateKeys.Contains(DuplicateKey(spec.Name, x.Values)) }).ToList();
        }
        return result;
    }

    private static List<string?[]> ReadRows(WorkbookPart workbook, Sheet sheet)
    {
        var part = (WorksheetPart)workbook.GetPartById(sheet.Id!); var result = new List<string?[]>();
        foreach (var row in part.Worksheet.Descendants<Row>())
        {
            var values = new string?[32]; var sequential = 0;
            foreach (var cell in row.Elements<Cell>()) { var i = ColumnIndex(cell.CellReference?.Value); if (i < 0) i = sequential; if (i < values.Length) values[i] = CellText(workbook, cell); sequential = Math.Max(sequential + 1, i + 1); }
            result.Add(values);
        }
        return result;
    }

    private static StagedRow Valid(WorkbookRow row, string entity, string action, string display, object data, long? targetId = null) => row.Duplicate ? Error(row, entity, "NoChange", display, "BULK_DUPLICATE_BUSINESS_KEY") : new(row.RowNumber, entity, action, "Valid", display, data, null, row.Values, targetId);
    private static StagedRow Error(WorkbookRow row, string entity, string action, string display, string code) => new(row.RowNumber, entity, action, "Error", display, new { Input = row.Values }, code, row.Values, null);
    private static string Required(WorkbookRow row, string name, string code) => string.IsNullOrWhiteSpace(row.Values[name]) ? throw new InvalidOperationException(code) : row.Values[name]!.Trim();
    private static DateOnly? Date(WorkbookRow row, string name, bool required) => Date(row.Values, name, required);
    private static DateOnly? Date(IReadOnlyDictionary<string, string?> values, string name, bool required = false) { var value = values[name]; if (string.IsNullOrWhiteSpace(value)) { if (required) throw new InvalidOperationException($"{name.ToUpperInvariant()}_REQUIRED"); return null; } if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)) return result; if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial)) return DateOnly.FromDateTime(DateTime.FromOADate(serial)); throw new InvalidOperationException($"{name.ToUpperInvariant()}_INVALID"); }
    private static bool Bool(WorkbookRow row, string name) { var v = row.Values[name]; if (IsTrue(v)) return true; if (Same(v, "false") || Same(v, "0")) return false; throw new InvalidOperationException($"{name.ToUpperInvariant()}_INVALID"); }
    private static bool IsTrue(string? value) => Same(value, "true") || Same(value, "1");
    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool Overlap(DateOnly from, DateOnly? to, StagedRow row) => V180MasterDataValidationService.Overlaps(from, to, Date(row.Values, "EffectiveFrom")!.Value, Date(row.Values, "EffectiveTo"));
    private static bool Covers(DateOnly from, DateOnly? to, DateOnly requiredFrom, DateOnly? requiredTo) => V180MasterDataValidationService.Covers(from, to, requiredFrom, requiredTo);
    private static string? Version(byte[]? value) => value is { Length: > 0 } ? Convert.ToBase64String(value) : null;
    private static int? IntTargetId(long? targetId) => targetId.HasValue ? checked((int)targetId.Value) : null;
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string EntityType(string sheet) => sheet switch { "Centers" => "Center", "TeamCenters" => "TeamCenter", "DeploymentSites" => "DeploymentSite", "TeamSites" => "TeamSite", "EmploymentSites" => "EmploymentSite", _ => sheet };
    private static string DisplayKey(string sheet, WorkbookRow row) => DuplicateKey(sheet, row.Values);
    private static string DuplicateKey(string sheet, IReadOnlyDictionary<string, string?> v) => sheet switch { "EmploymentStatus" => $"{v["EmployeeNo"]}|{v["EffectiveFrom"]}", "Centers" => v["CenterCode"] ?? "", "TeamCenters" => $"{v["TeamCode"]}|{v["EffectiveFrom"]}", "DeploymentSites" => v["SiteCode"] ?? "", "TeamSites" => $"{v["TeamCode"]}|{v["SiteCode"]}|{v["EffectiveFrom"]}", _ => $"{v["EmployeeNo"]}|{v["SiteCode"]}|{v["EffectiveFrom"]}" };
    private static int RequireAdmin(CurrentUserDto admin)
    {
        if (!admin.Roles.Any(x => x.Equals("admin", StringComparison.OrdinalIgnoreCase))) throw new UnauthorizedAccessException("ADMIN_REQUIRED");
        return admin.OrganizationId ?? throw new InvalidOperationException("ADMIN_ORGANIZATION_REQUIRED");
    }
    private static string[] Example(string name) => name switch { "EmploymentStatus" => ["E100", "Active", "2026-01-01", ""], "Centers" => ["C01", "Center 01", "2026-01-01", "", "true"], "TeamCenters" => ["T01", "C01", "2026-01-01", ""], "DeploymentSites" => ["C01", "S01", "Site 01", "L01", "2026-01-01", "", "true"], "TeamSites" => ["T01", "S01", "2026-01-01", ""], _ => ["E100", "S01", "true", "2026-01-01", ""] };
    private static void AddSheet(WorkbookPart workbook, Sheets sheets, uint id, string name, IEnumerable<string[]> rows) { var part = workbook.AddNewPart<WorksheetPart>(); var data = new SheetData(); foreach (var values in rows) { var row = new Row(); foreach (var value in values) row.Append(new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value ?? "")) }); data.Append(row); } part.Worksheet = new Worksheet(data); sheets.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = id, Name = name }); }
    private static int ColumnIndex(string? reference) { if (string.IsNullOrWhiteSpace(reference)) return -1; var value = 0; foreach (var ch in reference.TakeWhile(char.IsLetter)) value = value * 26 + char.ToUpperInvariant(ch) - 'A' + 1; return value - 1; }
    private static string? CellText(WorkbookPart workbook, Cell cell) { var raw = cell.CellValue?.InnerText ?? cell.InlineString?.Text?.Text ?? cell.InlineString?.InnerText; if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(raw, out var index)) return workbook.SharedStringTablePart?.SharedStringTable.ElementAt(index).InnerText; return raw; }

    private sealed record SheetSpec(string Name, string[] Headers);
    private sealed record WorkbookRow(int RowNumber, Dictionary<string, string?> Values, bool Duplicate);
    private sealed record StagedRow(int RowNumber, string EntityType, string Action, string Status, string DisplayKey, object Data, string? ErrorMessage, Dictionary<string, string?> Values, long? TargetId);
    private sealed record ProjectedCenter(string CenterCode, int? TargetId, DateOnly EffectiveFrom, DateOnly? EffectiveTo);
    private sealed record ProjectedSite(string SiteCode, int? TargetId, string CenterCode, int? CenterTargetId, string? LocationCode, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);
    private sealed class Projection(int orgId)
    {
        private readonly List<StagedRow> rows = [];
        public int OrgId { get; } = orgId;
        public IReadOnlyList<StagedRow> Rows => rows;
        public void Add(StagedRow row) => rows.Add(row);
        public bool Any(string type, Func<StagedRow, bool> predicate) => rows.Where(x => x.EntityType == type).Any(predicate);
        public StagedRow? First(string type, string key, string value) => rows.FirstOrDefault(x => x.EntityType == type && Same(x.Values[key], value));
        public bool Shadows(string type, long targetId) => rows.Any(x => x.EntityType == type && x.TargetId == targetId);
    }
}
