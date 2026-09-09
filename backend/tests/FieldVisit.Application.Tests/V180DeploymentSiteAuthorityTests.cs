using System.Reflection;
using FieldVisit.Api.Controllers;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180DeploymentSiteAuthorityTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 12, 31);
    private static readonly byte[] Version = [1,2,3,4,5,6,7,8];
    private static string Token => Convert.ToBase64String(Version);
    private static CurrentUserDto Admin(int org = 1) => new(99, "ADMIN", "Admin", null, org, null, null, ["admin"]);
    private static AppDbContext MemoryDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public void Ef_model_discovers_all_1800_003_authoritative_tables_and_rowversions()
    {
        using var db = MemoryDb();
        foreach (var type in new[] { typeof(DeploymentSite), typeof(DeploymentSiteLocationAssignment),
            typeof(TeamDeploymentSiteAssignment), typeof(EmploymentDeploymentSiteAssignment) })
        {
            var entity = db.Model.FindEntityType(type);
            Assert.NotNull(entity);
            var version = entity!.FindProperty("RowVersion");
            Assert.NotNull(version);
            Assert.True(version!.IsConcurrencyToken);
        }
        Assert.Equal("DeploymentSites", db.Model.FindEntityType(typeof(DeploymentSite))!.GetTableName());
        Assert.Equal("DeploymentSiteLocationAssignments", db.Model.FindEntityType(typeof(DeploymentSiteLocationAssignment))!.GetTableName());
        Assert.Equal("TeamDeploymentSiteAssignments", db.Model.FindEntityType(typeof(TeamDeploymentSiteAssignment))!.GetTableName());
        Assert.Equal("EmploymentDeploymentSiteAssignments", db.Model.FindEntityType(typeof(EmploymentDeploymentSiteAssignment))!.GetTableName());
    }

    [Fact]
    public void Deployment_lifecycle_boundaries_are_inclusive()
    {
        Assert.True(V180DeploymentSiteRules.IsEffective(Start, End, Start));
        Assert.True(V180DeploymentSiteRules.IsEffective(Start, End, End));
        Assert.True(V180DeploymentSiteRules.Overlaps(Start, End, End, null));
        Assert.True(V180DeploymentSiteRules.IsWithin(Start, End, Start, End));
    }

    [Fact]
    public async Task Site_create_is_center_scoped_and_period_contained()
    {
        await using var db = MemoryDb();
        db.Organizations.Add(Organization(1));
        db.Centers.Add(Center(10, 1));
        await db.SaveChangesAsync();
        var writer = new V180DeploymentSiteWriter(db);
        var created = await writer.CreateSiteAsync(Admin(), new(10, " s1 ", " Site One ", Start, End), default);
        Assert.Equal("S1", created.Code);
        Assert.Equal(10, created.CenterId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CreateSiteAsync(
            Admin(), new(10, "S2", "Outside", Start.AddDays(-1), End), default));
    }

    [Fact]
    public async Task Site_update_requires_valid_nonstale_rowversion()
    {
        await using var db = await SeedCoreAsync();
        var site = await db.Set<DeploymentSite>().SingleAsync(x => x.DeploymentSiteId == 100);
        site.RowVersion = Version.ToArray();
        var writer = new V180DeploymentSiteWriter(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.UpdateSiteAsync(Admin(), 100,
            new(10, "S1", "Site", Start, End, null, true, "not-base64"), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.UpdateSiteAsync(Admin(), 100,
            new(10, "S1", "Site", Start, End, null, true, Convert.ToBase64String(new byte[8])), default));
    }

    [Fact]
    public async Task Site_deactivation_is_blocked_by_effective_or_future_assignments()
    {
        await using var db = await SeedCoreAsync();
        var site = await db.Set<DeploymentSite>().SingleAsync(x => x.DeploymentSiteId == 100);
        site.RowVersion = Version.ToArray();
        db.Set<DeploymentSiteLocationAssignment>().Add(new DeploymentSiteLocationAssignment
        {
            DeploymentSiteLocationAssignmentId = 1, DeploymentSiteId = 100, LocationId = 200,
            EffectiveFrom = Start, EffectiveTo = End
        });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new V180DeploymentSiteWriter(db)
            .DeactivateSiteAsync(Admin(), 100, new(Start, Token), default));
    }

    [Fact]
    public async Task Location_assignment_preserves_history_rejects_overlap_and_cross_org_location()
    {
        await using var db = await SeedCoreAsync();
        db.Set<DeploymentSiteLocationAssignment>().Add(new DeploymentSiteLocationAssignment
        {
            DeploymentSiteLocationAssignmentId = 1, DeploymentSiteId = 100, LocationId = 200,
            EffectiveFrom = Start, EffectiveTo = End
        });
        db.Locations.Add(new Location { LocationId = 201, OrganizationId = 2, LocationName = "Other Org",
            IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var writer = new V180DeploymentSiteWriter(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CreateLocationAssignmentAsync(
            Admin(), new(100, 200, End), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CreateLocationAssignmentAsync(
            Admin(), new(100, 201, Start), default));
        Assert.Null(typeof(V180UpdateDeploymentSiteLocationAssignmentRequest).GetProperty("LocationId"));
    }

    [Fact]
    public async Task Team_site_requires_same_org_and_full_team_center_coverage()
    {
        await using var db = await SeedCoreAsync();
        var writer = new V180DeploymentSiteWriter(db);
        var valid = await writer.CreateTeamAssignmentAsync(Admin(), new(300, 100, Start, End), default);
        Assert.True(valid.AssignmentId > 0);
        db.Teams.Add(new Team { TeamId = 301, OrganizationId = 1, TeamCode = "T2", TeamName = "No Center",
            EffectiveFrom = Start, EffectiveTo = End, IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Teams.Add(new Team { TeamId = 302, OrganizationId = 2, TeamCode = "T3", TeamName = "Other Org",
            EffectiveFrom = Start, EffectiveTo = End, IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CreateTeamAssignmentAsync(
            Admin(), new(301, 100, Start, End), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => writer.CreateTeamAssignmentAsync(
            Admin(), new(302, 100, Start, End), default));
    }

    [Fact]
    public async Task Employment_site_is_org_scoped_and_only_one_overlapping_primary_is_allowed()
    {
        await using var db = await SeedCoreAsync();
        db.Set<DeploymentSite>().Add(new DeploymentSite
        {
            DeploymentSiteId = 101, CenterId = 10, SiteCode = "S2", SiteName = "Site 2",
            EffectiveFrom = Start, EffectiveTo = End, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        db.Set<EmploymentDeploymentSiteAssignment>().Add(new EmploymentDeploymentSiteAssignment
        {
            EmploymentDeploymentSiteAssignmentId = 1, EmploymentId = 400, DeploymentSiteId = 100,
            IsPrimary = true, EffectiveFrom = Start, EffectiveTo = End
        });
        db.Persons.Add(new Person { PersonId = 501, DisplayName = "Other", CreatedAt = DateTime.UtcNow });
        db.Employments.Add(new Employment { EmploymentId = 401, PersonId = 501, OrganizationId = 2,
            EmployeeNo = "E2", SourceType = "Test", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var writer = new V180DeploymentSiteWriter(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CreateEmploymentAssignmentAsync(
            Admin(), new(400, 101, true, End), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => writer.CreateEmploymentAssignmentAsync(
            Admin(), new(401, 100, false, Start), default));
    }

    [Fact]
    public async Task Reader_is_organization_scoped_and_fails_closed_on_ambiguous_current_location()
    {
        await using var db = await SeedCoreAsync();
        var reader = new V180DeploymentSiteReader(db);
        Assert.NotNull(await reader.GetAsync(Admin(), 100, Start, default));
        Assert.Null(await reader.GetAsync(Admin(2), 100, Start, default));
        db.Set<DeploymentSiteLocationAssignment>().AddRange(
            new DeploymentSiteLocationAssignment { DeploymentSiteLocationAssignmentId = 1, DeploymentSiteId = 100,
                LocationId = 200, EffectiveFrom = Start, EffectiveTo = End },
            new DeploymentSiteLocationAssignment { DeploymentSiteLocationAssignmentId = 2, DeploymentSiteId = 100,
                LocationId = 200, EffectiveFrom = Start.AddDays(1), EffectiveTo = End });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetAsync(Admin(), 100, Start.AddDays(2), default));
    }

    [Fact]
    public void Admin_routes_are_additive_under_v180()
    {
        var controller = typeof(V180DeploymentSiteAdminController);
        foreach (var (method, route) in new[] {
            ("Search", "deployment-sites"), ("Get", "deployment-sites/{deploymentSiteId:int}"),
            ("LocationAssignments", "deployment-sites/{deploymentSiteId:int}/location-assignments"),
            ("TeamAssignments", "deployment-sites/{deploymentSiteId:int}/team-assignments"),
            ("EmploymentAssignments", "deployment-sites/{deploymentSiteId:int}/employment-assignments"),
            ("CreateSite", "deployment-sites"), ("UpdateSite", "deployment-sites/{deploymentSiteId:int}"),
            ("DeactivateSite", "deployment-sites/{deploymentSiteId:int}/deactivate"),
            ("CreateLocationAssignment", "deployment-site-location-assignments"),
            ("UpdateLocationAssignment", "deployment-site-location-assignments/{assignmentId:long}"),
            ("EndLocationAssignment", "deployment-site-location-assignments/{assignmentId:long}/end"),
            ("CreateTeamAssignment", "team-deployment-site-assignments"),
            ("UpdateTeamAssignment", "team-deployment-site-assignments/{assignmentId:long}"),
            ("EndTeamAssignment", "team-deployment-site-assignments/{assignmentId:long}/end"),
            ("CreateEmploymentAssignment", "employment-deployment-site-assignments"),
            ("UpdateEmploymentAssignment", "employment-deployment-site-assignments/{assignmentId:long}"),
            ("EndEmploymentAssignment", "employment-deployment-site-assignments/{assignmentId:long}/end") })
        {
            var attr = controller.GetMethod(method)!.GetCustomAttributes<HttpMethodAttribute>().Single();
            Assert.Equal(route, attr.Template);
        }
    }

    private static async Task<AppDbContext> SeedCoreAsync()
    {
        var db = MemoryDb();
        db.Organizations.AddRange(Organization(1), Organization(2));
        db.Centers.Add(Center(10, 1));
        db.Set<DeploymentSite>().Add(new DeploymentSite
        {
            DeploymentSiteId = 100, CenterId = 10, SiteCode = "S1", SiteName = "Site",
            EffectiveFrom = Start, EffectiveTo = End, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        db.Locations.Add(new Location { LocationId = 200, OrganizationId = 1, LocationCode = "L1",
            LocationName = "Office", Address = "A", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Teams.Add(new Team { TeamId = 300, OrganizationId = 1, TeamCode = "T1", TeamName = "Team",
            EffectiveFrom = Start, EffectiveTo = End, IsActive = true, CreatedAt = DateTime.UtcNow });
        db.TeamCenterAssignments.Add(new TeamCenterAssignment { TeamCenterAssignmentId = 1,
            TeamId = 300, CenterId = 10, EffectiveFrom = Start, EffectiveTo = End, CreatedAt = DateTime.UtcNow });
        db.Persons.Add(new Person { PersonId = 500, DisplayName = "Person", CreatedAt = DateTime.UtcNow });
        db.Employments.Add(new Employment { EmploymentId = 400, PersonId = 500, OrganizationId = 1,
            EmployeeNo = "E1", SourceType = "Test", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return db;
    }

    private static Organization Organization(int id) => new()
    {
        OrganizationId = id, OrganizationCode = $"O{id}", OrganizationName = $"Org {id}",
        IsActive = true, CreatedAt = DateTime.UtcNow
    };

    private static Center Center(int id, int org) => new()
    {
        CenterId = id, OrganizationId = org, CenterCode = $"C{id}", CenterName = $"Center {id}",
        EffectiveFrom = Start, EffectiveTo = End, IsActive = true, CreatedAt = DateTime.UtcNow
    };
}
