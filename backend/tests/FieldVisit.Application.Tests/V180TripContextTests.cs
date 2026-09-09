using System.Reflection;
using FieldVisit.Api.Controllers;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180TripContextTests
{
    private static readonly DateOnly VisitDate = new(2026, 9, 9);
    private static CurrentUserDto Visitor(int organizationId = 1) =>
        new(1, "E001", "Visitor", null, organizationId, null, null, ["visitor"]);
    private static AppDbContext MemoryDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task UserId_maps_to_profile_EmploymentId_without_name_or_email_matching()
    {
        await using var db = await SeedAsync();
        var result = await new V180TripContextReader(db).ResolveAsync(Visitor(), VisitDate, null, default);

        Assert.Equal(10, result.EmploymentId);
        Assert.Equal(100, result.SelectedTeamId);
        Assert.True(result.EligibleForTrip);
    }

    [Fact]
    public async Task Missing_or_cross_organization_Employment_fails_closed()
    {
        await using var missing = await SeedAsync();
        missing.UserIdentityProfiles.Single().EmploymentId = null;
        await missing.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new V180TripContextReader(missing).ResolveAsync(Visitor(), VisitDate, null, default));
        Assert.Contains("TRIP_CONTEXT_EMPLOYMENT_MISSING", ex.Message);

        await using var crossOrg = await SeedAsync();
        var mismatch = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new V180TripContextReader(crossOrg).ResolveAsync(Visitor(2), VisitDate, null, default));
        Assert.Contains("TRIP_CONTEXT_ORGANIZATION_MISMATCH", mismatch.Message);
    }

    [Theory]
    [InlineData(EmploymentStatuses.Leave)]
    [InlineData(EmploymentStatuses.Terminated)]
    public async Task Non_active_status_on_VisitDate_is_not_eligible(string status)
    {
        await using var db = await SeedAsync();
        db.EmploymentStatusPeriods.Single().EmploymentStatus = status;
        await db.SaveChangesAsync();

        var result = await new V180TripContextReader(db).ResolveAsync(Visitor(), VisitDate, null, default);

        Assert.False(result.EligibleForTrip);
        Assert.Equal("EMPLOYMENT_NOT_ACTIVE", result.ValidationCode);
    }

    [Fact]
    public async Task EffectiveFrom_and_EffectiveTo_boundaries_are_inclusive()
    {
        await using var db = await SeedAsync();
        foreach (var status in db.EmploymentStatusPeriods) { status.EffectiveFrom = VisitDate; status.EffectiveTo = VisitDate; }
        foreach (var membership in db.TeamMemberships) { membership.EffectiveFrom = VisitDate; membership.EffectiveTo = VisitDate; }
        foreach (var site in db.DeploymentSites) { site.EffectiveFrom = VisitDate; site.EffectiveTo = VisitDate; }
        foreach (var row in db.EmploymentDeploymentSiteAssignments) { row.EffectiveFrom = VisitDate; row.EffectiveTo = VisitDate; }
        foreach (var row in db.TeamDeploymentSiteAssignments) { row.EffectiveFrom = VisitDate; row.EffectiveTo = VisitDate; }
        foreach (var row in db.DeploymentSiteLocationAssignments) { row.EffectiveFrom = VisitDate; row.EffectiveTo = VisitDate; }
        await db.SaveChangesAsync();

        var result = await new V180TripContextReader(db).ResolveAsync(Visitor(), VisitDate, null, default);
        Assert.True(result.EligibleForTrip);
        Assert.Single(result.EligibleDeploymentSites);
    }

    [Fact]
    public async Task Team_membership_uses_VisitDate_and_does_not_expose_future_membership_early()
    {
        await using var db = await SeedAsync();
        db.Teams.Add(new Team { TeamId = 101, OrganizationId = 1, TeamCode = "T2", TeamName = "Future",
            EffectiveFrom = VisitDate.AddYears(-1), IsActive = true, CreatedAt = DateTime.UtcNow });
        db.TeamMemberships.Add(new TeamMembership { TeamMembershipId = 2, EmploymentId = 10, TeamId = 101,
            IsPrimary = true, EffectiveFrom = VisitDate.AddDays(1), CreatedAt = DateTime.UtcNow });
        db.TeamMemberships.Single(x => x.TeamId == 100).EffectiveTo = VisitDate;
        await db.SaveChangesAsync();

        var oldContext = await new V180TripContextReader(db).ResolveAsync(Visitor(), VisitDate, null, default);
        var futureContext = await new V180TripContextReader(db).ResolveAsync(Visitor(), VisitDate.AddDays(1), null, default);

        Assert.Equal(new[] { 100 }, oldContext.Teams.Select(x => x.TeamId));
        Assert.Equal(new[] { 101 }, futureContext.Teams.Select(x => x.TeamId));
    }

    [Fact]
    public async Task Multiple_teams_without_primary_require_explicit_selection_instead_of_guessing()
    {
        await using var db = await SeedAsync();
        db.TeamMemberships.Single().IsPrimary = false;
        db.Teams.Add(new Team { TeamId = 101, OrganizationId = 1, TeamCode = "T2", TeamName = "Second",
            EffectiveFrom = VisitDate.AddYears(-1), IsActive = true, CreatedAt = DateTime.UtcNow });
        db.TeamMemberships.Add(new TeamMembership { TeamMembershipId = 2, EmploymentId = 10, TeamId = 101,
            EffectiveFrom = VisitDate.AddYears(-1), CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await new V180TripContextReader(db).ResolveAsync(Visitor(), VisitDate, null, default);
        Assert.False(result.EligibleForTrip);
        Assert.Null(result.SelectedTeamId);
        Assert.Equal("TEAM_SELECTION_REQUIRED", result.ValidationCode);
    }

    [Fact]
    public async Task Eligible_site_is_Employment_and_Team_intersection_and_requires_effective_location()
    {
        await using var db = await SeedAsync();
        db.DeploymentSites.Add(Site(301, "S2"));
        db.EmploymentDeploymentSiteAssignments.Add(EmploymentSite(2, 301, false));
        db.DeploymentSiteLocationAssignments.Add(SiteLocation(2, 301, 400));
        await db.SaveChangesAsync();

        var result = await new V180TripContextReader(db).ResolveAsync(Visitor(), VisitDate, null, default);
        Assert.Equal(new[] { 300 }, result.EligibleDeploymentSites.Select(x => x.DeploymentSiteId));

        db.DeploymentSiteLocationAssignments.Remove(db.DeploymentSiteLocationAssignments.Single(x => x.DeploymentSiteId == 300));
        await db.SaveChangesAsync();
        var noLocation = await new V180TripContextReader(db).ResolveAsync(Visitor(), VisitDate, null, default);
        Assert.Empty(noLocation.EligibleDeploymentSites);
        Assert.Equal("NO_ELIGIBLE_DEPLOYMENT_SITE", noLocation.ValidationCode);
    }

    [Fact]
    public async Task Site_relocation_returns_location_and_address_as_of_VisitDate()
    {
        await using var db = await SeedAsync();
        var oldAssignment = db.DeploymentSiteLocationAssignments.Single();
        oldAssignment.EffectiveTo = VisitDate;
        db.Locations.Add(new Location { LocationId = 401, OrganizationId = 1, LocationCode = "L2",
            LocationName = "New office", Address = "New address", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.DeploymentSiteLocationAssignments.Add(new DeploymentSiteLocationAssignment
        {
            DeploymentSiteLocationAssignmentId = 2, DeploymentSiteId = 300, LocationId = 401,
            EffectiveFrom = VisitDate.AddDays(1), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var oldContext = await new V180TripContextReader(db).ResolveAsync(Visitor(), VisitDate, null, default);
        var newContext = await new V180TripContextReader(db).ResolveAsync(Visitor(), VisitDate.AddDays(1), null, default);
        Assert.Equal((400, "Old address"), (oldContext.EligibleDeploymentSites.Single().LocationId, oldContext.EligibleDeploymentSites.Single().Address));
        Assert.Equal((401, "New address"), (newContext.EligibleDeploymentSites.Single().LocationId, newContext.EligibleDeploymentSites.Single().Address));
    }

    [Fact]
    public async Task Exactly_one_eligible_primary_defaults_both_ends_and_no_primary_does_not_guess()
    {
        await using var db = await SeedAsync();
        var reader = new V180TripContextReader(db);
        var primary = await reader.ResolveAsync(Visitor(), VisitDate, null, default);
        Assert.Equal(300, primary.PrimaryDeploymentSiteId);
        Assert.Equal(300, primary.DefaultStartDeploymentSiteId);
        Assert.Equal(300, primary.DefaultEndDeploymentSiteId);

        db.EmploymentDeploymentSiteAssignments.Single().IsPrimary = false;
        await db.SaveChangesAsync();
        var noPrimary = await reader.ResolveAsync(Visitor(), VisitDate, null, default);
        Assert.Null(noPrimary.PrimaryDeploymentSiteId);
        Assert.Null(noPrimary.DefaultStartDeploymentSiteId);
        Assert.Null(noPrimary.DefaultEndDeploymentSiteId);
    }

    [Fact]
    public async Task Multiple_effective_primary_sites_fail_closed()
    {
        await using var db = await SeedAsync();
        db.DeploymentSites.Add(Site(301, "S2"));
        db.EmploymentDeploymentSiteAssignments.Add(EmploymentSite(2, 301, true));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new V180TripContextReader(db).ResolveAsync(Visitor(), VisitDate, null, default));
        Assert.Contains("AMBIGUOUS_PRIMARY_DEPLOYMENT_SITE", ex.Message);
    }

    [Fact]
    public async Task Cross_organization_site_fails_closed()
    {
        await using var db = await SeedAsync();
        db.Centers.Single().OrganizationId = 2;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new V180TripContextReader(db).ResolveAsync(Visitor(), VisitDate, null, default));
        Assert.Contains("TRIP_CONTEXT_SITE_ORGANIZATION_MISMATCH", ex.Message);
    }

    [Fact]
    public void Visitor_route_is_not_admin_and_existing_write_contracts_remain_unchanged()
    {
        var route = typeof(TripsController).GetMethod("Context")!
            .GetCustomAttributes<HttpMethodAttribute>().Single();
        Assert.Equal("trips/context", route.Template);
        Assert.DoesNotContain("admin", route.Template!, StringComparison.OrdinalIgnoreCase);

        Assert.Null(typeof(SaveTripRequest).GetProperty("StartDeploymentSiteId"));
        Assert.Null(typeof(SaveTripRequest).GetProperty("EndDeploymentSiteId"));
        var tripService = Source("backend/src/FieldVisit.Application/TripService.cs");
        Assert.DoesNotContain("StartDeploymentSiteId =", tripService);
        Assert.DoesNotContain("EndDeploymentSiteId =", tripService);
    }

    [Fact]
    public void AppDbContext_maps_exact_1800_003_Trip_and_Snapshot_fields()
    {
        using var db = MemoryDb();
        var trip = db.Model.FindEntityType(typeof(VisitTrip))!;
        Assert.NotNull(trip.FindProperty(nameof(VisitTrip.StartDeploymentSiteId)));
        Assert.NotNull(trip.FindProperty(nameof(VisitTrip.EndDeploymentSiteId)));
        Assert.Equal("DeploymentSites", trip.GetForeignKeys().Single(x =>
            x.Properties.Single().Name == nameof(VisitTrip.StartDeploymentSiteId)).PrincipalEntityType.GetTableName());
        Assert.Equal("DeploymentSites", trip.GetForeignKeys().Single(x =>
            x.Properties.Single().Name == nameof(VisitTrip.EndDeploymentSiteId)).PrincipalEntityType.GetTableName());

        var snapshot = db.Model.FindEntityType(typeof(VisitTripSnapshot))!;
        foreach (var property in new[]
        {
            "StartDeploymentSiteIdSnapshot", "StartDeploymentSiteCodeSnapshot", "StartDeploymentSiteNameSnapshot",
            "StartDeploymentLocationIdSnapshot", "StartDeploymentAddressSnapshot", "EndDeploymentSiteIdSnapshot",
            "EndDeploymentSiteCodeSnapshot", "EndDeploymentSiteNameSnapshot", "EndDeploymentLocationIdSnapshot",
            "EndDeploymentAddressSnapshot"
        }) Assert.NotNull(snapshot.FindProperty(property));
        Assert.Equal(50, snapshot.FindProperty("StartDeploymentSiteCodeSnapshot")!.GetMaxLength());
        Assert.Equal(200, snapshot.FindProperty("StartDeploymentSiteNameSnapshot")!.GetMaxLength());
        Assert.Equal(500, snapshot.FindProperty("StartDeploymentAddressSnapshot")!.GetMaxLength());
    }

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = MemoryDb();
        db.Organizations.AddRange(
            new Organization { OrganizationId = 1, OrganizationCode = "O1", OrganizationName = "Org 1", IsActive = true, CreatedAt = DateTime.UtcNow },
            new Organization { OrganizationId = 2, OrganizationCode = "O2", OrganizationName = "Org 2", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.UserIdentityProfiles.Add(new UserIdentityProfile { UserId = 1, EmploymentId = 10,
            UserType = UserTypes.Internal, UserCode = "U1", CreatedAt = DateTime.UtcNow });
        db.Persons.Add(new Person { PersonId = 20, DisplayName = "Visitor", CreatedAt = DateTime.UtcNow });
        db.Employments.Add(new Employment { EmploymentId = 10, PersonId = 20, OrganizationId = 1,
            EmployeeNo = "E001", SourceType = "Test", CreatedAt = DateTime.UtcNow });
        db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod { EmploymentStatusPeriodId = 1,
            EmploymentId = 10, EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = VisitDate.AddYears(-1),
            SourceType = "Test", CreatedAt = DateTime.UtcNow });
        db.Teams.Add(new Team { TeamId = 100, OrganizationId = 1, TeamCode = "T1", TeamName = "Team 1",
            EffectiveFrom = VisitDate.AddYears(-1), IsActive = true, CreatedAt = DateTime.UtcNow });
        db.TeamMemberships.Add(new TeamMembership { TeamMembershipId = 1, EmploymentId = 10, TeamId = 100,
            IsPrimary = true, EffectiveFrom = VisitDate.AddYears(-1), CreatedAt = DateTime.UtcNow });
        db.Centers.Add(new Center { CenterId = 200, OrganizationId = 1, CenterCode = "C1", CenterName = "Center 1",
            EffectiveFrom = VisitDate.AddYears(-1), IsActive = true, CreatedAt = DateTime.UtcNow });
        db.DeploymentSites.Add(Site(300, "S1"));
        db.Locations.Add(new Location { LocationId = 400, OrganizationId = 1, LocationCode = "L1",
            LocationName = "Old office", Address = "Old address", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.EmploymentDeploymentSiteAssignments.Add(EmploymentSite(1, 300, true));
        db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment { TeamDeploymentSiteAssignmentId = 1,
            TeamId = 100, DeploymentSiteId = 300, EffectiveFrom = VisitDate.AddYears(-1), CreatedAt = DateTime.UtcNow });
        db.DeploymentSiteLocationAssignments.Add(SiteLocation(1, 300, 400));
        await db.SaveChangesAsync();
        return db;
    }

    private static DeploymentSite Site(int id, string code) => new()
    {
        DeploymentSiteId = id, CenterId = 200, SiteCode = code, SiteName = $"Site {code}",
        EffectiveFrom = VisitDate.AddYears(-1), IsActive = true, CreatedAt = DateTime.UtcNow
    };

    private static EmploymentDeploymentSiteAssignment EmploymentSite(long id, int siteId, bool primary) => new()
    {
        EmploymentDeploymentSiteAssignmentId = id, EmploymentId = 10, DeploymentSiteId = siteId,
        IsPrimary = primary, EffectiveFrom = VisitDate.AddYears(-1), CreatedAt = DateTime.UtcNow
    };

    private static DeploymentSiteLocationAssignment SiteLocation(long id, int siteId, int locationId) => new()
    {
        DeploymentSiteLocationAssignmentId = id, DeploymentSiteId = siteId, LocationId = locationId,
        EffectiveFrom = VisitDate.AddYears(-1), CreatedAt = DateTime.UtcNow
    };

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "backend", "FieldVisitSystem.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relative));
    }
}
