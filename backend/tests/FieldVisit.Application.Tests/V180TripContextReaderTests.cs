using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180TripContextReaderTests
{
    [Fact]
    public async Task ResolveAsync_returns_only_intersection_of_employment_and_team_sites()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"trip-context-{Guid.NewGuid()}")
            .Options;
        await using var db = new AppDbContext(options);
        var day = new DateOnly(2026, 9, 29);

        db.UserIdentityProfiles.Add(new UserIdentityProfile
        {
            UserId = 1, EmploymentId = 100, UserType = UserTypes.Internal,
            UserCode = "E001", IdentityProvider = "Demo"
        });
        db.Employments.Add(new Employment
        {
            EmploymentId = 100, PersonId = 500, OrganizationId = 1,
            EmployeeNo = "E001", SourceType = "UAT"
        });
        db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod
        {
            EmploymentStatusPeriodId = 1, EmploymentId = 100,
            EmploymentStatus = EmploymentStatuses.Active,
            EffectiveFrom = day.AddDays(-1), SourceType = "UAT"
        });
        db.Teams.Add(new Team
        {
            TeamId = 10, OrganizationId = 1, TeamCode = "T10", TeamName = "Team 10",
            IsActive = true, EffectiveFrom = day.AddDays(-10)
        });
        db.TeamMemberships.Add(new TeamMembership
        {
            TeamMembershipId = 1, EmploymentId = 100, TeamId = 10,
            IsPrimary = true, EffectiveFrom = day.AddDays(-10)
        });
        db.Centers.AddRange(
            new Center
            {
                CenterId = 7, OrganizationId = 1, CenterCode = "C07", CenterName = "Center 7",
                EffectiveFrom = day.AddDays(-10), IsActive = true
            },
            new Center
            {
                CenterId = 8, OrganizationId = 1, CenterCode = "C08", CenterName = "Center 8",
                EffectiveFrom = day.AddDays(-10), IsActive = true
            });
        db.TeamCenterAssignments.Add(new TeamCenterAssignment
        {
            TeamCenterAssignmentId = 1, TeamId = 10, CenterId = 7,
            EffectiveFrom = day.AddDays(-10), CreatedAt = DateTime.UtcNow
        });
        db.DeploymentSites.AddRange(
            new DeploymentSite { DeploymentSiteId = 101, CenterId = 7, SiteCode = "S101", SiteName = "Start", EffectiveFrom = day.AddDays(-10), IsActive = true },
            new DeploymentSite { DeploymentSiteId = 102, CenterId = 7, SiteCode = "S102", SiteName = "End", EffectiveFrom = day.AddDays(-10), IsActive = true },
            new DeploymentSite { DeploymentSiteId = 201, CenterId = 8, SiteCode = "S201", SiteName = "Other center", EffectiveFrom = day.AddDays(-10), IsActive = true },
            new DeploymentSite { DeploymentSiteId = 999, CenterId = 7, SiteCode = "S999", SiteName = "Employment only", EffectiveFrom = day.AddDays(-10), IsActive = true });
        db.EmploymentDeploymentSiteAssignments.AddRange(
            new EmploymentDeploymentSiteAssignment { EmploymentDeploymentSiteAssignmentId = 1, EmploymentId = 100, DeploymentSiteId = 101, IsPrimary = true, EffectiveFrom = day.AddDays(-10) },
            new EmploymentDeploymentSiteAssignment { EmploymentDeploymentSiteAssignmentId = 2, EmploymentId = 100, DeploymentSiteId = 102, EffectiveFrom = day.AddDays(-10) },
            new EmploymentDeploymentSiteAssignment { EmploymentDeploymentSiteAssignmentId = 3, EmploymentId = 100, DeploymentSiteId = 999, EffectiveFrom = day.AddDays(-10) });
        db.TeamDeploymentSiteAssignments.AddRange(
            new TeamDeploymentSiteAssignment { TeamDeploymentSiteAssignmentId = 1, TeamId = 10, DeploymentSiteId = 101, EffectiveFrom = day.AddDays(-10) },
            new TeamDeploymentSiteAssignment { TeamDeploymentSiteAssignmentId = 2, TeamId = 10, DeploymentSiteId = 102, EffectiveFrom = day.AddDays(-10) });
        db.Locations.AddRange(
            new Location { LocationId = 201, OrganizationId = 1, LocationCode = "L201", LocationName = "Start", Address = "Start address", IsActive = true, ApprovalStatus = "Approved" },
            new Location { LocationId = 202, OrganizationId = 1, LocationCode = "L202", LocationName = "End", Address = "End address", IsActive = true, ApprovalStatus = "Approved" },
            new Location { LocationId = 203, OrganizationId = 1, LocationCode = "L203", LocationName = "Other center", Address = "Other center address", IsActive = true, ApprovalStatus = "Approved" });
        db.DeploymentSiteLocationAssignments.AddRange(
            new DeploymentSiteLocationAssignment { DeploymentSiteLocationAssignmentId = 1, DeploymentSiteId = 101, LocationId = 201, EffectiveFrom = day.AddDays(-10) },
            new DeploymentSiteLocationAssignment { DeploymentSiteLocationAssignmentId = 2, DeploymentSiteId = 102, LocationId = 202, EffectiveFrom = day.AddDays(-10) },
            new DeploymentSiteLocationAssignment { DeploymentSiteLocationAssignmentId = 3, DeploymentSiteId = 201, LocationId = 203, EffectiveFrom = day.AddDays(-10) });
        await db.SaveChangesAsync();

        var user = new CurrentUserDto(
            1, "E001", "Visitor", "visitor@example.test", 1, 10, "Team 10",
            ["visitor"], [new TeamScopeDto(10, "Team 10", true)]);
        var context = await new V180TripContextReader(db).ResolveAsync(user, day, null, default);

        Assert.True(context.EligibleForTrip);
        Assert.Equal(100, context.EmploymentId);
        Assert.Equal(10, context.SelectedTeamId);
        Assert.Equal([101, 102], context.EligibleDeploymentSites.Select(x => x.DeploymentSiteId));
        Assert.Equal([101, 102, 201], context.OfficialDeploymentSites!.Select(x => x.DeploymentSiteId));
        Assert.Equal(101, context.PrimaryDeploymentSiteId);
        Assert.Equal(101, context.DefaultStartDeploymentSiteId);
        Assert.Equal(101, context.DefaultEndDeploymentSiteId);
    }

    [Fact]
    public async Task Trip_context_rejects_inactive_team()
    {
        var (db, user, day) = await SeedEligibleContextAsync();
        await using (db)
        {
            db.Teams.Single().IsActive = false;
            await db.SaveChangesAsync();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new V180TripContextReader(db).ResolveAsync(user, day, null, default));
            Assert.Contains("TRIP_CONTEXT_TEAM_INVALID", ex.Message);
        }
    }

    [Theory]
    [InlineData("center")]
    [InlineData("site")]
    [InlineData("inactive-location")]
    [InlineData("unapproved-location")]
    public async Task Trip_context_excludes_lifecycle_ineligible_site(string kind)
    {
        var (db, user, day) = await SeedEligibleContextAsync();
        await using (db)
        {
            switch (kind)
            {
                case "center": db.Centers.Single().IsActive = false; break;
                case "site": db.DeploymentSites.Single().IsActive = false; break;
                case "inactive-location": db.Locations.Single().IsActive = false; break;
                case "unapproved-location": db.Locations.Single().ApprovalStatus = "Pending"; break;
            }
            await db.SaveChangesAsync();

            var context = await new V180TripContextReader(db).ResolveAsync(user, day, null, default);
            Assert.False(context.EligibleForTrip);
            Assert.Empty(context.EligibleDeploymentSites);
        }
    }

    [Fact]
    public async Task Trip_context_rejects_team_center_site_center_mismatch()
    {
        var (db, user, day) = await SeedEligibleContextAsync();
        await using (db)
        {
            db.TeamCenterAssignments.Single().CenterId = 8;
            await db.SaveChangesAsync();

            var context = await new V180TripContextReader(db).ResolveAsync(user, day, null, default);
            Assert.False(context.EligibleForTrip);
            Assert.Empty(context.EligibleDeploymentSites);
        }
    }

    private static async Task<(AppDbContext Db, CurrentUserDto User, DateOnly Day)> SeedEligibleContextAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"trip-context-c6-{Guid.NewGuid()}")
            .Options;
        var db = new AppDbContext(options);
        var day = new DateOnly(2026, 9, 29);
        db.UserIdentityProfiles.Add(new UserIdentityProfile { UserId = 1, EmploymentId = 100, UserType = UserTypes.Internal, UserCode = "E001", IdentityProvider = "Demo" });
        db.Employments.Add(new Employment { EmploymentId = 100, PersonId = 500, OrganizationId = 1, EmployeeNo = "E001", SourceType = "UAT" });
        db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod { EmploymentStatusPeriodId = 1, EmploymentId = 100, EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = day.AddDays(-1), SourceType = "UAT" });
        db.Teams.Add(new Team { TeamId = 10, OrganizationId = 1, TeamCode = "T10", TeamName = "Team 10", IsActive = true, EffectiveFrom = day.AddDays(-10) });
        db.TeamMemberships.Add(new TeamMembership { TeamMembershipId = 1, EmploymentId = 100, TeamId = 10, IsPrimary = true, EffectiveFrom = day.AddDays(-10) });
        db.Centers.Add(new Center { CenterId = 7, OrganizationId = 1, CenterCode = "C07", CenterName = "Center 7", IsActive = true, EffectiveFrom = day.AddDays(-10) });
        db.TeamCenterAssignments.Add(new TeamCenterAssignment { TeamCenterAssignmentId = 1, TeamId = 10, CenterId = 7, EffectiveFrom = day.AddDays(-10), CreatedAt = DateTime.UtcNow });
        db.DeploymentSites.Add(new DeploymentSite { DeploymentSiteId = 101, CenterId = 7, SiteCode = "S101", SiteName = "Site", IsActive = true, EffectiveFrom = day.AddDays(-10) });
        db.EmploymentDeploymentSiteAssignments.Add(new EmploymentDeploymentSiteAssignment { EmploymentDeploymentSiteAssignmentId = 1, EmploymentId = 100, DeploymentSiteId = 101, IsPrimary = true, EffectiveFrom = day.AddDays(-10) });
        db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment { TeamDeploymentSiteAssignmentId = 1, TeamId = 10, DeploymentSiteId = 101, EffectiveFrom = day.AddDays(-10) });
        db.Locations.Add(new Location { LocationId = 201, OrganizationId = 1, LocationCode = "L201", LocationName = "Location", IsActive = true, ApprovalStatus = "Approved" });
        db.DeploymentSiteLocationAssignments.Add(new DeploymentSiteLocationAssignment { DeploymentSiteLocationAssignmentId = 1, DeploymentSiteId = 101, LocationId = 201, EffectiveFrom = day.AddDays(-10) });
        await db.SaveChangesAsync();
        return (db, new CurrentUserDto(1, "E001", "Visitor", "visitor@example.test", 1, 10, "Team 10", ["visitor"], [new TeamScopeDto(10, "Team 10", true)]), day);
    }
}
