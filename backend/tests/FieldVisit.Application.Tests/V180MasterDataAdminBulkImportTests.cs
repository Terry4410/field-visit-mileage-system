using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180MasterDataAdminBulkImportTests
{
    private static readonly string[] SheetNames = ["EmploymentStatus", "Centers", "TeamCenters", "DeploymentSites", "TeamSites", "EmploymentSites"];

    [Fact]
    public async Task Template_has_exactly_six_expected_sheets_and_headers()
    {
        await using var db = Db();
        var file = await new V180MasterDataBulkWorkbookService(db).CreateTemplateAsync(Admin(), default);
        using var stream = new MemoryStream(file.Content); using var book = SpreadsheetDocument.Open(stream, false);
        Assert.Equal(SheetNames, book.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Select(x => x.Name!.Value).ToArray());
        Assert.Equal(new[] { "EmployeeNo", "Status", "EffectiveFrom", "EffectiveTo" }, Headers(book, "EmploymentStatus"));
        Assert.Equal(new[] { "EmployeeNo", "SiteCode", "IsPrimary", "EffectiveFrom", "EffectiveTo" }, Headers(book, "EmploymentSites"));
    }

    [Fact]
    public async Task Preview_rejects_missing_or_unexpected_sheets_and_headers()
    {
        await using var db = Db(); var service = new V180MasterDataBulkWorkbookService(db);
        var missing = Workbook(SheetNames.Take(5).ToDictionary(x => x, _ => new[] { Array.Empty<string>() }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewAsync(Admin(), missing, default));
        var headers = EmptyWorkbook(); headers["Centers"] = new[] { new[] { "CenterCode", "CenterName", "EffectiveFrom", "EffectiveTo", "IsActive", "Unexpected" } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewAsync(Admin(), Workbook(headers), default));
    }

    [Fact]
    public async Task Preview_stages_only_import_rows_and_never_mutates_master_data()
    {
        await using var db = Db(); var service = new V180MasterDataBulkWorkbookService(db);
        var rows = EmptyWorkbook(); rows["Centers"] = new[] { new[] { "CenterCode", "CenterName", "EffectiveFrom", "EffectiveTo", "IsActive" }, new[] { "C01", "Center", "2026-01-01", "", "true" } };
        var preview = await service.PreviewAsync(Admin(), Workbook(rows), default);
        Assert.Equal(1, preview.TotalCount); Assert.Equal(1, preview.ValidCount); Assert.Empty(db.Centers);
        Assert.Single(db.ImportBatches); Assert.Single(db.ImportBatchItems); Assert.Single(db.AuditLogs.Where(x => x.Action == "V180MasterDataBulkPreview"));
    }

    [Fact]
    public async Task Preview_classifies_create_update_nochange_and_snapshots_existing_rowversion()
    {
        await using var db = Db();
        db.Centers.AddRange(
            new Center { CenterId = 1, OrganizationId = 1, CenterCode = "SAME", CenterName = "Same", EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true, RowVersion = [1] },
            new Center { CenterId = 2, OrganizationId = 1, CenterCode = "UPDATE", CenterName = "Old", EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true, RowVersion = [2] });
        await db.SaveChangesAsync();
        var rows = EmptyWorkbook(); rows["Centers"] = new[]
        {
            new[] { "CenterCode", "CenterName", "EffectiveFrom", "EffectiveTo", "IsActive" },
            new[] { "NEW", "New", "2026-01-01", "", "true" }, new[] { "SAME", "Same", "2026-01-01", "", "true" }, new[] { "UPDATE", "Changed", "2026-01-01", "", "true" }
        };
        var preview = await new V180MasterDataBulkWorkbookService(db).PreviewAsync(Admin(), Workbook(rows), default);
        Assert.Equal(new[] { "Create", "NoChange", "Update" }, preview.Items.Select(x => x.Action).ToArray());
        var update = db.ImportBatchItems.Single(x => x.DisplayKey == "UPDATE");
        Assert.Contains("expectedRowVersion", update.DataJson); Assert.Contains(Convert.ToBase64String([2]), update.DataJson);
    }

    [Fact]
    public async Task Duplicate_business_key_and_cross_organization_reference_fail_closed()
    {
        await using var db = Db();
        db.Locations.Add(new FieldVisit.Domain.Entities.Location { LocationId = 5, OrganizationId = 2, LocationCode = "L-OTHER", LocationName = "Other", IsActive = true, ApprovalStatus = "Approved", CreatedAt = DateTime.UtcNow });
        db.Centers.Add(new Center { CenterId = 1, OrganizationId = 1, CenterCode = "C01", CenterName = "Center", EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true });
        await db.SaveChangesAsync();
        var rows = EmptyWorkbook(); rows["Centers"] = new[] { new[] { "CenterCode", "CenterName", "EffectiveFrom", "EffectiveTo", "IsActive" }, new[] { "DUP", "A", "2026-01-01", "", "true" }, new[] { "DUP", "B", "2026-01-01", "", "true" } };
        rows["DeploymentSites"] = new[] { new[] { "CenterCode", "SiteCode", "SiteName", "LocationCode", "EffectiveFrom", "EffectiveTo", "IsActive" }, new[] { "C01", "S01", "Site", "L-OTHER", "2026-01-01", "", "true" } };
        var preview = await new V180MasterDataBulkWorkbookService(db).PreviewAsync(Admin(), Workbook(rows), default);
        Assert.Equal(3, preview.ErrorCount); Assert.Contains(preview.Items, x => x.ErrorMessage == "BULK_DUPLICATE_BUSINESS_KEY"); Assert.Contains(preview.Items, x => x.ErrorMessage == "UNKNOWN_LOCATION_CODE");
    }

    [Fact]
    public async Task Valid_staged_parents_satisfy_later_rows_but_invalid_parents_do_not()
    {
        await using var db = Db(); Seed(db); await db.SaveChangesAsync();
        var rows = EmptyWorkbook();
        rows["Centers"] = new[] { new[] { "CenterCode", "CenterName", "EffectiveFrom", "EffectiveTo", "IsActive" }, new[] { "C01", "Center", "2026-01-01", "", "true" } };
        rows["TeamCenters"] = new[] { new[] { "TeamCode", "CenterCode", "EffectiveFrom", "EffectiveTo" }, new[] { "T01", "C01", "2026-01-01", "" } };
        rows["DeploymentSites"] = new[] { new[] { "CenterCode", "SiteCode", "SiteName", "LocationCode", "EffectiveFrom", "EffectiveTo", "IsActive" }, new[] { "C01", "S01", "Site", "L01", "2026-01-01", "", "true" } };
        rows["TeamSites"] = new[] { new[] { "TeamCode", "SiteCode", "EffectiveFrom", "EffectiveTo" }, new[] { "T01", "S01", "2026-01-01", "" } };
        rows["EmploymentStatus"] = new[] { new[] { "EmployeeNo", "Status", "EffectiveFrom", "EffectiveTo" }, new[] { "E01", "Active", "2026-01-01", "" } };
        rows["EmploymentSites"] = new[] { new[] { "EmployeeNo", "SiteCode", "IsPrimary", "EffectiveFrom", "EffectiveTo" }, new[] { "E01", "S01", "true", "2026-01-01", "" } };
        var preview = await new V180MasterDataBulkWorkbookService(db).PreviewAsync(Admin(), Workbook(rows), default);
        Assert.Equal(6, preview.ValidCount); Assert.Equal(0, preview.ErrorCount);
    }

    [Fact]
    public async Task Different_staged_centers_do_not_satisfy_same_center_coverage()
    {
        await using var db = Db(); Seed(db); await db.SaveChangesAsync();
        var rows = StagedBase(); rows["Centers"] = new[] { HeadersFor("Centers"), new[] { "C01", "One", "2026-01-01", "", "true" }, new[] { "C02", "Two", "2026-01-01", "", "true" } };
        rows["TeamCenters"] = new[] { HeadersFor("TeamCenters"), new[] { "T01", "C01", "2026-01-01", "" } };
        rows["DeploymentSites"] = new[] { HeadersFor("DeploymentSites"), new[] { "C02", "S01", "Site", "L01", "2026-01-01", "", "true" } };
        rows["TeamSites"] = new[] { HeadersFor("TeamSites"), new[] { "T01", "S01", "2026-01-01", "" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.EntityType == "TeamSite" && x.ErrorMessage == "TEAM_SITE_WITHOUT_TEAM_CENTER_COVERAGE");
    }

    [Fact]
    public async Task Different_staged_sites_do_not_satisfy_employment_site_coverage()
    {
        await using var db = Db(); Seed(db); await db.SaveChangesAsync();
        var rows = StagedBase(); rows["TeamSites"] = new[] { HeadersFor("TeamSites"), new[] { "T01", "S02", "2026-01-01", "" } };
        rows["EmploymentSites"] = new[] { HeadersFor("EmploymentSites"), new[] { "E01", "S01", "true", "2026-01-01", "" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.EntityType == "EmploymentSite" && x.ErrorMessage == "EMPLOYMENT_SITE_WITHOUT_TEAM_SITE_COVERAGE");
    }

    [Fact]
    public async Task Staged_existing_center_update_retains_target_identity()
    {
        await using var db = Db(); db.Centers.Add(new Center { CenterId = 7, OrganizationId = 1, CenterCode = "C01", CenterName = "Old", EffectiveFrom = new DateOnly(2020, 1, 1), IsActive = true, RowVersion = [7] }); await db.SaveChangesAsync();
        var rows = EmptyWorkbook(); rows["Centers"] = new[] { HeadersFor("Centers"), new[] { "C01", "New", "2020-01-01", "", "true" } };
        await Preview(db, rows);
        var item = db.ImportBatchItems.Single(); Assert.Contains("targetId\":7", item.DataJson); Assert.Contains(Convert.ToBase64String([7]), item.DataJson);
    }

    [Fact]
    public async Task Staged_existing_site_update_retains_target_identity()
    {
        await using var db = Db(); SeedExistingCoverage(db); await db.SaveChangesAsync();
        var rows = EmptyWorkbook(); rows["DeploymentSites"] = new[] { HeadersFor("DeploymentSites"), new[] { "C01", "S01", "Changed", "L01", "2020-01-01", "", "true" } };
        await Preview(db, rows);
        var item = db.ImportBatchItems.Single(); Assert.Contains("targetId\":40", item.DataJson); Assert.Contains(Convert.ToBase64String([4]), item.DataJson);
    }

    [Fact]
    public async Task Employment_status_update_rejects_breaking_existing_employment_site()
    {
        await using var db = Db(); SeedExistingCoverage(db); await db.SaveChangesAsync();
        var rows = EmptyWorkbook(); rows["EmploymentStatus"] = new[] { HeadersFor("EmploymentStatus"), new[] { "E01", "Leave", "2020-01-01", "" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.ErrorMessage == "EMPLOYMENT_STATUS_CHANGE_BREAKS_EMPLOYMENT_SITE");
    }

    [Fact]
    public async Task Center_update_rejects_breaking_existing_children()
    {
        await using var db = Db(); SeedExistingCoverage(db); await db.SaveChangesAsync();
        var rows = EmptyWorkbook(); rows["Centers"] = new[] { HeadersFor("Centers"), new[] { "C01", "Center", "2021-01-01", "", "true" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.ErrorMessage == "CENTER_PERIOD_HAS_DEPENDENCIES");
    }

    [Fact]
    public async Task Team_center_update_rejects_breaking_existing_team_site()
    {
        await using var db = Db(); SeedExistingCoverage(db); await db.SaveChangesAsync();
        var rows = EmptyWorkbook(); rows["TeamCenters"] = new[] { HeadersFor("TeamCenters"), new[] { "T01", "C01", "2020-01-01", "2020-12-31" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.ErrorMessage == "TEAM_CENTER_CHANGE_BREAKS_TEAM_SITE");
    }

    [Fact]
    public async Task Deployment_site_update_preserves_reverse_dependencies_and_location_history()
    {
        await using var db = Db(); SeedExistingCoverage(db); await db.SaveChangesAsync();
        var rows = EmptyWorkbook(); rows["DeploymentSites"] = new[] { HeadersFor("DeploymentSites"), new[] { "C01", "S01", "Site", "L01", "2021-01-01", "", "true" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.ErrorMessage == "DEPLOYMENT_SITE_PERIOD_HAS_DEPENDENCIES");
    }

    [Fact]
    public async Task Team_site_update_rejects_breaking_existing_employment_site()
    {
        await using var db = Db(); SeedExistingCoverage(db); await db.SaveChangesAsync();
        var rows = EmptyWorkbook(); rows["TeamSites"] = new[] { HeadersFor("TeamSites"), new[] { "T01", "S01", "2020-01-01", "2020-12-31" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.ErrorMessage == "TEAM_SITE_CHANGE_HAS_DEPENDENCIES");
    }

    [Fact]
    public async Task Deployment_site_update_requires_existing_location_history()
    {
        await using var db = Db(); SeedExistingCoverage(db);
        db.Locations.Add(new FieldVisit.Domain.Entities.Location { LocationId = 21, OrganizationId = 1, LocationCode = "L02", LocationName = "Location 2", IsActive = true, ApprovalStatus = "Approved", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var rows = EmptyWorkbook(); rows["DeploymentSites"] = new[] { HeadersFor("DeploymentSites"), new[] { "C01", "S01", "Site", "L02", "2020-01-01", "", "true" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.EntityType == "DeploymentSite" && x.ErrorMessage == "DEPLOYMENT_SITE_LOCATION_CHANGE_REQUIRES_RELOCATION_FLOW");
    }

    [Fact]
    public async Task Deployment_site_update_requires_location_history_covering_proposed_period()
    {
        await using var db = Db(); SeedExistingCoverage(db); await db.SaveChangesAsync();
        db.DeploymentSiteLocationAssignments.Single().EffectiveTo = new DateOnly(2025, 12, 31);
        await db.SaveChangesAsync();
        var rows = EmptyWorkbook(); rows["DeploymentSites"] = new[] { HeadersFor("DeploymentSites"), new[] { "C01", "S01", "Site", "L01", "2020-01-01", "", "true" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.EntityType == "DeploymentSite" && x.ErrorMessage == "DEPLOYMENT_SITE_LOCATION_COVERAGE_REQUIRED");
    }

    [Fact]
    public async Task Employment_site_requires_effective_approved_active_location()
    {
        await using var db = Db(); SeedExistingCoverage(db); await db.SaveChangesAsync();
        db.EmploymentDeploymentSiteAssignments.RemoveRange(db.EmploymentDeploymentSiteAssignments);
        db.Locations.Single(x => x.LocationCode == "L01").IsActive = false;
        await db.SaveChangesAsync();
        var rows = EmptyWorkbook(); rows["EmploymentSites"] = new[] { HeadersFor("EmploymentSites"), new[] { "E01", "S01", "false", "2021-01-01", "" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.EntityType == "EmploymentSite" && x.ErrorMessage == "EMPLOYMENT_SITE_WITHOUT_EFFECTIVE_LOCATION");
    }

    [Fact]
    public async Task Staged_employment_status_update_shadows_db_status_for_employment_site()
    {
        await using var db = Db(); SeedExistingCoverage(db); await db.SaveChangesAsync();
        db.EmploymentDeploymentSiteAssignments.RemoveRange(db.EmploymentDeploymentSiteAssignments);
        await db.SaveChangesAsync();
        var rows = EmptyWorkbook();
        rows["EmploymentStatus"] = new[] { HeadersFor("EmploymentStatus"), new[] { "E01", "Leave", "2020-01-01", "" } };
        rows["EmploymentSites"] = new[] { HeadersFor("EmploymentSites"), new[] { "E01", "S01", "false", "2021-01-01", "" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.EntityType == "EmploymentStatus" && x.Status == "Valid" && x.Action == "Update");
        Assert.Contains(preview.Items, x => x.EntityType == "EmploymentSite" && x.ErrorMessage == "EMPLOYMENT_SITE_WITHOUT_ACTIVE_EMPLOYMENT");
    }

    [Fact]
    public async Task Staged_team_center_update_shadows_db_coverage_for_team_site()
    {
        await using var db = Db(); SeedExistingCoverage(db); await db.SaveChangesAsync();
        db.TeamDeploymentSiteAssignments.RemoveRange(db.TeamDeploymentSiteAssignments);
        db.EmploymentDeploymentSiteAssignments.RemoveRange(db.EmploymentDeploymentSiteAssignments);
        db.Centers.Add(new Center { CenterId = 31, OrganizationId = 1, CenterCode = "C02", CenterName = "Center 2", EffectiveFrom = new DateOnly(2020, 1, 1), IsActive = true });
        await db.SaveChangesAsync();
        var rows = EmptyWorkbook();
        rows["TeamCenters"] = new[] { HeadersFor("TeamCenters"), new[] { "T01", "C02", "2020-01-01", "" } };
        rows["TeamSites"] = new[] { HeadersFor("TeamSites"), new[] { "T01", "S01", "2020-01-01", "" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.EntityType == "TeamCenter" && x.Status == "Valid" && x.Action == "Update");
        Assert.Contains(preview.Items, x => x.EntityType == "TeamSite" && x.ErrorMessage == "TEAM_SITE_WITHOUT_TEAM_CENTER_COVERAGE");
    }

    [Fact]
    public async Task Staged_team_site_update_shadows_db_coverage_for_employment_site()
    {
        await using var db = Db(); SeedExistingCoverage(db); await db.SaveChangesAsync();
        db.EmploymentDeploymentSiteAssignments.RemoveRange(db.EmploymentDeploymentSiteAssignments);
        await db.SaveChangesAsync();
        var rows = EmptyWorkbook();
        rows["TeamSites"] = new[] { HeadersFor("TeamSites"), new[] { "T01", "S01", "2020-01-01", "2020-12-31" } };
        rows["EmploymentSites"] = new[] { HeadersFor("EmploymentSites"), new[] { "E01", "S01", "false", "2021-01-01", "" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.EntityType == "TeamSite" && x.Status == "Valid" && x.Action == "Update");
        Assert.Contains(preview.Items, x => x.EntityType == "EmploymentSite" && x.ErrorMessage == "EMPLOYMENT_SITE_WITHOUT_TEAM_SITE_COVERAGE");
    }

    [Fact]
    public async Task Employment_site_rejects_same_site_overlap_even_when_nonprimary()
    {
        await using var db = Db(); SeedExistingCoverage(db); await db.SaveChangesAsync();
        var rows = EmptyWorkbook(); rows["EmploymentSites"] = new[] { HeadersFor("EmploymentSites"), new[] { "E01", "S01", "false", "2021-01-01", "" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.EntityType == "EmploymentSite" && x.ErrorMessage == "OVERLAPPING_EMPLOYMENT_SITE");
    }

    [Fact]
    public async Task Staged_employment_site_update_shadows_db_primary_overlap()
    {
        await using var db = Db(); SeedExistingCoverage(db);
        db.DeploymentSites.Add(new DeploymentSite { DeploymentSiteId = 41, CenterId = 30, SiteCode = "S02", SiteName = "Site 2", EffectiveFrom = new DateOnly(2020, 1, 1), IsActive = true, CreatedAt = DateTime.UtcNow });
        db.DeploymentSiteLocationAssignments.Add(new DeploymentSiteLocationAssignment { DeploymentSiteLocationAssignmentId = 6, DeploymentSiteId = 41, LocationId = 20, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = DateTime.UtcNow });
        db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment { TeamDeploymentSiteAssignmentId = 7, TeamId = 10, DeploymentSiteId = 41, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var rows = EmptyWorkbook();
        rows["EmploymentSites"] = new[]
        {
            HeadersFor("EmploymentSites"),
            new[] { "E01", "S01", "false", "2020-01-01", "" },
            new[] { "E01", "S02", "true", "2021-01-01", "" }
        };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.DisplayKey == "E01|S01|2020-01-01" && x.Status == "Valid" && x.Action == "Update");
        Assert.Contains(preview.Items, x => x.DisplayKey == "E01|S02|2021-01-01" && x.Status == "Valid" && x.Action == "Create");
    }

    [Fact]
    public async Task Existing_site_db_team_site_coverage_satisfies_employment_site()
    {
        await using var db = Db(); SeedExistingCoverage(db); await db.SaveChangesAsync();
        db.EmploymentDeploymentSiteAssignments.RemoveRange(db.EmploymentDeploymentSiteAssignments);
        db.TeamCenterAssignments.RemoveRange(db.TeamCenterAssignments);
        await db.SaveChangesAsync();
        var rows = EmptyWorkbook(); rows["EmploymentSites"] = new[] { HeadersFor("EmploymentSites"), new[] { "E01", "S01", "false", "2021-01-01", "" } };
        var preview = await Preview(db, rows);
        Assert.Contains(preview.Items, x => x.EntityType == "EmploymentSite" && x.Status == "Valid" && x.Action == "Create");
    }


    [Fact]
    public async Task Confirm_applies_valid_batch_and_is_idempotent()
    {
        await using var db = Db(); Seed(db); await db.SaveChangesAsync();
        var rows = StagedBase();
        rows["TeamSites"] = new[] { HeadersFor("TeamSites"), new[] { "T01", "S01", "2026-01-01", "" } };
        rows["EmploymentSites"] = new[] { HeadersFor("EmploymentSites"), new[] { "E01", "S01", "true", "2026-01-01", "" } };
        var service = new V180MasterDataBulkWorkbookService(db);

        var preview = await service.PreviewAsync(Admin(), Workbook(rows), default);
        Assert.Equal(6, preview.ValidCount);
        Assert.Equal(0, preview.ErrorCount);

        var result = await service.ConfirmAsync(Admin(), preview.ImportBatchId, default);

        Assert.Equal(6, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Unchanged);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Errors);
        Assert.Single(db.EmploymentStatusPeriods);
        Assert.Single(db.Centers);
        Assert.Single(db.TeamCenterAssignments);
        Assert.Single(db.DeploymentSites);
        Assert.Single(db.TeamDeploymentSiteAssignments);
        Assert.Single(db.EmploymentDeploymentSiteAssignments);
        Assert.Equal("Confirmed", db.ImportBatches.Single().Status);
        Assert.All(db.ImportBatchItems, x => Assert.Equal("Applied", x.Status));
        Assert.Single(db.AuditLogs.Where(x => x.Action == "V180MasterDataBulkConfirm"));

        var repeated = await service.ConfirmAsync(Admin(), preview.ImportBatchId, default);
        Assert.Equal(6, repeated.Created);
        Assert.Single(db.Centers);
        Assert.Single(db.DeploymentSites);
    }

    [Fact]
    public async Task Confirm_rejects_preview_errors_without_master_mutation()
    {
        await using var db = Db(); Seed(db); await db.SaveChangesAsync();
        var rows = EmptyWorkbook();
        rows["Centers"] = new[]
        {
            HeadersFor("Centers"),
            new[] { "C-DUP", "One", "2026-01-01", "", "true" },
            new[] { "C-DUP", "Two", "2026-01-01", "", "true" }
        };
        var service = new V180MasterDataBulkWorkbookService(db);
        var preview = await service.PreviewAsync(Admin(), Workbook(rows), default);
        Assert.True(preview.ErrorCount > 0);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ConfirmAsync(Admin(), preview.ImportBatchId, default));

        Assert.Equal("BULK_BATCH_HAS_ERRORS", ex.Message);
        Assert.Empty(db.Centers);
        Assert.Equal("Previewed", db.ImportBatches.Single().Status);
        Assert.Empty(db.AuditLogs.Where(x => x.Action == "V180MasterDataBulkConfirm"));
    }

    [Fact]
    public async Task Confirm_revalidates_entire_batch_before_applying_any_row()
    {
        await using var db = Db(); Seed(db); await db.SaveChangesAsync();
        var rows = StagedBase();
        var service = new V180MasterDataBulkWorkbookService(db);
        var preview = await service.PreviewAsync(Admin(), Workbook(rows), default);
        Assert.Equal(4, preview.ValidCount);

        db.Locations.Single(x => x.LocationCode == "L01").IsActive = false;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ConfirmAsync(Admin(), preview.ImportBatchId, default));

        Assert.Contains("BULK_CONFIRM_REVALIDATION_FAILED:LOCATION_NOT_ACTIVE", ex.Message);
        Assert.Empty(db.EmploymentStatusPeriods);
        Assert.Empty(db.Centers);
        Assert.Empty(db.TeamCenterAssignments);
        Assert.Empty(db.DeploymentSites);
        Assert.Equal("Previewed", db.ImportBatches.Single().Status);
    }

    [Fact]
    public async Task Confirm_rejects_rowversion_drift()
    {
        await using var db = Db(); SeedExistingCoverage(db); await db.SaveChangesAsync();
        var rows = EmptyWorkbook();
        rows["Centers"] = new[]
        {
            HeadersFor("Centers"),
            new[] { "C01", "Changed", "2020-01-01", "", "true" }
        };
        var service = new V180MasterDataBulkWorkbookService(db);
        var preview = await service.PreviewAsync(Admin(), Workbook(rows), default);
        Assert.Contains(preview.Items, x => x.EntityType == "Center" && x.Action == "Update");

        db.Centers.Single().RowVersion = [9];
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ConfirmAsync(Admin(), preview.ImportBatchId, default));

        Assert.Equal("BULK_CONFIRM_ROWVERSION_CHANGED", ex.Message);
        Assert.Equal("Center", db.Centers.Single().CenterName);
        Assert.Equal("Previewed", db.ImportBatches.Single().Status);
    }

    [Fact]
    public async Task Confirm_rejects_action_drift()
    {
        await using var db = Db(); Seed(db); await db.SaveChangesAsync();
        var rows = EmptyWorkbook();
        rows["Centers"] = new[]
        {
            HeadersFor("Centers"),
            new[] { "C01", "Center", "2026-01-01", "", "true" }
        };
        var service = new V180MasterDataBulkWorkbookService(db);
        var preview = await service.PreviewAsync(Admin(), Workbook(rows), default);
        Assert.Contains(preview.Items, x => x.EntityType == "Center" && x.Action == "Create");

        db.Centers.Add(new Center
        {
            CenterId = 99,
            OrganizationId = 1,
            CenterCode = "C01",
            CenterName = "Center",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ConfirmAsync(Admin(), preview.ImportBatchId, default));

        Assert.Equal("BULK_CONFIRM_ACTION_CHANGED", ex.Message);
        Assert.Single(db.Centers);
        Assert.Equal("Previewed", db.ImportBatches.Single().Status);
    }

    [Fact]
    public async Task Confirm_rejects_expired_batch()
    {
        await using var db = Db(); Seed(db); await db.SaveChangesAsync();
        var rows = EmptyWorkbook();
        rows["Centers"] = new[]
        {
            HeadersFor("Centers"),
            new[] { "C01", "Center", "2026-01-01", "", "true" }
        };
        var service = new V180MasterDataBulkWorkbookService(db);
        var preview = await service.PreviewAsync(Admin(), Workbook(rows), default);

        db.ImportBatches.Single().ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ConfirmAsync(Admin(), preview.ImportBatchId, default));

        Assert.Equal("BULK_BATCH_EXPIRED", ex.Message);
        Assert.Empty(db.Centers);
        Assert.Equal("Previewed", db.ImportBatches.Single().Status);
    }

    [Fact]
    public async Task Confirm_is_organization_scoped()
    {
        await using var db = Db(); Seed(db); await db.SaveChangesAsync();
        var rows = EmptyWorkbook();
        rows["Centers"] = new[]
        {
            HeadersFor("Centers"),
            new[] { "C01", "Center", "2026-01-01", "", "true" }
        };
        var service = new V180MasterDataBulkWorkbookService(db);
        var preview = await service.PreviewAsync(Admin(), Workbook(rows), default);
        var other = new CurrentUserDto(901, "ADMIN2", "Admin 2", "admin2@example.test", 2, null, null, ["admin"]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ConfirmAsync(other, preview.ImportBatchId, default));

        Assert.Equal("BULK_BATCH_NOT_FOUND", ex.Message);
        Assert.Empty(db.Centers);
        Assert.Equal("Previewed", db.ImportBatches.Single().Status);
    }

    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"v180-bulk-{Guid.NewGuid()}").Options);
    private static CurrentUserDto Admin() => new(900, "ADMIN", "Admin", "admin@example.test", 1, null, null, ["admin"]);
    private static Task<ImportPreviewDto> Preview(AppDbContext db, Dictionary<string, string[][]> rows) => new V180MasterDataBulkWorkbookService(db).PreviewAsync(Admin(), Workbook(rows), default);
    private static Dictionary<string, string[][]> StagedBase()
    {
        var rows = EmptyWorkbook();
        rows["EmploymentStatus"] = new[] { HeadersFor("EmploymentStatus"), new[] { "E01", "Active", "2026-01-01", "" } };
        rows["Centers"] = new[] { HeadersFor("Centers"), new[] { "C01", "Center", "2026-01-01", "", "true" } };
        rows["TeamCenters"] = new[] { HeadersFor("TeamCenters"), new[] { "T01", "C01", "2026-01-01", "" } };
        rows["DeploymentSites"] = new[] { HeadersFor("DeploymentSites"), new[] { "C01", "S01", "Site", "L01", "2026-01-01", "", "true" } };
        return rows;
    }
    private static Dictionary<string, string[][]> EmptyWorkbook() => SheetNames.ToDictionary(x => x, x => new[] { HeadersFor(x) });
    private static string[] HeadersFor(string name) => name switch { "EmploymentStatus" => ["EmployeeNo", "Status", "EffectiveFrom", "EffectiveTo"], "Centers" => ["CenterCode", "CenterName", "EffectiveFrom", "EffectiveTo", "IsActive"], "TeamCenters" => ["TeamCode", "CenterCode", "EffectiveFrom", "EffectiveTo"], "DeploymentSites" => ["CenterCode", "SiteCode", "SiteName", "LocationCode", "EffectiveFrom", "EffectiveTo", "IsActive"], "TeamSites" => ["TeamCode", "SiteCode", "EffectiveFrom", "EffectiveTo"], _ => ["EmployeeNo", "SiteCode", "IsPrimary", "EffectiveFrom", "EffectiveTo"] };
    private static byte[] Workbook(Dictionary<string, string[][]> rows)
    {
        using var stream = new MemoryStream(); using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true)) { var workbook = doc.AddWorkbookPart(); workbook.Workbook = new Workbook(); var sheets = workbook.Workbook.AppendChild(new Sheets()); uint id = 1; foreach (var pair in rows) { var part = workbook.AddNewPart<WorksheetPart>(); var data = new SheetData(); foreach (var values in pair.Value) { var row = new Row(); foreach (var value in values) row.Append(new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value)) }); data.Append(row); } part.Worksheet = new Worksheet(data); sheets.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = id++, Name = pair.Key }); } workbook.Workbook.Save(); } return stream.ToArray();
    }
    private static string[] Headers(SpreadsheetDocument book, string name) { var workbook = book.WorkbookPart!; var sheet = workbook.Workbook.Sheets!.Elements<Sheet>().Single(x => x.Name!.Value == name); var part = (WorksheetPart)workbook.GetPartById(sheet.Id!); return part.Worksheet.Descendants<Row>().First().Elements<Cell>().Select(x => x.InlineString!.Text!.Text).ToArray(); }
    private static void Seed(AppDbContext db)
    {
        db.Teams.Add(new Team { TeamId = 10, OrganizationId = 1, TeamCode = "T01", TeamName = "Team", IsActive = true, EffectiveFrom = new DateOnly(2020, 1, 1) });
        db.Employments.Add(new Employment { EmploymentId = 100, PersonId = 1, OrganizationId = 1, EmployeeNo = "E01", SourceType = "Test" });
        db.TeamMemberships.Add(new TeamMembership { TeamMembershipId = 1, EmploymentId = 100, TeamId = 10, EffectiveFrom = new DateOnly(2020, 1, 1) });
        db.Locations.Add(new FieldVisit.Domain.Entities.Location { LocationId = 20, OrganizationId = 1, LocationCode = "L01", LocationName = "Location", IsActive = true, ApprovalStatus = "Approved", CreatedAt = DateTime.UtcNow });
    }

    private static void SeedExistingCoverage(AppDbContext db)
    {
        Seed(db);
        db.Centers.Add(new Center { CenterId = 30, OrganizationId = 1, CenterCode = "C01", CenterName = "Center", EffectiveFrom = new DateOnly(2020, 1, 1), IsActive = true, RowVersion = [3] });
        db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod { EmploymentStatusPeriodId = 1, EmploymentId = 100, EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = new DateOnly(2020, 1, 1), SourceType = "Test" });
        db.TeamCenterAssignments.Add(new TeamCenterAssignment { TeamCenterAssignmentId = 2, TeamId = 10, CenterId = 30, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = DateTime.UtcNow, RowVersion = [2] });
        db.DeploymentSites.Add(new DeploymentSite { DeploymentSiteId = 40, CenterId = 30, SiteCode = "S01", SiteName = "Site", EffectiveFrom = new DateOnly(2020, 1, 1), IsActive = true, CreatedAt = DateTime.UtcNow, RowVersion = [4] });
        db.DeploymentSiteLocationAssignments.Add(new DeploymentSiteLocationAssignment { DeploymentSiteLocationAssignmentId = 3, DeploymentSiteId = 40, LocationId = 20, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = DateTime.UtcNow });
        db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment { TeamDeploymentSiteAssignmentId = 4, TeamId = 10, DeploymentSiteId = 40, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = DateTime.UtcNow });
        db.EmploymentDeploymentSiteAssignments.Add(new EmploymentDeploymentSiteAssignment { EmploymentDeploymentSiteAssignmentId = 5, EmploymentId = 100, DeploymentSiteId = 40, IsPrimary = true, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = DateTime.UtcNow });
    }
}
