using FieldVisit.Api.Controllers;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180MasterDataAdminTests
{
    private static readonly DateOnly Today = BusinessTime.Today;

    [Theory]
    [InlineData("visitor")]
    [InlineData("leader")]
    public async Task Service_rejects_non_admin_roles(string role)
    {
        var service = new V180MasterDataAdminService(
            new StubCurrentUserService(User(role)),
            new StubMasterDataRepository());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.ReadinessAsync(default));
    }

    [Fact]
    public async Task Service_allows_admin_and_forwards_organization_scoped_request()
    {
        var repository = new StubMasterDataRepository();
        var service = new V180MasterDataAdminService(
            new StubCurrentUserService(User("admin")),
            repository);

        await service.ReadinessAsync(default);

        Assert.Equal(1, repository.ReadinessCalls);
    }

    [Fact]
    public void Controller_is_restricted_to_admin_role()
    {
        var attribute = typeof(V180MasterDataAdminController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal("admin", attribute.Roles);
    }

    [Fact]
    public void RowVersion_is_required_and_stale_values_fail_closed()
    {
        var actual = new byte[] { 1, 2, 3, 4 };

        Assert.Contains(
            "ROWVERSION_REQUIRED",
            Assert.Throws<InvalidOperationException>(
                () => V180MasterDataValidationService.RowVersion(null, actual)).Message);
        Assert.Contains(
            "ROWVERSION_CONFLICT",
            Assert.Throws<InvalidOperationException>(
                () => V180MasterDataValidationService.RowVersion(
                    Convert.ToBase64String(new byte[] { 9 }),
                    actual)).Message);

        V180MasterDataValidationService.RowVersion(
            Convert.ToBase64String(actual),
            actual);
    }

    [Fact]
    public async Task Readiness_uses_active_employment_and_real_team_center_assignment()
    {
        await using var db = Db();
        SeedVisitorIdentity(db);
        await db.SaveChangesAsync();
        var repository = new V180MasterDataAdminRepository(db);

        var first = await repository.GetReadinessAsync(User("admin"), default);
        Assert.Equal(1, first.EmploymentStatusMissingCount);
        Assert.Equal(1, first.TeamCenterMissingCount);
        Assert.False(first.IsUatReady);

        db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod
        {
            EmploymentId = 100,
            EmploymentStatus = EmploymentStatuses.Active,
            EffectiveFrom = Today.AddDays(-10),
            SourceType = "UAT"
        });
        db.TeamCenterAssignments.Add(new TeamCenterAssignment
        {
            TeamId = 10,
            CenterId = 20,
            EffectiveFrom = Today.AddDays(-10),
            CreatedAt = DateTime.UtcNow
        });
        db.Locations.Add(new Location
        {
            LocationId = 30,
            OrganizationId = 1,
            LocationCode = "L30",
            LocationName = "Office",
            ApprovalStatus = "Approved",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.DeploymentSites.Add(new DeploymentSite
        {
            DeploymentSiteId = 40,
            CenterId = 20,
            SiteCode = "S40",
            SiteName = "Office",
            EffectiveFrom = Today.AddDays(-10),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.DeploymentSiteLocationAssignments.Add(new DeploymentSiteLocationAssignment
        {
            DeploymentSiteLocationAssignmentId = 50,
            DeploymentSiteId = 40,
            LocationId = 30,
            EffectiveFrom = Today.AddDays(-10),
            CreatedAt = DateTime.UtcNow
        });
        db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment
        {
            TeamDeploymentSiteAssignmentId = 60,
            TeamId = 10,
            DeploymentSiteId = 40,
            EffectiveFrom = Today.AddDays(-10),
            CreatedAt = DateTime.UtcNow
        });
        db.EmploymentDeploymentSiteAssignments.Add(new EmploymentDeploymentSiteAssignment
        {
            EmploymentDeploymentSiteAssignmentId = 70,
            EmploymentId = 100,
            DeploymentSiteId = 40,
            IsPrimary = true,
            EffectiveFrom = Today.AddDays(-10),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var ready = await repository.GetReadinessAsync(User("admin"), default);
        Assert.Equal(0, ready.EmploymentStatusMissingCount);
        Assert.Equal(0, ready.TeamCenterMissingCount);
        Assert.Equal(1, ready.DeploymentSiteCount);
        Assert.Equal(0, ready.TeamSiteMissingCount);
        Assert.Equal(0, ready.EmploymentSiteMissingCount);
        Assert.True(ready.IsUatReady);
    }

    [Fact]
    public async Task Employment_status_overlap_is_rejected()
    {
        await using var db = Db();
        SeedVisitorIdentity(db);
        db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod
        {
            EmploymentStatusPeriodId = 1,
            EmploymentId = 100,
            EmploymentStatus = EmploymentStatuses.Active,
            EffectiveFrom = Today.AddDays(-5),
            EffectiveTo = Today.AddDays(5),
            SourceType = "UAT"
        });
        await db.SaveChangesAsync();
        var repository = new V180MasterDataAdminRepository(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveEmploymentStatusAsync(
                User("admin"),
                null,
                new V180EmploymentStatusInput(
                    "E100",
                    EmploymentStatuses.Leave,
                    Today,
                    Today.AddDays(10)),
                default));

        Assert.Contains("OVERLAPPING_EMPLOYMENT_STATUS", ex.Message);
    }

    [Fact]
    public async Task Team_site_allows_same_team_to_use_different_sites_in_same_period()
    {
        await using var db = Db();
        SeedVisitorIdentity(db);
        SeedSite(db, 40, "S40", 30, "L30");
        SeedSite(db, 41, "S41", 31, "L31");
        db.TeamCenterAssignments.Add(new TeamCenterAssignment
        {
            TeamCenterAssignmentId = 1,
            TeamId = 10,
            CenterId = 20,
            EffectiveFrom = Today.AddDays(-30),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var repository = new V180MasterDataAdminRepository(db);

        await repository.SaveTeamSiteAsync(
            User("admin"), null,
            new V180TeamSiteInput("T10", "S40", Today, null), default);
        await repository.SaveTeamSiteAsync(
            User("admin"), null,
            new V180TeamSiteInput("T10", "S41", Today, null), default);

        Assert.Equal(2, await db.TeamDeploymentSiteAssignments.CountAsync());
    }

    [Fact]
    public async Task Team_site_same_site_overlap_is_rejected()
    {
        await using var db = Db();
        SeedVisitorIdentity(db);
        SeedSite(db, 40, "S40", 30, "L30");
        db.TeamCenterAssignments.Add(new TeamCenterAssignment
        {
            TeamCenterAssignmentId = 1,
            TeamId = 10,
            CenterId = 20,
            EffectiveFrom = Today.AddDays(-30),
            CreatedAt = DateTime.UtcNow
        });
        db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment
        {
            TeamDeploymentSiteAssignmentId = 1,
            TeamId = 10,
            DeploymentSiteId = 40,
            EffectiveFrom = Today.AddDays(-2),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var repository = new V180MasterDataAdminRepository(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveTeamSiteAsync(
                User("admin"), null,
                new V180TeamSiteInput("T10", "S40", Today, null), default));

        Assert.Contains("OVERLAPPING_TEAM_SITE", ex.Message);
    }

    [Fact]
    public async Task Deployment_site_create_writes_site_location_and_audit_together()
    {
        await using var db = Db();
        SeedVisitorIdentity(db);
        db.Locations.Add(new Location
        {
            LocationId = 30,
            OrganizationId = 1,
            LocationCode = "L30",
            LocationName = "Office",
            ApprovalStatus = "Approved",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var repository = new V180MasterDataAdminRepository(db);

        var result = await repository.SaveDeploymentSiteAsync(
            User("admin"),
            null,
            new V180DeploymentSiteInput(
                "C20", "S40", "Office", "L30",
                Today, null, true),
            default);

        Assert.Equal("S40", result.Key);
        var site = await db.DeploymentSites.SingleAsync();
        var link = await db.DeploymentSiteLocationAssignments.SingleAsync();
        Assert.Equal(site.DeploymentSiteId, link.DeploymentSiteId);
        Assert.Equal(30, link.LocationId);
        Assert.Contains(await db.AuditLogs.ToListAsync(), x =>
            x.EntityType == "DeploymentSite" && x.EntityId == "S40");
    }

    [Fact]
    public async Task Deployment_site_rejects_cross_organization_location()
    {
        await using var db = Db();
        SeedVisitorIdentity(db);
        db.Locations.Add(new Location
        {
            LocationId = 30,
            OrganizationId = 2,
            LocationCode = "OTHER",
            LocationName = "Other org",
            ApprovalStatus = "Approved",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var repository = new V180MasterDataAdminRepository(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveDeploymentSiteAsync(
                User("admin"), null,
                new V180DeploymentSiteInput(
                    "C20", "S40", "Office", "OTHER",
                    Today, null, true), default));

        Assert.Contains("UNKNOWN_LOCATION_CODE", ex.Message);
    }

    [Fact]
    public async Task Deployment_site_update_does_not_rewrite_location_assignment_history()
    {
        await using var db = Db();
        SeedVisitorIdentity(db);
        SeedSite(db, 40, "S40", 30, "L30");
        await db.SaveChangesAsync();
        var site = db.DeploymentSites.Single(x => x.DeploymentSiteId == 40);
        site.RowVersion = [1];
        var link = db.DeploymentSiteLocationAssignments.Single(x => x.DeploymentSiteId == 40);
        link.EffectiveFrom = Today.AddDays(-60);
        link.EffectiveTo = Today.AddDays(90);
        await db.SaveChangesAsync();
        var repository = new V180MasterDataAdminRepository(db);

        await repository.SaveDeploymentSiteAsync(
            User("admin"),
            40,
            new V180DeploymentSiteInput(
                "C20", "S40", "Renamed office", "L30",
                Today.AddDays(-10), Today.AddDays(30), true,
                Convert.ToBase64String([1])),
            default);

        Assert.Equal("Renamed office", site.SiteName);
        Assert.Equal(Today.AddDays(-10), site.EffectiveFrom);
        Assert.Equal(Today.AddDays(30), site.EffectiveTo);
        Assert.Equal(30, link.LocationId);
        Assert.Equal(Today.AddDays(-60), link.EffectiveFrom);
        Assert.Equal(Today.AddDays(90), link.EffectiveTo);
    }

    [Fact]
    public async Task Deployment_site_update_rejects_location_history_change()
    {
        await using var db = Db();
        SeedVisitorIdentity(db);
        SeedSite(db, 40, "S40", 30, "L30");
        await db.SaveChangesAsync();
        db.DeploymentSites.Single(x => x.DeploymentSiteId == 40).RowVersion = [1];
        db.Locations.Add(new Location
        {
            LocationId = 31,
            OrganizationId = 1,
            LocationCode = "L31",
            LocationName = "Different office",
            ApprovalStatus = "Approved",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var repository = new V180MasterDataAdminRepository(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveDeploymentSiteAsync(
                User("admin"),
                40,
                new V180DeploymentSiteInput(
                    "C20", "S40", "Office", "L31", Today, null, true,
                    Convert.ToBase64String([1])),
                default));

        Assert.Contains("DEPLOYMENT_SITE_LOCATION_CHANGE_REQUIRES_RELOCATION_FLOW", ex.Message);
        Assert.Equal(30, db.DeploymentSiteLocationAssignments.Single().LocationId);
    }

    [Fact]
    public async Task Employment_status_update_rejects_breaking_existing_employment_site_coverage()
    {
        await using var db = Db();
        SeedVisitorIdentity(db);
        db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod
        {
            EmploymentStatusPeriodId = 1,
            EmploymentId = 100,
            EmploymentStatus = EmploymentStatuses.Active,
            EffectiveFrom = Today.AddDays(-30),
            SourceType = "UAT",
            RowVersion = [1]
        });
        db.EmploymentDeploymentSiteAssignments.Add(new EmploymentDeploymentSiteAssignment
        {
            EmploymentDeploymentSiteAssignmentId = 1,
            EmploymentId = 100,
            DeploymentSiteId = 40,
            EffectiveFrom = Today.AddDays(-5),
            EffectiveTo = Today.AddDays(5),
            IsPrimary = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var repository = new V180MasterDataAdminRepository(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveEmploymentStatusAsync(
                User("admin"),
                1,
                new V180EmploymentStatusInput(
                    "E100", EmploymentStatuses.Leave,
                    Today.AddDays(-30), null, Convert.ToBase64String([1])),
                default));

        Assert.Contains("EMPLOYMENT_STATUS_CHANGE_BREAKS_EMPLOYMENT_SITE", ex.Message);
        var assignment = await db.EmploymentDeploymentSiteAssignments.SingleAsync();
        Assert.Equal(Today.AddDays(-5), assignment.EffectiveFrom);
        Assert.Equal(Today.AddDays(5), assignment.EffectiveTo);
    }

    [Fact]
    public async Task Readiness_rejects_team_center_and_site_center_mismatch()
    {
        await using var db = Db();
        SeedVisitorIdentity(db);
        db.Centers.Add(new Center
        {
            CenterId = 21,
            OrganizationId = 1,
            CenterCode = "C21",
            CenterName = "Center 21",
            EffectiveFrom = Today.AddDays(-30),
            IsActive = true
        });
        SeedSite(db, 41, "S41", 31, "L31");
        await db.SaveChangesAsync();
        db.DeploymentSites.Single(x => x.DeploymentSiteId == 41).CenterId = 21;
        db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod
        {
            EmploymentId = 100,
            EmploymentStatus = EmploymentStatuses.Active,
            EffectiveFrom = Today.AddDays(-30),
            SourceType = "UAT"
        });
        db.TeamCenterAssignments.Add(new TeamCenterAssignment
        {
            TeamId = 10,
            CenterId = 20,
            EffectiveFrom = Today.AddDays(-30),
            CreatedAt = DateTime.UtcNow
        });
        db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment
        {
            TeamId = 10,
            DeploymentSiteId = 41,
            EffectiveFrom = Today.AddDays(-30),
            CreatedAt = DateTime.UtcNow
        });
        db.EmploymentDeploymentSiteAssignments.Add(new EmploymentDeploymentSiteAssignment
        {
            EmploymentId = 100,
            DeploymentSiteId = 41,
            IsPrimary = true,
            EffectiveFrom = Today.AddDays(-30),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var repository = new V180MasterDataAdminRepository(db);

        var readiness = await repository.GetReadinessAsync(User("admin"), default);

        Assert.Equal(0, readiness.TeamCenterMissingCount);
        Assert.True(readiness.TeamSiteMissingCount > 0);
        Assert.True(readiness.EmploymentSiteMissingCount > 0);
        Assert.False(readiness.IsUatReady);
    }

    [Fact]
    public async Task Readiness_requires_at_least_one_target_visitor()
    {
        await using var db = Db();
        db.Centers.Add(new Center
        {
            CenterId = 20, OrganizationId = 1, CenterCode = "C20",
            CenterName = "Center 20", EffectiveFrom = Today.AddDays(-30), IsActive = true
        });
        db.Locations.Add(new Location
        {
            LocationId = 30, OrganizationId = 1, LocationCode = "L30",
            LocationName = "Office", ApprovalStatus = "Approved", IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.DeploymentSites.Add(new DeploymentSite
        {
            DeploymentSiteId = 40, CenterId = 20, SiteCode = "S40", SiteName = "Office",
            EffectiveFrom = Today.AddDays(-30), IsActive = true, CreatedAt = DateTime.UtcNow
        });
        db.DeploymentSiteLocationAssignments.Add(new DeploymentSiteLocationAssignment
        {
            DeploymentSiteLocationAssignmentId = 50, DeploymentSiteId = 40, LocationId = 30,
            EffectiveFrom = Today.AddDays(-30), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var readiness = await new V180MasterDataAdminRepository(db)
            .GetReadinessAsync(User("admin"), default);

        Assert.Equal(1, readiness.DeploymentSiteCount);
        Assert.False(readiness.IsUatReady);
    }

    [Fact]
    public async Task Readiness_rejects_inactive_team_path()
    {
        await using var db = Db();
        SeedCompleteEligibility(db, 40, "S40", 30, "L30");
        db.Teams.Single(x => x.TeamId == 10).IsActive = false;
        db.EmploymentDeploymentSiteAssignments.Add(new EmploymentDeploymentSiteAssignment
        {
            EmploymentDeploymentSiteAssignmentId = 70, EmploymentId = 100,
            DeploymentSiteId = 40, IsPrimary = true,
            EffectiveFrom = Today.AddDays(-30), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var readiness = await new V180MasterDataAdminRepository(db)
            .GetReadinessAsync(User("admin"), default);

        Assert.True(readiness.EmploymentSiteMissingCount > 0);
        Assert.False(readiness.IsUatReady);
    }

    [Fact]
    public async Task Employment_site_requires_active_status_membership_team_site_and_location()
    {
        await using var db = Db();
        SeedVisitorIdentity(db);
        SeedSite(db, 40, "S40", 30, "L30");
        db.TeamCenterAssignments.Add(new TeamCenterAssignment
        {
            TeamCenterAssignmentId = 1,
            TeamId = 10,
            CenterId = 20,
            EffectiveFrom = Today.AddDays(-30),
            CreatedAt = DateTime.UtcNow
        });
        db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment
        {
            TeamDeploymentSiteAssignmentId = 2,
            TeamId = 10,
            DeploymentSiteId = 40,
            EffectiveFrom = Today.AddDays(-30),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var repository = new V180MasterDataAdminRepository(db);

        var missingStatus = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveEmploymentSiteAsync(
                User("admin"), null,
                new V180EmploymentSiteInput("E100", "S40", true, Today, null),
                default));
        Assert.Contains("EMPLOYMENT_SITE_WITHOUT_ACTIVE_EMPLOYMENT", missingStatus.Message);

        db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod
        {
            EmploymentId = 100,
            EmploymentStatus = EmploymentStatuses.Active,
            EffectiveFrom = Today.AddDays(-30),
            SourceType = "UAT"
        });
        await db.SaveChangesAsync();

        var result = await repository.SaveEmploymentSiteAsync(
            User("admin"), null,
            new V180EmploymentSiteInput("E100", "S40", true, Today, null),
            default);

        Assert.True(result.IsPrimary);
    }

    [Fact]
    public async Task Employment_site_same_site_overlap_is_rejected_even_when_non_primary()
    {
        await using var db = Db();
        SeedCompleteEligibility(db, 40, "S40", 30, "L30");
        db.EmploymentDeploymentSiteAssignments.Add(new EmploymentDeploymentSiteAssignment
        {
            EmploymentDeploymentSiteAssignmentId = 1,
            EmploymentId = 100,
            DeploymentSiteId = 40,
            IsPrimary = false,
            EffectiveFrom = Today.AddDays(-2),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var repository = new V180MasterDataAdminRepository(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveEmploymentSiteAsync(
                User("admin"), null,
                new V180EmploymentSiteInput("E100", "S40", false, Today, null),
                default));

        Assert.Contains("OVERLAPPING_EMPLOYMENT_SITE", ex.Message);
    }

    [Fact]
    public async Task Employment_site_rejects_second_overlapping_primary_site()
    {
        await using var db = Db();
        SeedCompleteEligibility(db, 40, "S40", 30, "L30");
        SeedSite(db, 41, "S41", 31, "L31");
        db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment
        {
            TeamDeploymentSiteAssignmentId = 3,
            TeamId = 10,
            DeploymentSiteId = 41,
            EffectiveFrom = Today.AddDays(-30),
            CreatedAt = DateTime.UtcNow
        });
        db.EmploymentDeploymentSiteAssignments.Add(new EmploymentDeploymentSiteAssignment
        {
            EmploymentDeploymentSiteAssignmentId = 1,
            EmploymentId = 100,
            DeploymentSiteId = 40,
            IsPrimary = true,
            EffectiveFrom = Today.AddDays(-2),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var repository = new V180MasterDataAdminRepository(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveEmploymentSiteAsync(
                User("admin"), null,
                new V180EmploymentSiteInput("E100", "S41", true, Today, null),
                default));

        Assert.Contains("MULTIPLE_PRIMARY_EMPLOYMENT_SITE", ex.Message);
    }

    [Fact]
    public void Effective_period_boundaries_are_inclusive()
    {
        Assert.True(V180MasterDataValidationService.Overlaps(
            Today, Today.AddDays(2), Today.AddDays(2), Today.AddDays(3)));
        Assert.True(V180MasterDataValidationService.Covers(
            Today, Today.AddDays(3), Today, Today.AddDays(3)));
        Assert.False(V180MasterDataValidationService.Overlaps(
            Today, Today.AddDays(1), Today.AddDays(2), null));
    }

    private static AppDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"v180-master-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static CurrentUserDto User(string role) =>
        new(
            UserId: role == "admin" ? 900 : role == "leader" ? 901 : 902,
            EmployeeNo: role.ToUpperInvariant(),
            DisplayName: role,
            Email: $"{role}@example.test",
            OrganizationId: 1,
            TeamId: null,
            TeamName: null,
            Roles: [role]);

    private static void SeedVisitorIdentity(AppDbContext db)
    {
        db.Roles.Add(new Role
        {
            RoleId = 1,
            RoleCode = "visitor",
            RoleName = "Visitor",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.Users.Add(new User
        {
            UserId = 1,
            OrganizationId = 1,
            EmployeeNo = "E100",
            DisplayName = "Visitor",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            UserRoleAssignmentId = 1,
            UserId = 1,
            RoleId = 1,
            EffectiveFrom = Today.AddDays(-30),
            CreatedAt = DateTime.UtcNow
        });
        db.Employments.Add(new Employment
        {
            EmploymentId = 100,
            PersonId = 1000,
            OrganizationId = 1,
            EmployeeNo = "E100",
            SourceType = "UAT"
        });
        db.UserIdentityProfiles.Add(new UserIdentityProfile
        {
            UserId = 1,
            EmploymentId = 100,
            UserType = UserTypes.Internal,
            UserCode = "visitor01",
            IdentityProvider = "Demo"
        });
        db.Teams.Add(new Team
        {
            TeamId = 10,
            OrganizationId = 1,
            TeamCode = "T10",
            TeamName = "Team 10",
            IsActive = true,
            EffectiveFrom = Today.AddDays(-30)
        });
        db.TeamMemberships.Add(new TeamMembership
        {
            TeamMembershipId = 1,
            EmploymentId = 100,
            TeamId = 10,
            IsPrimary = true,
            EffectiveFrom = Today.AddDays(-30)
        });
        db.Centers.Add(new Center
        {
            CenterId = 20,
            OrganizationId = 1,
            CenterCode = "C20",
            CenterName = "Center 20",
            EffectiveFrom = Today.AddDays(-30),
            IsActive = true
        });
    }

    private static void SeedSite(
        AppDbContext db,
        int siteId,
        string siteCode,
        int locationId,
        string locationCode)
    {
        db.Locations.Add(new Location
        {
            LocationId = locationId,
            OrganizationId = 1,
            LocationCode = locationCode,
            LocationName = locationCode,
            ApprovalStatus = "Approved",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.DeploymentSites.Add(new DeploymentSite
        {
            DeploymentSiteId = siteId,
            CenterId = 20,
            SiteCode = siteCode,
            SiteName = siteCode,
            EffectiveFrom = Today.AddDays(-30),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.DeploymentSiteLocationAssignments.Add(new DeploymentSiteLocationAssignment
        {
            DeploymentSiteLocationAssignmentId = 1000 + siteId,
            DeploymentSiteId = siteId,
            LocationId = locationId,
            EffectiveFrom = Today.AddDays(-30),
            CreatedAt = DateTime.UtcNow
        });
    }

    private static void SeedCompleteEligibility(
        AppDbContext db,
        int siteId,
        string siteCode,
        int locationId,
        string locationCode)
    {
        SeedVisitorIdentity(db);
        SeedSite(db, siteId, siteCode, locationId, locationCode);
        db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod
        {
            EmploymentStatusPeriodId = 1,
            EmploymentId = 100,
            EmploymentStatus = EmploymentStatuses.Active,
            EffectiveFrom = Today.AddDays(-30),
            SourceType = "UAT"
        });
        db.TeamCenterAssignments.Add(new TeamCenterAssignment
        {
            TeamCenterAssignmentId = 1,
            TeamId = 10,
            CenterId = 20,
            EffectiveFrom = Today.AddDays(-30),
            CreatedAt = DateTime.UtcNow
        });
        db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment
        {
            TeamDeploymentSiteAssignmentId = 2,
            TeamId = 10,
            DeploymentSiteId = siteId,
            EffectiveFrom = Today.AddDays(-30),
            CreatedAt = DateTime.UtcNow
        });
    }

    private sealed class StubCurrentUserService(CurrentUserDto user) : ICurrentUserService
    {
        public CurrentUserDto GetRequired() => user;
    }

    private sealed class StubMasterDataRepository : IV180MasterDataAdminRepository
    {
        public int ReadinessCalls { get; private set; }

        public Task<V180MasterDataReadinessDto> GetReadinessAsync(
            CurrentUserDto admin,
            CancellationToken ct)
        {
            ReadinessCalls++;
            return Task.FromResult(new V180MasterDataReadinessDto(0, 0, 1, 0, 0, true));
        }

        public Task<IReadOnlyList<V180MasterDataRow>> ListAsync(CurrentUserDto admin, string kind, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<V180MasterDataRow>>([]);
        public Task<V180MasterDataRow> SaveEmploymentStatusAsync(CurrentUserDto admin, long? id, V180EmploymentStatusInput input, CancellationToken ct) => throw new NotSupportedException();
        public Task<V180MasterDataRow> SaveCenterAsync(CurrentUserDto admin, int? id, V180CenterInput input, CancellationToken ct) => throw new NotSupportedException();
        public Task<V180MasterDataRow> SaveTeamCenterAsync(CurrentUserDto admin, long? id, V180TeamCenterInput input, CancellationToken ct) => throw new NotSupportedException();
        public Task<V180MasterDataRow> SaveDeploymentSiteAsync(CurrentUserDto admin, int? id, V180DeploymentSiteInput input, CancellationToken ct) => throw new NotSupportedException();
        public Task<V180MasterDataRow> SaveTeamSiteAsync(CurrentUserDto admin, long? id, V180TeamSiteInput input, CancellationToken ct) => throw new NotSupportedException();
        public Task<V180MasterDataRow> SaveEmploymentSiteAsync(CurrentUserDto admin, long? id, V180EmploymentSiteInput input, CancellationToken ct) => throw new NotSupportedException();
    }
}
