using System.IO.Compression;
using System.Text;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FieldVisit.Application.Tests;

// Isolated in-memory record/snapshot fixtures. These are NOT Azure live results.
// QueryTripsAsync is shared by on-screen query, XLSX and PDF generation.
public sealed class V180ThreeRoleHistoricalExportScopeTests
{
    private static readonly DateOnly Historical = new(2026, 11, 1);
    private static readonly DateOnly Future = new(2026, 12, 1);

    [Fact]
    public async Task Historical_exports_match_three_role_scope_and_never_mix_teams_or_organizations()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("THREE_ROLE_REPORT_" + Guid.NewGuid().ToString("N")).Options;
        await using var db = new AppDbContext(options);

        db.Organizations.AddRange(
            new Organization { OrganizationId = 1, OrganizationCode = "OWN", OrganizationName = "Own", IsActive = true },
            new Organization { OrganizationId = 2, OrganizationCode = "FOREIGN", OrganizationName = "Foreign", IsActive = true });
        db.Teams.AddRange(
            new Team { TeamId = 11, OrganizationId = 1, TeamCode = "A", TeamName = "A Team", IsActive = true },
            new Team { TeamId = 12, OrganizationId = 1, TeamCode = "B", TeamName = "B Team", IsActive = true },
            new Team { TeamId = 21, OrganizationId = 2, TeamCode = "X", TeamName = "Other Org Team", IsActive = true });
        db.Users.AddRange(
            new User { UserId = 1, OrganizationId = 1, TeamId = 11, EmployeeNo = "V1", DisplayName = "Visitor A", IsActive = true },
            new User { UserId = 2, OrganizationId = 1, TeamId = 12, EmployeeNo = "V2", DisplayName = "Visitor B", IsActive = true },
            new User { UserId = 3, OrganizationId = 2, TeamId = 21, EmployeeNo = "V3", DisplayName = "Visitor X", IsActive = true });
        db.VisitTrips.AddRange(
            Trip(101, "HIST_OWN_TEAM", 1, 11, 1),
            Trip(102, "HIST_OTHER_TEAM", 2, 12, 1),
            Trip(103, "HIST_OTHER_ORG", 3, 21, 2),
            Trip(104, "HIST_SNAPSHOT_MOVED", 1, 11, 1, "Approved"),
            Trip(105, "HIST_SNAPSHOT_ALLOWED", 1, 11, 1, "Approved"),
            Trip(106, "FUTURE_OWN_TEAM", 1, 11, 1, visitDate: Future));
        db.VisitTripSnapshots.AddRange(
            Snapshot(104, "HIST_SNAPSHOT_MOVED", 12, 1),
            Snapshot(105, "HIST_SNAPSHOT_ALLOWED", 11, 1));
        await db.SaveChangesAsync();

        var repo = new V160FinalRepository(db, null!);
        var query = new TripQueryRequest(StartDate: Historical, EndDate: Future);
        var report = new ReportDocumentService(new ConfigurationBuilder().Build());

        var leader = Actor("leader", 77, 1, 11);
        var visitor = Actor("visitor", 1, 1, 11);
        var admin = Actor("admin", 88, 1, null);
        var foreignAdmin = Actor("admin", 99, 2, null);

        async Task<IReadOnlyList<TripQueryRowDto>> Verify(
            CurrentUserDto actor, params string[] expected)
        {
            var paged = await repo.QueryTripsAsync(actor, query, false, default);
            var all = await repo.QueryTripsAsync(actor, query, true, default);
            var actual = all.Items.Select(x => x.TripNo).OrderBy(x => x).ToArray();
            Assert.Equal(expected.OrderBy(x => x).ToArray(), actual);
            Assert.Equal(actual, paged.Items.Select(x => x.TripNo).OrderBy(x => x).ToArray());

            var excel = await report.CreateExcelAsync(all.Items, query, actor, default);
            Assert.EndsWith(".xlsx", excel.FileName);
            Assert.StartsWith("application/vnd.openxmlformats", excel.ContentType);
            using var archive = new ZipArchive(new MemoryStream(excel.Content));
            var names = string.Join("\n", archive.Entries
                .Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)
                    && e.FullName.EndsWith(".xml", StringComparison.Ordinal))
                .Select(e =>
                {
                    using var reader = new StreamReader(e.Open(), Encoding.UTF8);
                    return reader.ReadToEnd();
                }));
            foreach (var id in expected) Assert.Contains(id, names);
            foreach (var id in new[]
                {"HIST_OWN_TEAM", "HIST_OTHER_TEAM", "HIST_OTHER_ORG",
                 "HIST_SNAPSHOT_MOVED", "HIST_SNAPSHOT_ALLOWED", "FUTURE_OWN_TEAM"}
                .Except(expected))
                Assert.DoesNotContain(id, names);

            var pdf = await report.CreatePdfAsync(all.Items, query, actor, default);
            Assert.Equal("application/pdf", pdf.ContentType);
            Assert.True(pdf.Content.Length > 2000);
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf.Content.AsSpan(0, 5)));
            return all.Items;
        }

        // Approved row 104 is currently linked to team A, but the immutable
        // latest approved snapshot belongs to team B: leader A must not see it.
        await Verify(leader, "HIST_OWN_TEAM", "HIST_SNAPSHOT_ALLOWED", "FUTURE_OWN_TEAM");
        await Verify(visitor, "HIST_OWN_TEAM", "HIST_SNAPSHOT_MOVED", "HIST_SNAPSHOT_ALLOWED", "FUTURE_OWN_TEAM");
        await Verify(admin, "HIST_OWN_TEAM", "HIST_OTHER_TEAM",
            "HIST_SNAPSHOT_MOVED", "HIST_SNAPSHOT_ALLOWED", "FUTURE_OWN_TEAM");
        await Verify(foreignAdmin, "HIST_OTHER_ORG");

        // Date and status are caller-selected filters inside the existing role scope.
        var futureOnly = query with { StartDate = Future, EndDate = Future };
        foreach (var actor in new[] { leader, visitor, admin })
            Assert.Equal(new[] { "FUTURE_OWN_TEAM" },
                (await repo.QueryTripsAsync(actor, futureOnly, true, default)).Items.Select(x => x.TripNo));
        Assert.Empty((await repo.QueryTripsAsync(foreignAdmin, futureOnly, true, default)).Items);
        var approvedOnly = query with { Status = "Approved" };
        Assert.Equal(new[] { "HIST_SNAPSHOT_ALLOWED" },
            (await repo.QueryTripsAsync(leader, approvedOnly, true, default)).Items.Select(x => x.TripNo));

        // Narrow explicit filter cannot bypass server-side team, self or org.
        var wrongFilter = query with { TeamId = 12, VisitorId = 2 };
        var leaderOther = await repo.QueryTripsAsync(leader, wrongFilter, true, default);
        Assert.Empty(leaderOther.Items);
        var visitorOther = await repo.QueryTripsAsync(visitor, wrongFilter, true, default);
        Assert.Empty(visitorOther.Items);
    }

    private static CurrentUserDto Actor(string role, int userId, int orgId, int? teamId) =>
        new(userId, "ACTOR", role, null, orgId, teamId, null, [role]);

    private static VisitTrip Trip(long id, string tripNo, int user, int? team, int org,
        string status = "Draft", DateOnly visitDate = default) =>
        new()
        {
            VisitTripId = id, TripNo = tripNo, UserId = user,
            OrganizationId = org, TeamId = team, VisitDate = visitDate == default ? Historical : visitDate,
            Status = status, CreatedByUserId = user, CreatedAt = DateTime.UtcNow
        };

    private static VisitTripSnapshot Snapshot(long tripId, string tripNo, int? team, int org) =>
        new()
        {
            VisitTripSnapshotId = tripId + 1000, VisitTripId = tripId, SnapshotVersion = 1,
            TripNo = tripNo, UserId = 1, OrganizationId = org,
            TeamId = team, TeamNameSnapshot = team == 11 ? "A Team" : "B Team",
            DisplayNameSnapshot = "Visitor A", EmployeeNoSnapshot = "V1",
            VisitDate = Historical, CreatedAt = DateTime.UtcNow
        };
}
