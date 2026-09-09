using System.Reflection;
using FieldVisit.Api.Controllers;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180TeamCenterLifecycleWriteTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 12, 31);
    private static readonly byte[] Version = [1, 2, 3, 4, 5, 6, 7, 8];
    private static string Token => Convert.ToBase64String(Version);
    private static CurrentUserDto Admin(int org = 1) => new(99, "ADMIN", "Admin", null,
        org, null, null, ["admin"]);
    private static AppDbContext MemoryDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static V180TeamCenterLifecycleWriter Writer(AppDbContext db) => new(db);

    [Fact]
    public async Task Team_create_update_and_deactivate_are_authoritative_lifecycle_writes()
    {
        await using var db = MemoryDb();
        db.Organizations.Add(Organization());
        await db.SaveChangesAsync();
        var created = await Writer(db).CreateTeamAsync(Admin(), new(" t1 ", " Team One ", Start), default);
        Assert.Equal("T1", created.Code);
        var row = await db.Teams.SingleAsync();
        row.RowVersion = Version.ToArray();
        await db.SaveChangesAsync();
        var updated = await Writer(db).UpdateTeamAsync(Admin(), row.TeamId,
            new("T1", "Team Renamed", Start, null, "note", true, Token), default);
        Assert.Equal("Team Renamed", updated.Name);
        var deactivated = await Writer(db).DeactivateTeamAsync(Admin(), row.TeamId,
            new(End, Token), default);
        Assert.False(deactivated.IsActive);
        Assert.Equal(End, deactivated.EffectiveTo);
    }

    [Fact]
    public async Task Center_create_update_and_deactivate_are_authoritative_lifecycle_writes()
    {
        await using var db = MemoryDb();
        db.Organizations.Add(Organization());
        await db.SaveChangesAsync();
        var created = await Writer(db).CreateCenterAsync(Admin(), new(" c1 ", " Center One ", Start), default);
        Assert.Equal("C1", created.Code);
        var row = await db.Centers.SingleAsync(); row.RowVersion = Version.ToArray(); await db.SaveChangesAsync();
        Assert.Equal("Center Renamed", (await Writer(db).UpdateCenterAsync(Admin(), row.CenterId,
            new("C1", "Center Renamed", Start, null, null, true, Token), default)).Name);
        var ended = await Writer(db).DeactivateCenterAsync(Admin(), row.CenterId, new(End, Token), default);
        Assert.False(ended.IsActive);
        Assert.Equal(End, ended.EffectiveTo);
    }

    [Fact]
    public async Task Valid_team_center_assignment_is_created_and_can_be_updated_and_ended()
    {
        await using var db = await SeedCoreAsync();
        var writer = Writer(db);
        var created = await writer.CreateTeamCenterAssignmentAsync(Admin(), new(10, 20, Start, End, "initial"), default);
        var row = await db.TeamCenterAssignments.SingleAsync(); row.RowVersion = Version.ToArray(); await db.SaveChangesAsync();
        var updated = await writer.UpdateTeamCenterAssignmentAsync(Admin(), created.TeamCenterAssignmentId,
            new(20, Start, End, "updated", Token), default);
        Assert.Equal("updated", updated.ChangeReason);
        var ended = await writer.EndTeamCenterAssignmentAsync(Admin(), created.TeamCenterAssignmentId,
            new(Start, Token), default);
        Assert.Equal(Start, ended.EffectiveTo);
    }

    [Fact]
    public async Task Organization_mismatch_is_rejected()
    {
        await using var db = await SeedCoreAsync(centerOrganization: 2);
        await Assert.ThrowsAnyAsync<Exception>(() => Writer(db).CreateTeamCenterAssignmentAsync(
            Admin(), new(10, 20, Start), default));
    }

    [Fact]
    public async Task Assignment_overlap_including_touching_boundary_is_rejected()
    {
        await using var db = await SeedCoreAsync();
        db.TeamCenterAssignments.Add(new TeamCenterAssignment { TeamCenterAssignmentId = 1, TeamId = 10,
            CenterId = 20, EffectiveFrom = Start, EffectiveTo = End, RowVersion = Version });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(db).CreateTeamCenterAssignmentAsync(
            Admin(), new(10, 20, End), default));
    }

    [Fact]
    public void Lifecycle_boundaries_are_inclusive()
    {
        Assert.True(V180AsOfRules.IsEffective(Start, End, Start));
        Assert.True(V180AsOfRules.IsEffective(Start, End, End));
        Assert.True(V180TeamCenterLifecycleRules.IsWithin(Start, End, Start, End));
        Assert.True(V180TeamCenterLifecycleRules.Overlaps(Start, End, End, null));
    }

    [Fact]
    public async Task Assignment_outside_team_lifecycle_is_rejected()
    {
        await using var db = await SeedCoreAsync(teamFrom: Start.AddDays(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(db).CreateTeamCenterAssignmentAsync(
            Admin(), new(10, 20, Start, End), default));
    }

    [Fact]
    public async Task Assignment_outside_center_lifecycle_is_rejected()
    {
        await using var db = await SeedCoreAsync(centerTo: End.AddDays(-1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(db).CreateTeamCenterAssignmentAsync(
            Admin(), new(10, 20, Start, End), default));
    }

    [Fact]
    public async Task Stale_rowversion_is_rejected()
    {
        await using var db = await SeedCoreAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(db).UpdateTeamAsync(Admin(), 10,
            new("T", "Team", Start, End, null, true, Convert.ToBase64String(new byte[8])), default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AQID")]
    public async Task Invalid_version_is_rejected(string version)
    {
        await using var db = await SeedCoreAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(db).UpdateCenterAsync(Admin(), 20,
            new("C", "Center", Start, End, null, true, version), default));
    }

    [Fact]
    public async Task Future_schedule_conflict_fails_closed()
    {
        await using var db = await SeedCoreAsync();
        db.TeamMemberships.Add(new TeamMembership { TeamMembershipId = 1, TeamId = 10, EmploymentId = 1,
            EffectiveFrom = End.AddDays(10), EffectiveTo = null });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(db).DeactivateTeamAsync(
            Admin(), 10, new(End, Token), default));
    }

    [Fact]
    public async Task Team_deactivation_is_blocked_by_effective_membership()
    {
        await using var db = await SeedCoreAsync();
        db.TeamMemberships.Add(new TeamMembership { TeamMembershipId = 1, TeamId = 10, EmploymentId = 1,
            EffectiveFrom = Start, EffectiveTo = End }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(db).DeactivateTeamAsync(Admin(), 10, new(Start, Token), default));
    }

    [Fact]
    public async Task Team_deactivation_is_blocked_by_effective_leader_or_delegation()
    {
        await using var db = await SeedCoreAsync();
        db.TeamLeaderAssignments.Add(new TeamLeaderAssignment { TeamLeaderAssignmentId = 1, TeamId = 10,
            EmploymentId = 1, EffectiveFrom = Start, EffectiveTo = End });
        db.TeamLeaderDelegations.Add(new TeamLeaderDelegation { TeamLeaderDelegationId = 1,
            TeamLeaderAssignmentId = 1, DelegateEmploymentId = 2, EffectiveFrom = Start, EffectiveTo = End });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(db).DeactivateTeamAsync(Admin(), 10, new(Start, Token), default));
    }

    [Fact]
    public async Task Team_deactivation_is_blocked_by_effective_center_assignment()
    {
        await using var db = await SeedCoreAsync();
        db.TeamCenterAssignments.Add(new TeamCenterAssignment { TeamCenterAssignmentId = 1, TeamId = 10,
            CenterId = 20, EffectiveFrom = Start, EffectiveTo = End }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(db).DeactivateTeamAsync(Admin(), 10, new(Start, Token), default));
    }

    [Fact]
    public async Task Center_deactivation_is_blocked_by_effective_team_assignment()
    {
        await using var db = await SeedCoreAsync();
        db.TeamCenterAssignments.Add(new TeamCenterAssignment { TeamCenterAssignmentId = 1, TeamId = 10,
            CenterId = 20, EffectiveFrom = Start, EffectiveTo = End }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(db).DeactivateCenterAsync(Admin(), 20, new(Start, Token), default));
    }

    [Fact]
    public void V180_write_routes_are_additive_and_admin_scoped()
    {
        var controller = typeof(V180OrganizationPeopleAdminController);
        foreach (var (method, route) in new[] {
            ("CreateTeam", "teams"), ("UpdateTeam", "teams/{teamId:int}"),
            ("DeactivateTeam", "teams/{teamId:int}/deactivate"), ("CreateCenter", "centers"),
            ("UpdateCenter", "centers/{centerId:int}"), ("DeactivateCenter", "centers/{centerId:int}/deactivate"),
            ("CreateTeamCenterAssignment", "team-center-assignments"),
            ("UpdateTeamCenterAssignment", "team-center-assignments/{assignmentId:long}"),
            ("EndTeamCenterAssignment", "team-center-assignments/{assignmentId:long}/end") })
        {
            var attribute = controller.GetMethod(method)!.GetCustomAttributes<HttpMethodAttribute>().Single();
            Assert.Equal(route, attribute.Template);
        }
    }

    [Fact]
    public void Legacy_team_writes_delegate_without_an_independent_direct_write_fallback()
    {
        var source = Source("backend/src/FieldVisit.Infrastructure/V160FinalRepository.cs");
        var slice = Slice(source, "CreateManagedTeamAsync", "public async Task<IReadOnlyList<ManagedLocationDto>>");
        Assert.Contains("v180TeamCenterWriter.CreateTeamAsync", slice);
        Assert.Contains("v180TeamCenterWriter.UpdateTeamAsync", slice);
        Assert.Contains("v180TeamCenterWriter.DeactivateTeamAsync", slice);
        Assert.DoesNotContain("db.Teams.Add", slice);
        Assert.DoesNotContain("row.TeamCode =", slice);
        Assert.DoesNotContain("row.IsActive =", slice);
        Assert.DoesNotContain("db.SaveChangesAsync", slice);
    }

    private static Organization Organization(int id = 1) => new() { OrganizationId = id,
        OrganizationCode = $"O{id}", OrganizationName = $"Org {id}", IsActive = true, RowVersion = Version.ToArray() };

    private static async Task<AppDbContext> SeedCoreAsync(int centerOrganization = 1,
        DateOnly? teamFrom = null, DateOnly? centerTo = null)
    {
        var db = MemoryDb();
        db.Organizations.AddRange(Organization(), Organization(2));
        db.Teams.Add(new Team { TeamId = 10, OrganizationId = 1, TeamCode = "T", TeamName = "Team",
            EffectiveFrom = teamFrom ?? Start, EffectiveTo = End, IsActive = true, RowVersion = Version.ToArray() });
        db.Centers.Add(new Center { CenterId = 20, OrganizationId = centerOrganization,
            CenterCode = "C", CenterName = "Center", EffectiveFrom = Start, EffectiveTo = centerTo ?? End,
            IsActive = true, RowVersion = Version.ToArray() });
        await db.SaveChangesAsync();
        return db;
    }

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, relative)))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relative));
    }

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from);
        return source[from..to];
    }
}
