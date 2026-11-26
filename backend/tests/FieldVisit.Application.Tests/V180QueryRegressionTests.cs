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
    private static V160FinalRepository Repo(AppDbContext db) => new(db, new V170AccessControl(db));

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
            snapshots, db.Users, db.Projects, db.VisitTypes, db.DeploymentSites, db.Centers,
            "客戶' OR 1=1");
        var sql = query.OrderBy(t => t.VisitTripId).Skip(50).Take(50).ToQueryString();
        Assert.Contains("VisitTripSnapshots", sql);
        Assert.Contains("ProjectNameSnapshot", sql);
        Assert.Contains("OFFSET", sql);
        Assert.Contains("FETCH NEXT", sql);
        Assert.Contains("WHERE", sql);
    }

    [Theory]
    [InlineData("SITE-ALPHA")]
    [InlineData("Alpha official site")]
    [InlineData("CENTER-A")]
    [InlineData("Alpha center")]
    [InlineData("Frozen deployment address")]
    public async Task Trip_keyword_covers_official_site_and_center_business_fields(string keyword)
    {
        await using var db = MemoryDb();
        db.Users.Add(new User { UserId = 1, OrganizationId = 1, EmployeeNo = "E001", DisplayName = "Tester" });
        db.Centers.Add(new Center { CenterId = 1, OrganizationId = 1, CenterCode = "CENTER-A", CenterName = "Alpha center", EffectiveFrom = new(2026,1,1), IsActive = true });
        db.DeploymentSites.Add(new DeploymentSite { DeploymentSiteId = 1, CenterId = 1, SiteCode = "SITE-ALPHA", SiteName = "Alpha official site", EffectiveFrom = new(2026,1,1), IsActive = true });
        db.VisitTrips.Add(new VisitTrip { VisitTripId = 31, TripNo = "T-31", UserId = 1, OrganizationId = 1, VisitDate = new(2026,10,8), Status = TripStatuses.Approved });
        db.VisitTripSnapshots.Add(new VisitTripSnapshot { VisitTripSnapshotId = 31, VisitTripId = 31, SnapshotVersion = 1, TripNo = "T-31", UserId = 1, OrganizationId = 1, EmployeeNoSnapshot = "E001", DisplayNameSnapshot = "Tester", OrganizationNameSnapshot = "Org", StartDeploymentSiteCodeSnapshot = "SITE-ALPHA", StartDeploymentAddressSnapshot = "Frozen deployment address", VisitDate = new(2026,10,8) });
        await db.SaveChangesAsync();
        var result = await Repo(db).QueryTripsAsync(Actor(), new TripQueryRequest(Keyword: keyword), false, default);
        Assert.Equal(31, Assert.Single(result.Items).VisitTripId);
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


    [Fact]
    public async Task Explicit_all_corrections_preserves_scope_filters_and_pagination()
    {
        await using var db = MemoryDb();
        await SeedHistory(db);
        db.Users.AddRange(new User { UserId = 2, OrganizationId = 1, EmployeeNo = "OTHER-TEAM", DisplayName = "Other team" },
            new User { UserId = 3, OrganizationId = 2, EmployeeNo = "OTHER-ORG", DisplayName = "Other org" });
        db.Teams.AddRange(new Team { TeamId = 20, OrganizationId = 1, TeamCode = "T20", TeamName = "Other team" },
            new Team { TeamId = 30, OrganizationId = 2, TeamCode = "T30", TeamName = "Other org" });
        for (int id = 2; id <= 3; id++) {
            var org = id == 2 ? 1 : 2; var team = id * 10;
            db.VisitTrips.Add(new VisitTrip { VisitTripId = id, TripNo = $"OTHER-{id}", UserId = id, OrganizationId = org, TeamId = team, VisitDate = new(2026,9,7), Status = TripStatuses.Approved });
            db.VisitTripSnapshots.Add(new VisitTripSnapshot { VisitTripSnapshotId = id+10, VisitTripId = id, SnapshotVersion = 1, TripNo = $"OTHER-{id}", UserId = id, OrganizationId = org, TeamId = team, EmployeeNoSnapshot = $"E{id}", DisplayNameSnapshot = $"Other {id}", VisitDate = new(2026,9,7) });
        }
        var proposal = new CorrectionProposal(new(2026,9,7), null, null, null, 10, 9, 2.5m, 22.5m, []);
        var json = JsonSerializer.Serialize(proposal, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        string[] states = ["PendingLeaderReview", "PendingAdminClose", "Closed", "Rejected"];
        for(int i=1;i<=6;i++) {
            var tripId=i<=4?1:i-3;
            db.CorrectionRequests.Add(new CorrectionRequest { CorrectionRequestId=i, VisitTripId=tripId, BaseSnapshotId=tripId==1?2:tripId+10, RequestedByUserId=tripId, Status=states[(i-1)%4], Reason=i==2?"FINDME":"Other", RequestedAt=new DateTime(2026,9,6,16,0,0,DateTimeKind.Utc).AddDays(i-1), ProposedChangesJson=json });
        }
        await db.SaveChangesAsync();
        var repo=Repo(db);
        var admin=await repo.SearchCorrectionsAsync(Actor(), new(Status:"All",PageSize:2), default);
        Assert.Equal(5,admin.TotalCount); Assert.Equal(new long[]{5,4},admin.Items.Select(x=>x.CorrectionRequestId));
        var next=await repo.SearchCorrectionsAsync(Actor(), new(Status:"All",Page:2,PageSize:2), default);
        Assert.Equal(new long[]{3,2},next.Items.Select(x=>x.CorrectionRequestId));
        foreach(var role in new[]{"visitor","leader"}) {
            var scoped=await repo.SearchCorrectionsAsync(Actor(role), new(Status:"All"), default);
            Assert.Equal(new long[]{4,3,2,1},scoped.Items.Select(x=>x.CorrectionRequestId));
        }
        var exact=await repo.SearchCorrectionsAsync(Actor(), new(Status:"All",VisitTripId:1,TripNo:"FROZEN-TRIP",PageSize:2), default);
        Assert.Equal(4,exact.TotalCount); Assert.Equal(new long[]{4,3},exact.Items.Select(x=>x.CorrectionRequestId));
        var deniedExact=await repo.SearchCorrectionsAsync(Actor("visitor"), new(Status:"All",VisitTripId:2,TripNo:"OTHER-2"), default);
        Assert.Empty(deniedExact.Items);
        var wrongNo=await repo.SearchCorrectionsAsync(Actor(), new(Status:"All",VisitTripId:1,TripNo:"OTHER-2"), default);
        Assert.Empty(wrongNo.Items);
        var other=await repo.SearchCorrectionsAsync(Actor(org:2), new(Status:"All"), default);
        Assert.Equal(6,Assert.Single(other.Items).CorrectionRequestId);
        var filtered=await repo.SearchCorrectionsAsync(Actor(), new(Status:"All",Keyword:"FINDME",StartDate:new(2026,9,8),EndDate:new(2026,9,8)),default);
        Assert.Equal(2,Assert.Single(filtered.Items).CorrectionRequestId);
        var closed=await repo.SearchCorrectionsAsync(Actor(), new(Status:"Closed"),default);
        Assert.Equal(3,Assert.Single(closed.Items).CorrectionRequestId);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.SearchCorrectionsAsync(Actor(),new(Status:"Invalid"),default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.SearchCorrectionsAsync(Actor(),new(Status:"All",StartDate:new(2026,9,9),EndDate:new(2026,9,8)),default));
        Assert.Equal(6,await db.CorrectionRequests.CountAsync()); Assert.Equal(4,await db.VisitTripSnapshots.CountAsync());
    }

    private sealed class ReportActor(CurrentUserDto actor) : ICurrentUserService
    { public CurrentUserDto GetRequired()=>actor; }

    [Fact]
    public async Task Correction_export_reads_all_pages_of_exact_scoped_trip()
    {
        await using var db=MemoryDb();
        await SeedHistory(db);
        var proposal=new CorrectionProposal(new(2026,9,7),null,null,null,10,9,2.5m,22.5m,[]);
        var json=JsonSerializer.Serialize(proposal,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        for(var i=1;i<=105;i++)
            db.CorrectionRequests.Add(new CorrectionRequest{CorrectionRequestId=1000+i,VisitTripId=1,BaseSnapshotId=2,
                RequestedByUserId=1,Status="Closed",Reason=$"EXPORT-{i:000}",RequestedAt=new DateTime(2026,9,6,16,0,0,DateTimeKind.Utc).AddMinutes(i),ProposedChangesJson=json});
        await db.SaveChangesAsync();
        foreach(var role in new[]{"admin","leader","visitor"})
        {
            var controller=new FieldVisit.Api.Controllers.V180HistoryRateReportController(Repo(db),new ReportActor(Actor(role)),null!,new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            var file=Assert.IsType<Microsoft.AspNetCore.Mvc.FileContentResult>(await controller.CorrectionExcel(new(Status:"All",VisitTripId:1,TripNo:"FROZEN-TRIP",Page:3,PageSize:1),default));
            using var book=new NPOI.XSSF.UserModel.XSSFWorkbook(new MemoryStream(file.FileContents));
            var sheet=book.GetSheet("查詢結果");
            Assert.Equal(105,sheet.LastRowNum);
            Assert.Equal("EXPORT-105",sheet.GetRow(1).GetCell(3).StringCellValue);
            Assert.Equal("EXPORT-001",sheet.GetRow(105).GetCell(3).StringCellValue);
            Assert.Equal("2026-09-07",sheet.GetRow(1).GetCell(0).StringCellValue);
        }
        var outsider=new FieldVisit.Api.Controllers.V180HistoryRateReportController(Repo(db),new ReportActor(Actor(org:2)),null!,new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var denied=Assert.IsType<Microsoft.AspNetCore.Mvc.FileContentResult>(await outsider.CorrectionExcel(new(Status:"All",VisitTripId:1,TripNo:"FROZEN-TRIP"),default));
        using var empty=new NPOI.XSSF.UserModel.XSSFWorkbook(new MemoryStream(denied.FileContents));
        Assert.Equal(0,empty.GetSheet("查詢結果").LastRowNum);
        Assert.Equal(105,await db.CorrectionRequests.CountAsync());
    }

    [Theory]
    [InlineData("Leader says ok")]
    [InlineData("Admin closed note")]
    [InlineData("OLD-VALUE")]
    [InlineData("VISIT-CODE")]
    [InlineData("FROZEN-ADDRESS")]
    public async Task Correction_keyword_searches_operational_fields_without_bypassing_scope(string keyword)
    {
        await using var db = MemoryDb();
        await SeedHistory(db);
        var snapshot = await db.VisitTripSnapshots.OrderByDescending(x => x.SnapshotVersion).Include(x=>x.Stops).FirstAsync();
        snapshot.StartDeploymentSiteCodeSnapshot = "SITE-CORR";
        snapshot.StartDeploymentAddressSnapshot = "FROZEN-ADDRESS";
        snapshot.Stops[0].VisitTypeCodeSnapshot = "VISIT-CODE";
        db.CorrectionRequests.Add(new CorrectionRequest { CorrectionRequestId = 77, VisitTripId = 1, BaseSnapshotId = snapshot.VisitTripSnapshotId, RequestedByUserId = 1, Status = "PendingAdminClose", Reason = "Correction reason", LeaderComments = "Leader says ok", AdminComments = "Admin closed note", RequestedAt = DateTime.UtcNow, ProposedChangesJson = "{}" });
        db.CorrectionRequestChanges.Add(new CorrectionRequestChange { CorrectionRequestChangeId = 77, CorrectionRequestId = 77, FieldName = "Notes", OldValue = "OLD-VALUE", NewValue = "NEW-VALUE", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var result = await Repo(db).SearchCorrectionsAsync(Actor(), new V180SearchRequest(Keyword: keyword), default);
        Assert.Equal(77, Assert.Single(result.Items).CorrectionRequestId);
    }

    [Fact]
    public async Task Survivor_location_keyword_resolves_preserved_source_history_without_rewrite()
    {
        await using var db = MemoryDb();
        db.Users.Add(new User { UserId = 1, OrganizationId = 1, EmployeeNo = "E001", DisplayName = "Tester" });
        db.Teams.Add(new Team { TeamId = 10, OrganizationId = 1, TeamCode = "T10", TeamName = "Team" });
        db.Locations.AddRange(
            new Location { LocationId = 201, OrganizationId = 1, LocationCode = "OLD-LOC", LocationName = "OLD LOCATION",
                IsActive = false, DuplicateOfLocationId = 202, DuplicateReason = "merged" },
            new Location { LocationId = 202, OrganizationId = 1, LocationCode = "SURVIVOR-LOC", LocationName = "SURVIVOR MASTER",
                Address = "SURVIVOR ADDRESS", IsActive = true, ApprovalStatus = "Approved" });
        db.VisitTrips.Add(new VisitTrip { VisitTripId = 21, TripNo = "T-21", UserId = 1, OrganizationId = 1, TeamId = 10,
            VisitDate = new(2026, 9, 30), Status = TripStatuses.Approved, Purpose = "visit" });
        db.VisitTripSnapshots.Add(new VisitTripSnapshot {
            VisitTripSnapshotId = 21, VisitTripId = 21, SnapshotVersion = 1, TripNo = "T-21",
            UserId = 1, OrganizationId = 1, TeamId = 10, EmployeeNoSnapshot = "E001",
            DisplayNameSnapshot = "Tester", TeamNameSnapshot = "Team", VisitDate = new(2026, 9, 30),
            Stops = [new VisitTripSnapshotStop { StopSequence = 1, LocationId = 201,
                LocationCodeSnapshot = "OLD-LOC", LocationNameSnapshot = "OLD LOCATION", AddressSnapshot = "OLD ADDRESS" }]
        });
        await db.SaveChangesAsync();

        var result = await Repo(db).QueryTripsAsync(
            Actor(), new TripQueryRequest(LocationKeyword: "SURVIVOR MASTER"), false, default);

        Assert.Equal(21, Assert.Single(result.Items).VisitTripId);
        Assert.Equal(201, await db.VisitTripSnapshotStops.Select(x => x.LocationId).SingleAsync());
        Assert.Equal(202, await db.Locations.Where(x => x.LocationId == 201).Select(x => x.DuplicateOfLocationId).SingleAsync());
    }
}
