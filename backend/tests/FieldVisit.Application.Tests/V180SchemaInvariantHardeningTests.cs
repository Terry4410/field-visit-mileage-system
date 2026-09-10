using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180SchemaInvariantHardeningTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 12, 31);
    private static readonly byte[] Version = [1,2,3,4,5,6,7,8];
    private static string Token => Convert.ToBase64String(Version);
    private static CurrentUserDto Admin() => new(99, "ADMIN", "Admin", null, 1, null, null, ["admin"]);
    private static AppDbContext MemoryDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Employment_same_site_overlap_is_rejected_regardless_of_primary()
    {
        await using var db = await SeedAuthorityAsync();
        db.EmploymentDeploymentSiteAssignments.Add(new EmploymentDeploymentSiteAssignment
        {
            EmploymentDeploymentSiteAssignmentId = 1, EmploymentId = 400, DeploymentSiteId = 100,
            IsPrimary = false, EffectiveFrom = Start, EffectiveTo = Start.AddDays(20), RowVersion = Version.ToArray()
        });
        await db.SaveChangesAsync();
        var writer = new V180DeploymentSiteWriter(db);

        var nonPrimary = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.CreateEmploymentAssignmentAsync(Admin(),
                new(400, 100, false, Start.AddDays(10), Start.AddDays(30)), default));
        Assert.Contains("EMPLOYMENT_SITE_OVERLAP", nonPrimary.Message);

        var mixed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.CreateEmploymentAssignmentAsync(Admin(),
                new(400, 100, true, Start.AddDays(10), Start.AddDays(30)), default));
        Assert.Contains("EMPLOYMENT_SITE_OVERLAP", mixed.Message);
    }

    [Fact]
    public async Task Employment_same_site_inclusive_boundary_rejects_D_and_accepts_D_plus_1()
    {
        await using var db = await SeedAuthorityAsync();
        var d = Start.AddDays(20);
        db.EmploymentDeploymentSiteAssignments.Add(new EmploymentDeploymentSiteAssignment
        {
            EmploymentDeploymentSiteAssignmentId = 1, EmploymentId = 400, DeploymentSiteId = 100,
            IsPrimary = false, EffectiveFrom = Start, EffectiveTo = d, RowVersion = Version.ToArray()
        });
        await db.SaveChangesAsync();
        var writer = new V180DeploymentSiteWriter(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CreateEmploymentAssignmentAsync(
            Admin(), new(400, 100, false, d, d.AddDays(5)), default));
        var accepted = await writer.CreateEmploymentAssignmentAsync(
            Admin(), new(400, 100, false, d.AddDays(1), d.AddDays(5)), default);
        Assert.Equal(d.AddDays(1), accepted.EffectiveFrom);
    }

    [Fact]
    public async Task Employment_update_or_end_failure_does_not_mutate_original_and_end_cannot_extend()
    {
        await using var db = await SeedAuthorityAsync();
        var d = Start.AddDays(20);
        var original = new EmploymentDeploymentSiteAssignment
        {
            EmploymentDeploymentSiteAssignmentId = 1, EmploymentId = 400, DeploymentSiteId = 100,
            IsPrimary = false, EffectiveFrom = Start, EffectiveTo = d, RowVersion = Version.ToArray()
        };
        db.EmploymentDeploymentSiteAssignments.AddRange(original,
            new EmploymentDeploymentSiteAssignment
            {
                EmploymentDeploymentSiteAssignmentId = 2, EmploymentId = 400, DeploymentSiteId = 100,
                IsPrimary = false, EffectiveFrom = d.AddDays(2), EffectiveTo = d.AddDays(10)
            });
        await db.SaveChangesAsync();
        var writer = new V180DeploymentSiteWriter(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.UpdateEmploymentAssignmentAsync(
            Admin(), 1, new(false, Start, d.AddDays(2), Token), default));
        Assert.Equal(d, original.EffectiveTo);

        var endEx = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.EndEmploymentAssignmentAsync(
            Admin(), 1, new(d.AddDays(1), Token), default));
        Assert.Contains("END_EXTENSION_NOT_ALLOWED", endEx.Message);
        Assert.Equal(d, original.EffectiveTo);
    }

    [Fact]
    public async Task Corrupted_directly_seeded_same_site_overlap_keeps_resolver_fail_closed()
    {
        var visitDate = new DateOnly(2026, 9, 9);
        await using var db = MemoryDb();
        db.Organizations.Add(new Organization { OrganizationId = 1, OrganizationCode = "O1", OrganizationName = "Org", IsActive = true });
        db.UserIdentityProfiles.Add(new UserIdentityProfile { UserId = 1, EmploymentId = 10, UserType = UserTypes.Internal, UserCode = "U1" });
        db.Persons.Add(new Person { PersonId = 20, DisplayName = "Visitor" });
        db.Employments.Add(new Employment { EmploymentId = 10, PersonId = 20, OrganizationId = 1, EmployeeNo = "E001", SourceType = "Test" });
        db.EmploymentStatusPeriods.Add(new EmploymentStatusPeriod { EmploymentStatusPeriodId = 1, EmploymentId = 10,
            EmploymentStatus = EmploymentStatuses.Active, EffectiveFrom = Start, SourceType = "Test" });
        db.Teams.Add(new Team { TeamId = 100, OrganizationId = 1, TeamCode = "T1", TeamName = "Team", EffectiveFrom = Start, IsActive = true });
        db.TeamMemberships.Add(new TeamMembership { TeamMembershipId = 1, EmploymentId = 10, TeamId = 100, IsPrimary = true, EffectiveFrom = Start });
        db.Centers.Add(new Center { CenterId = 200, OrganizationId = 1, CenterCode = "C1", CenterName = "Center", EffectiveFrom = Start, IsActive = true });
        db.DeploymentSites.Add(new DeploymentSite { DeploymentSiteId = 300, CenterId = 200, SiteCode = "S1", SiteName = "Site", EffectiveFrom = Start, IsActive = true });
        db.Locations.Add(new Location { LocationId = 400, OrganizationId = 1, LocationCode = "L1", LocationName = "Office", Address = "A", IsActive = true });
        db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment { TeamDeploymentSiteAssignmentId = 1,
            TeamId = 100, DeploymentSiteId = 300, EffectiveFrom = Start });
        db.DeploymentSiteLocationAssignments.Add(new DeploymentSiteLocationAssignment { DeploymentSiteLocationAssignmentId = 1,
            DeploymentSiteId = 300, LocationId = 400, EffectiveFrom = Start });
        db.EmploymentDeploymentSiteAssignments.AddRange(
            new EmploymentDeploymentSiteAssignment { EmploymentDeploymentSiteAssignmentId = 1, EmploymentId = 10,
                DeploymentSiteId = 300, IsPrimary = false, EffectiveFrom = Start },
            new EmploymentDeploymentSiteAssignment { EmploymentDeploymentSiteAssignmentId = 2, EmploymentId = 10,
                DeploymentSiteId = 300, IsPrimary = false, EffectiveFrom = Start.AddDays(1) });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new V180TripContextReader(db)
            .ResolveAsync(new CurrentUserDto(1, "E001", "Visitor", null, 1, null, null, ["visitor"]), visitDate, null, default));
        Assert.Contains("AMBIGUOUS_EMPLOYMENT_SITE", ex.Message);
    }

    [Theory]
    [InlineData("shorten")]
    [InlineData("move")]
    [InlineData("shift")]
    [InlineData("future")]
    public async Task TeamCenter_breaking_existing_or_future_team_site_is_rejected_without_partial_mutation(string change)
    {
        await using var db = await SeedTeamCoverageAsync(futureTeamSite: change == "future");
        var row = db.TeamCenterAssignments.Single();
        var originalCenter = row.CenterId;
        var originalFrom = row.EffectiveFrom;
        var originalTo = row.EffectiveTo;
        var writer = new V180TeamCenterLifecycleWriter(db);

        V180UpdateTeamCenterAssignmentRequest request = change switch
        {
            "move" => new(11, Start, End, "move", Token),
            "shift" => new(10, Start.AddDays(10), End, "shift", Token),
            "future" => new(10, Start, new DateOnly(2026, 8, 31), "future break", Token),
            _ => new(10, Start, new DateOnly(2026, 6, 30), "shorten", Token)
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.UpdateTeamCenterAssignmentAsync(Admin(), 1, request, default));
        Assert.Contains("TEAM_CENTER_DEPENDENCY_CONFLICT", ex.Message);
        Assert.Equal(originalCenter, row.CenterId);
        Assert.Equal(originalFrom, row.EffectiveFrom);
        Assert.Equal(originalTo, row.EffectiveTo);
    }

    [Fact]
    public async Task TeamCenter_end_breaking_team_site_rejects_and_safe_parent_change_succeeds()
    {
        await using var db = await SeedTeamCoverageAsync(teamSiteTo: new DateOnly(2026, 6, 30));
        var row = db.TeamCenterAssignments.Single();
        var writer = new V180TeamCenterLifecycleWriter(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.EndTeamCenterAssignmentAsync(
            Admin(), 1, new(new DateOnly(2026, 6, 1), Token), default));
        Assert.Equal(End, row.EffectiveTo);

        var safe = await writer.UpdateTeamCenterAssignmentAsync(Admin(), 1,
            new(10, Start, new DateOnly(2026, 7, 1), "safe", Token), default);
        Assert.Equal(new DateOnly(2026, 7, 1), safe.EffectiveTo);
    }

    [Fact]
    public async Task DeploymentSite_center_move_is_friendly_rejected_when_team_site_would_lose_coverage()
    {
        await using var db = await SeedTeamCoverageAsync();
        var site = db.DeploymentSites.Single();
        site.RowVersion = Version.ToArray();
        await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new V180DeploymentSiteWriter(db).UpdateSiteAsync(
            Admin(), 100, new(11, "S1", "Site", Start, End, null, true, Token), default));
        Assert.Contains("TEAM_SITE_CENTER_CONFLICT", ex.Message);
        Assert.Equal(10, site.CenterId);
    }

    [Fact]
    public void Sql_schema_has_separate_A1_A2_and_shared_transaction_lock_for_B_parent_child_races()
    {
        var up = Source("database/migrations/1800_003_deployment_sites/Up.sql");
        Assert.Contains("TR_EmploymentDeploymentSites_SameSiteNoOverlap", up);
        Assert.Contains("TR_EmploymentDeploymentSites_OnePrimary", up);
        Assert.Contains("FieldVisit.EmploymentSiteTemporalInvariant", up);
        Assert.Contains("TR_TeamCenterAssignments_ProtectTeamSites", up);
        Assert.Contains("TR_DeploymentSites_ProtectTeamSites", up);
        Assert.True(Count(up, "FieldVisit.TeamSiteCoverageInvariant") >= 3);
        Assert.True(Count(up, "@LockOwner=N''Transaction''") >= 5);
        Assert.Contains("i.EffectiveFrom<=ISNULL(x.EffectiveTo", up);
        Assert.Contains("x.EffectiveFrom<=ISNULL(i.EffectiveTo", up);
    }

    [Fact]
    public void Verify_checks_A1_A2_B1_and_new_protection_objects_independently()
    {
        var verify = Source("database/migrations/1800_003_deployment_sites/Verify.sql");
        Assert.Contains("A1", verify);
        Assert.Contains("A2", verify);
        Assert.Contains("B1", verify);
        Assert.Contains("TR_EmploymentDeploymentSites_SameSiteNoOverlap", verify);
        Assert.Contains("TR_TeamCenterAssignments_ProtectTeamSites", verify);
        Assert.Contains("TR_DeploymentSites_ProtectTeamSites", verify);
    }

    private static async Task<AppDbContext> SeedAuthorityAsync()
    {
        var db = MemoryDb();
        db.Organizations.Add(new Organization { OrganizationId = 1, OrganizationCode = "O1", OrganizationName = "Org", IsActive = true });
        db.Centers.Add(new Center { CenterId = 10, OrganizationId = 1, CenterCode = "C10", CenterName = "Center", EffectiveFrom = Start, EffectiveTo = End, IsActive = true });
        db.DeploymentSites.Add(new DeploymentSite { DeploymentSiteId = 100, CenterId = 10, SiteCode = "S1", SiteName = "Site", EffectiveFrom = Start, EffectiveTo = End, IsActive = true });
        db.Persons.Add(new Person { PersonId = 500, DisplayName = "Person" });
        db.Employments.Add(new Employment { EmploymentId = 400, PersonId = 500, OrganizationId = 1, EmployeeNo = "E1", SourceType = "Test" });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<AppDbContext> SeedTeamCoverageAsync(bool futureTeamSite = false, DateOnly? teamSiteTo = null)
    {
        var db = MemoryDb();
        db.Organizations.Add(new Organization { OrganizationId = 1, OrganizationCode = "O1", OrganizationName = "Org", IsActive = true });
        db.Centers.AddRange(
            new Center { CenterId = 10, OrganizationId = 1, CenterCode = "C10", CenterName = "Center 10", EffectiveFrom = Start, EffectiveTo = End, IsActive = true },
            new Center { CenterId = 11, OrganizationId = 1, CenterCode = "C11", CenterName = "Center 11", EffectiveFrom = Start, EffectiveTo = End, IsActive = true });
        db.Teams.Add(new Team { TeamId = 300, OrganizationId = 1, TeamCode = "T1", TeamName = "Team", EffectiveFrom = Start, EffectiveTo = End, IsActive = true });
        db.DeploymentSites.Add(new DeploymentSite { DeploymentSiteId = 100, CenterId = 10, SiteCode = "S1", SiteName = "Site", EffectiveFrom = Start, EffectiveTo = End, IsActive = true });
        db.TeamCenterAssignments.Add(new TeamCenterAssignment { TeamCenterAssignmentId = 1, TeamId = 300, CenterId = 10,
            EffectiveFrom = Start, EffectiveTo = End, RowVersion = Version.ToArray() });
        db.TeamDeploymentSiteAssignments.Add(new TeamDeploymentSiteAssignment { TeamDeploymentSiteAssignmentId = 1,
            TeamId = 300, DeploymentSiteId = 100,
            EffectiveFrom = futureTeamSite ? new DateOnly(2026, 9, 1) : Start,
            EffectiveTo = teamSiteTo ?? End });
        await db.SaveChangesAsync();
        return db;
    }

    private static int Count(string source, string needle)
    {
        var count = 0;
        for (var at = 0; (at = source.IndexOf(needle, at, StringComparison.Ordinal)) >= 0; at += needle.Length) count++;
        return count;
    }

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "backend", "FieldVisitSystem.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relative));
    }
}
