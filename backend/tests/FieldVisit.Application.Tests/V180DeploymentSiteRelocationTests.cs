using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180DeploymentSiteRelocationTests
{
    private static AppDbContext MemoryDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CurrentUserDto Admin() =>
        new(1, "admin01", "Admin", null, 1, null, null, ["admin"]);

    [Fact]
    public async Task Relocation_closes_old_assignment_and_creates_new_period()
    {
        await using var db = MemoryDb();
        db.Centers.Add(new Center
        {
            CenterId = 1,
            OrganizationId = 1,
            CenterCode = "CENTER",
            CenterName = "Center",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            RowVersion = [1]
        });
        db.DeploymentSites.Add(new DeploymentSite
        {
            DeploymentSiteId = 1,
            CenterId = 1,
            SiteCode = "SITE",
            SiteName = "Site",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            RowVersion = [2]
        });
        db.Locations.AddRange(
            new Location
            {
                LocationId = 1,
                OrganizationId = 1,
                LocationCode = "LOC-OLD",
                LocationName = "Old",
                IsTemporary = false,
                ApprovalStatus = "Approved",
                IsActive = true
            },
            new Location
            {
                LocationId = 2,
                OrganizationId = 1,
                LocationCode = "LOC-NEW",
                LocationName = "New",
                IsTemporary = false,
                ApprovalStatus = "Approved",
                IsActive = true
            });
        db.DeploymentSiteLocationAssignments.Add(new DeploymentSiteLocationAssignment
        {
            DeploymentSiteLocationAssignmentId = 1,
            DeploymentSiteId = 1,
            LocationId = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            CreatedAt = DateTime.UtcNow,
            RowVersion = [3]
        });
        await db.SaveChangesAsync();

        var repo = new V180MasterDataAdminRepository(db);
        var result = await repo.RelocateDeploymentSiteAsync(
            Admin(),
            1,
            new V180DeploymentSiteRelocationInput(
                "LOC-NEW",
                new DateOnly(2026, 10, 8),
                "Office moved",
                Convert.ToBase64String([2])),
            default);

        Assert.Equal("LOC-NEW", result.ReferenceKey);
        var assignments = await db.DeploymentSiteLocationAssignments
            .OrderBy(x => x.EffectiveFrom)
            .ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.Equal(new DateOnly(2026, 10, 7), assignments[0].EffectiveTo);
        Assert.Equal(2, assignments[1].LocationId);
        Assert.Equal(new DateOnly(2026, 10, 8), assignments[1].EffectiveFrom);
        Assert.Equal("Office moved", assignments[1].ChangeReason);
    }

    [Fact]
    public async Task Relocation_rejects_future_assignment_to_prevent_history_rewrite()
    {
        await using var db = MemoryDb();
        db.Centers.Add(new Center
        {
            CenterId = 1,
            OrganizationId = 1,
            CenterCode = "CENTER",
            CenterName = "Center",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true
        });
        db.DeploymentSites.Add(new DeploymentSite
        {
            DeploymentSiteId = 1,
            CenterId = 1,
            SiteCode = "SITE",
            SiteName = "Site",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            RowVersion = [2]
        });
        db.Locations.AddRange(
            new Location { LocationId = 1, OrganizationId = 1, LocationCode = "LOC-1", LocationName = "One", IsActive = true, ApprovalStatus = "Approved" },
            new Location { LocationId = 2, OrganizationId = 1, LocationCode = "LOC-2", LocationName = "Two", IsActive = true, ApprovalStatus = "Approved" });
        db.DeploymentSiteLocationAssignments.AddRange(
            new DeploymentSiteLocationAssignment { DeploymentSiteLocationAssignmentId = 1, DeploymentSiteId = 1, LocationId = 1, EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 11, 30) },
            new DeploymentSiteLocationAssignment { DeploymentSiteLocationAssignmentId = 2, DeploymentSiteId = 1, LocationId = 2, EffectiveFrom = new DateOnly(2026, 12, 1) });
        await db.SaveChangesAsync();

        var repo = new V180MasterDataAdminRepository(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.RelocateDeploymentSiteAsync(
                Admin(),
                1,
                new V180DeploymentSiteRelocationInput(
                    "LOC-2",
                    new DateOnly(2026, 10, 8),
                    "Unexpected backfill",
                    Convert.ToBase64String([2])),
                default));
    }
}
