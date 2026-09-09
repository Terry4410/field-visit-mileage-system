using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180QueryRegressionTests
{
    private static AppDbContext MemoryDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static CurrentUserDto Actor(string role = "admin", int org = 1, int id = 1, int team = 10) =>
        new(id, "A", "Actor", null, org, team, "Team", [role]);
    private static V160FinalRepository Repo(AppDbContext db) =>
        new(db, new V170AccessControl(db), new V180OrganizationPeopleWriter(db));

    [Fact]
    public void Query_limits_and_validation_are_enforced()
    {
        var r = V180QueryRules.Normalize(new(Keyword: "  客戶  ", Page: int.MaxValue, PageSize: 100000));
        Assert.Equal("客戶", r.Keyword);
        Assert.Equal(100, r.PageSize);
        Assert.True((long)(r.Page - 1) * r.PageSize < int.MaxValue);
        Assert.Equal(1, V180QueryRules.Normalize(new(Page: -1, PageSize: -1)).PageSize);
        Assert.Throws<InvalidOperationException>(() => V180QueryRules.Normalize(new(Keyword: new string('x', 201))));
        Assert.Throws<InvalidOperationException>(() => V180QueryRules.Normalize(
            new(StartDate: new(2026, 9, 8), EndDate: new(2026, 9, 7))));
    }

    [Theory]
    [InlineData("up", 2, 2, 1, 3)]
    [InlineData("down", 2, 1, 3, 2)]
    public void Arrow_order_is_server_owned(string direction, int id, int a, int b, int c)
    {
        V180OrderItem[] before = [new(1, 10), new(2, 10), new(3, 999)];
        var after = V180QueryRules.Move(before, id, new(direction, before));
        Assert.Equal(new[] { a, b, c }, after.Select(x => x.VisitTypeId));
        Assert.Equal(new[] { 10, 20, 30 }, after.Select(x => x.SortOrder));
        Assert.Equal(999, before[2].SortOrder);
    }

    [Fact]
    public void Stale_order_cannot_overwrite_another_admin()
    {
        V180OrderItem[] current = [new(1, 10), new(2, 20)];
        Assert.Throws<InvalidOperationException>(() => V180QueryRules.Move(current, 1,
            new("down", [new(1, 20), new(2, 10)])));
        Assert.Throws<InvalidOperationException>(() => V180QueryRules.Move(current, 1, new("sideways", current)));
        Assert.Throws<KeyNotFoundException>(() => V180QueryRules.Move(current, 99, new("up", current)));
        Assert.Equal(current, V180QueryRules.Move(current, 1, new("up", current)));
        Assert.Equal(current, V180QueryRules.Move(current, 2, new("down", current)));
    }

    [Fact]
    public async Task Users_are_filtered_before_paging_and_scoped_to_org()
    {
        await using var db = MemoryDb();
        for (var i = 1; i <= 125; i++) db.Users.Add(new User {
            UserId = i, OrganizationId = 1, EmployeeNo = $"E{i:000}", DisplayName = "Matching", Email = $"u{i}@example.test", IsActive = true
        });
        db.Users.Add(new User { UserId = 999, OrganizationId = 2, EmployeeNo = "E999", DisplayName = "Matching", IsActive = true });
        await db.SaveChangesAsync();
        var result = await Repo(db).SearchUsersAsync(Actor(), new(Keyword: "Matching", Page: 2, PageSize: 50), default);
        Assert.Equal(125, result.TotalCount);
        Assert.Equal(50, result.Items.Count);
        Assert.Equal("E051", result.Items[0].EmployeeNo);
        Assert.DoesNotContain(result.Items, x => x.UserId == 999);
        var email = await Repo(db).SearchUsersAsync(Actor(), new(Keyword: "u125@"), default);
        Assert.Equal(125, Assert.Single(email.Items).UserId);
        Assert.DoesNotContain(db.ChangeTracker.Entries(), x => x.State == EntityState.Modified);
    }

    [Theory]
    [InlineData("visitor")]
    [InlineData("leader")]
    [InlineData("supervisor")]
    public async Task Non_admin_cannot_use_management_search_or_reorder(string role)
    {
        await using var db = MemoryDb();
        var repo = Repo(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repo.SearchUsersAsync(Actor(role), new(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repo.SearchTeamsAsync(Actor(role), new(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repo.SearchProjectsAsync(Actor(role), new(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repo.MoveVisitTypeAsync(Actor(role), 1, new("up", []), default));
    }

    [Fact]
    public async Task Team_search_uses_effective_members_and_does_not_fake_lifecycle_dates()
    {
        await using var db = MemoryDb();
        db.Teams.AddRange(
            new Team { TeamId = 10, OrganizationId = 1, TeamCode = "A10", TeamName = "Test", IsActive = true },
            new Team { TeamId = 20, OrganizationId = 2, TeamCode = "A20", TeamName = "Test", IsActive = true });
        db.UserTeamAssignments.AddRange(
            new UserTeamAssignment { UserId = 1, TeamId = 10, EffectiveFrom = BusinessTime.Today.AddDays(-1) },
            new UserTeamAssignment { UserId = 2, TeamId = 10, EffectiveFrom = BusinessTime.Today.AddDays(1) });
        await db.SaveChangesAsync();
        var result = await Repo(db).SearchTeamsAsync(Actor(), new(Keyword: "Test", IsActive: true), default);
        Assert.Equal(1, Assert.Single(result.Items).MemberCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repo(db).SearchTeamsAsync(Actor(), new(StartDate: BusinessTime.Today), default));
    }

    [Theory]
    [InlineData("NotStarted", 1)]
    [InlineData("InProgress", 2)]
    [InlineData("Ended", 3)]
    [InlineData("Inactive", 4)]
    public async Task Project_status_date_scope_and_paging(string status, int expected)
    {
        await using var db = MemoryDb();
        var today = BusinessTime.Today;
        db.Projects.AddRange(
            new Project { ProjectId = 1, OrganizationId = 1, TeamId = 10, ProjectCode = "P1", ProjectName = "Match", IsActive = true, StartDate = today.AddDays(1) },
            new Project { ProjectId = 2, OrganizationId = 1, TeamId = 10, ProjectCode = "P2", ProjectName = "Match", IsActive = true, StartDate = today, EndDate = today },
            new Project { ProjectId = 3, OrganizationId = 1, TeamId = 10, ProjectCode = "P3", ProjectName = "Match", IsActive = true, EndDate = today.AddDays(-1) },
            new Project { ProjectId = 4, OrganizationId = 1, TeamId = 10, ProjectCode = "P4", ProjectName = "Match", IsActive = false },
            new Project { ProjectId = 5, OrganizationId = 2, TeamId = 20, ProjectCode = "P5", ProjectName = "Match", IsActive = true });
        await db.SaveChangesAsync();
        var result = await Repo(db).SearchProjectsAsync(Actor(), new(Keyword: "Match", Status: status, TeamId: 10, PageSize: 1), default);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(expected, Assert.Single(result.Items).ProjectId);
        var dateRange = await Repo(db).SearchProjectsAsync(Actor(), new(StartDate: today, EndDate: today, Status: "InProgress"), default);
        Assert.Equal(2, Assert.Single(dateRange.Items).ProjectId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repo(db).SearchProjectsAsync(Actor(), new(Status: "Invalid"), default));
    }

    private static async Task SeedHistory(AppDbContext db)
    {
        db.Users.Add(new User { UserId = 1, OrganizationId = 1, EmployeeNo = "CURRENT-EMPLOYEE", DisplayName = "CURRENT-NAME" });
        db.Teams.Add(new Team { TeamId = 10, OrganizationId = 1, TeamCode = "T10", TeamName = "CURRENT-TEAM" });
        db.VisitTrips.Add(new VisitTrip { VisitTripId = 1, TripNo = "CURRENT-TRIP", UserId = 1, OrganizationId = 1, TeamId = 10,
            VisitDate = new(2026, 9, 7), Status = TripStatuses.Approved, Purpose = "CURRENT-PURPOSE" });
        foreach (var version in new[] { 1, 2 }) db.VisitTripSnapshots.Add(new VisitTripSnapshot {
            VisitTripSnapshotId = version, VisitTripId = 1, SnapshotVersion = version,
            TripNo = version == 1 ? "SUPERSEDED-TRIP" : "FROZEN-TRIP",
            UserId = 1, OrganizationId = 1, TeamId = 10, EmployeeNoSnapshot = "FROZEN-EMPLOYEE",
            DisplayNameSnapshot = "FROZEN-NAME", TeamNameSnapshot = "FROZEN-TEAM",
            VisitDate = new(2026, 9, 7), ClaimedDistanceKmSnapshot = 10, ApprovedDistanceKmSnapshot = 9,
            RatePerKmSnapshot = 2.5m, SubsidyAmountSnapshot = 22.5m,
            Stops = [new VisitTripSnapshotStop { StopSequence = 1, LocationNameSnapshot = "FROZEN-LOCATION",
                AddressSnapshot = "FROZEN-ADDRESS", ProjectNameSnapshot = "FROZEN-PROJECT", ProjectCodeSnapshot = "FROZEN-CODE" }]
        });
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("FROZEN-TRIP", 1)]
    [InlineData("FROZEN-EMPLOYEE", 1)]
    [InlineData("FROZEN-NAME", 1)]
    [InlineData("FROZEN-TEAM", 1)]
    [InlineData("FROZEN-LOCATION", 1)]
    [InlineData("FROZEN-ADDRESS", 1)]
    [InlineData("FROZEN-PROJECT", 1)]
    [InlineData("FROZEN-CODE", 1)]
    [InlineData("CURRENT", 0)]
    [InlineData("SUPERSEDED", 0)]
    public async Task Approved_query_and_export_use_latest_snapshot_without_modifying_history(string keyword, int matches)
    {
        await using var db = MemoryDb();
        await SeedHistory(db);
        var before = await db.VisitTripSnapshots.AsNoTracking().Select(x => new {
            x.VisitTripSnapshotId, x.TripNo, x.RatePerKmSnapshot, x.SubsidyAmountSnapshot
        }).ToListAsync();
        var request = new TripQueryRequest(Keyword: keyword);
        var result = await Repo(db).QueryTripsAsync(Actor(), request, false, default);
        var export = await Repo(db).QueryTripsAsync(Actor(), request, true, default);
        Assert.Equal(matches, result.TotalCount);
        Assert.Equal(result.Items.Select(x => x.VisitTripId), export.Items.Select(x => x.VisitTripId));
        if (matches > 0) {
            Assert.Equal(2, result.Items[0].SnapshotVersion);
            Assert.Equal(22.5m, result.Items[0].SubsidyAmount);
        }
        var after = await db.VisitTripSnapshots.AsNoTracking().Select(x => new {
            x.VisitTripSnapshotId, x.TripNo, x.RatePerKmSnapshot, x.SubsidyAmountSnapshot
        }).ToListAsync();
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData("CURRENT-TRIP")]
    [InlineData("CURRENT-EMPLOYEE")]
    [InlineData("CURRENT-NAME")]
    [InlineData("CURRENT-PURPOSE")]
    [InlineData("CURRENT-NOTE")]
    [InlineData("CURRENT-LOCATION")]
    [InlineData("CURRENT-ADDRESS")]
    [InlineData("CURRENT-LOCATION-CODE")]
    [InlineData("CURRENT-PROJECT")]
    [InlineData("CURRENT-PROJECT-CODE")]
    [InlineData("CURRENT-VISIT-TYPE")]
    [InlineData("CURRENT-VISIT-TYPE-CODE")]
    [InlineData("CURRENT-STOP-PURPOSE")]
    [InlineData("CURRENT-STOP-NOTE")]
    public async Task Non_approved_any_field_keyword_uses_current_data(string keyword)
    {
        await using var db = MemoryDb();
        var location = new Location { LocationId = 1, OrganizationId = 1, LocationCode = "CURRENT-LOCATION-CODE",
            LocationName = "Master name", IsActive = true };
        db.Users.Add(new User { UserId = 1, OrganizationId = 1, EmployeeNo = "CURRENT-EMPLOYEE", DisplayName = "CURRENT-NAME" });
        db.Projects.Add(new Project { ProjectId = 1, OrganizationId = 1, ProjectCode = "CURRENT-PROJECT-CODE",
            ProjectName = "CURRENT-PROJECT", IsActive = true });
        db.VisitTypes.Add(new VisitType { VisitTypeId = 1, VisitTypeCode = "CURRENT-VISIT-TYPE-CODE",
            VisitTypeName = "CURRENT-VISIT-TYPE", IsActive = true });
        db.Locations.Add(location);
        db.VisitTrips.Add(new VisitTrip { VisitTripId = 1, TripNo = "CURRENT-TRIP", UserId = 1,
            OrganizationId = 1, VisitDate = new(2026, 9, 7), Status = TripStatuses.Submitted,
            Purpose = "CURRENT-PURPOSE", Notes = "CURRENT-NOTE", Stops = [new VisitTripStop {
                StopSequence = 1, LocationId = 1, Location = location, LocationNameSnapshot = "CURRENT-LOCATION",
                AddressSnapshot = "CURRENT-ADDRESS", ProjectId = 1, VisitTypeId = 1,
                VisitPurpose = "CURRENT-STOP-PURPOSE", Notes = "CURRENT-STOP-NOTE"
            }] });
        await db.SaveChangesAsync();

        var result = await Repo(db).QueryTripsAsync(Actor(), new(Keyword: keyword), false, default);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, Assert.Single(result.Items).VisitTripId);
    }

    [Theory]
    [InlineData("admin", 2, 1, 10)]
    [InlineData("visitor", 1, 2, 10)]
    [InlineData("leader", 1, 2, 20)]
    [InlineData("supervisor", 1, 2, 10)]
    public async Task Keyword_does_not_bypass_existing_trip_data_scope(string role, int org, int id, int team)
    {
        await using var db = MemoryDb();
        await SeedHistory(db);
        var result = await Repo(db).QueryTripsAsync(Actor(role, org, id, team), new(Keyword: "FROZEN"), false, default);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Any_field_predicate_translates_to_SQL_Server_with_filter_before_pagination()
    {
        // SQL generation only: no DB or credential is used.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer((string?)null)
            .Options);
        var snapshots = db.VisitTripSnapshots.Where(s => !db.VisitTripSnapshots.Any(n =>
            n.VisitTripId == s.VisitTripId && n.SnapshotVersion > s.SnapshotVersion));
        var query = V180TripKeywordQuery.Apply(db.VisitTrips.Where(t => t.OrganizationId == 1),
            snapshots, db.Users, db.Projects, db.VisitTypes, "客戶' OR 1=1");
        var sql = query.OrderBy(t => t.VisitTripId).Skip(50).Take(50).ToQueryString();
        Assert.Contains("VisitTripSnapshots", sql);
        Assert.Contains("ProjectNameSnapshot", sql);
        Assert.Contains("OFFSET", sql);
        Assert.Contains("FETCH NEXT", sql);
        Assert.Contains("WHERE", sql);
    }

    [Fact]
    public async Task Corrections_require_filters_and_use_Taipei_request_dates()
    {
        await using var db = MemoryDb();
        await SeedHistory(db);
        var proposal = new CorrectionProposal(new(2026, 9, 7), null, null, null, 10, 9, 2.5m, 22.5m, []);
        db.CorrectionRequests.AddRange(
            new CorrectionRequest { CorrectionRequestId = 1, VisitTripId = 1, BaseSnapshotId = 2, RequestedByUserId = 1,
                Status = "PendingAdminClose", RequestedAt = new(2026, 9, 6, 16, 0, 0, DateTimeKind.Utc),
                ProposedChangesJson = JsonSerializer.Serialize(proposal, new JsonSerializerOptions(JsonSerializerDefaults.Web)) },
            new CorrectionRequest { CorrectionRequestId = 2, VisitTripId = 1, BaseSnapshotId = 2, RequestedByUserId = 1,
                Status = "PendingAdminClose", RequestedAt = new(2026, 9, 6, 15, 59, 59, DateTimeKind.Utc),
                ProposedChangesJson = JsonSerializer.Serialize(proposal, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repo(db).SearchCorrectionsAsync(Actor(), new(), default));
        var r = await Repo(db).SearchCorrectionsAsync(Actor(), new(Status: "PendingAdminClose",
            Keyword: "FROZEN", StartDate: new(2026, 9, 7), EndDate: new(2026, 9, 7)), default);
        Assert.Equal(1, Assert.Single(r.Items).CorrectionRequestId);
        var otherOrg = await Repo(db).SearchCorrectionsAsync(Actor(org: 2), new(Status: "PendingAdminClose"), default);
        Assert.Empty(otherOrg.Items);
        Assert.Equal(2, await db.VisitTripSnapshots.CountAsync());
    }
}
