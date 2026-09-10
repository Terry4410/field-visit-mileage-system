using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180TripPersistenceRulesTests
{
    [Fact]
    public void Primary_defaults_only_when_context_supplies_a_deterministic_primary()
    {
        var resolved = V180TripPersistenceRules.ResolveDraftSites(Context(primary: 10), null, null);
        Assert.Equal(10, resolved.Start);
        Assert.Equal(10, resolved.End);
        var none = V180TripPersistenceRules.ResolveDraftSites(Context(), null, null);
        Assert.Null(none.Start);
        Assert.Null(none.End);
    }

    [Fact]
    public void Existing_sites_are_retained_only_while_eligible()
    {
        var resolved = V180TripPersistenceRules.ResolveDraftSites(Context(primary: 10), null, null, 11, 99);
        Assert.Equal(11, resolved.Start);
        Assert.Equal(10, resolved.End);
    }

    [Fact]
    public void Site_outside_authoritative_intersection_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            V180TripPersistenceRules.ResolveDraftSites(Context(), 99, 10));
        Assert.Contains("DEPLOYMENT_SITE_INELIGIBLE", ex.Message);
    }

    [Fact]
    public void Draft_may_be_incomplete_but_submit_requires_both_sites()
    {
        var draft = V180TripPersistenceRules.ResolveDraftSites(Context(), null, null);
        Assert.Null(draft.Start);
        Assert.Throws<InvalidOperationException>(() =>
            V180TripPersistenceRules.EnsureReadyForSubmit(Context(), 20, 1, null, 10));
        Assert.Throws<InvalidOperationException>(() =>
            V180TripPersistenceRules.EnsureReadyForSubmit(Context(), 20, 1, 10, null));
    }

    [Fact]
    public void Submit_fails_closed_when_identity_team_or_assignments_change()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V180TripPersistenceRules.EnsureReadyForSubmit(Context(), 21, 1, 10, 11));
        Assert.Throws<InvalidOperationException>(() =>
            V180TripPersistenceRules.EnsureReadyForSubmit(Context(), 20, 2, 10, 11));
        Assert.Throws<InvalidOperationException>(() =>
            V180TripPersistenceRules.EnsureReadyForSubmit(Context(), 20, 1, 10, 99));
    }

    [Fact]
    public void Trip_service_preserves_legacy_branch_and_creates_submitted_snapshot()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "backend/src/FieldVisit.Application/TripService.cs"));
        Assert.Contains("if (trip.EmploymentId.HasValue)", source);
        Assert.Contains("V170TripTeamSelectionRules.EnsureStillAllowed(user, trip.TeamId)", source);
        Assert.Contains("AddSubmittedSnapshotAsync", source);
        Assert.Contains("StartDeploymentSiteId", source);
        Assert.Contains("EndDeploymentSiteId", source);
    }

    [Fact]
    public void Snapshot_and_correction_paths_copy_immutable_deployment_fields()
    {
        var root = FindRepositoryRoot();
        var snapshotSource = File.ReadAllText(Path.Combine(root, "backend/src/FieldVisit.Infrastructure/V160Repositories.cs"));
        var correctionSource = File.ReadAllText(Path.Combine(root, "backend/src/FieldVisit.Infrastructure/V160FinalRepository.cs"));
        Assert.Contains("GetLatestAsync(trip.VisitTripId, \"Submitted\"", snapshotSource);
        Assert.Contains("CopySnapshot(submitted", snapshotSource);
        Assert.Contains("StartDeploymentAddressSnapshot = source.StartDeploymentAddressSnapshot", snapshotSource);
        Assert.Contains("StartDeploymentAddressSnapshot = baseSnapshot.StartDeploymentAddressSnapshot", correctionSource);
        Assert.Contains("EndDeploymentAddressSnapshot = baseSnapshot.EndDeploymentAddressSnapshot", correctionSource);
    }

    [Fact]
    public async Task Approved_snapshot_copies_latest_submitted_basis_and_preserves_old_address()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.VisitTripSnapshots.Add(new VisitTripSnapshot
        {
            VisitTripSnapshotId = 1, VisitTripId = 7, SnapshotVersion = 1, SnapshotType = "Submitted",
            TripNo = "T1", UserId = 1, EmploymentIdSnapshot = 20, EmployeeNoSnapshot = "E1",
            DisplayNameSnapshot = "Visitor", OrganizationId = 1, OrganizationNameSnapshot = "Org",
            StartDeploymentSiteIdSnapshot = 10, StartDeploymentSiteNameSnapshot = "Site A",
            StartDeploymentAddressSnapshot = "OLD", EndDeploymentSiteIdSnapshot = 11,
            EndDeploymentSiteNameSnapshot = "Site B", EndDeploymentAddressSnapshot = "OLD B",
            VisitDate = new(2026, 9, 1), StatusSnapshot = "Submitted", CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var trip = new VisitTrip
        {
            VisitTripId = 7, EmploymentId = 20, Status = "Approved", ApprovedAt = DateTime.UtcNow,
            MileageCalculation = new MileageCalculation { ApprovedDistanceKm = 5, RatePerKmSnapshot = 2, ApprovedAmount = 10 }
        };

        await new TripSnapshotRepository(db).AddApprovedSnapshotAsync(
            trip, new CurrentUserDto(9, "A", "Approver", null, 1, null, null, ["leader"]), default);
        await db.SaveChangesAsync();

        var approved = await db.VisitTripSnapshots.Include(x => x.Stops)
            .SingleAsync(x => x.SnapshotType == "Approved");
        Assert.Equal(2, approved.SnapshotVersion);
        Assert.Equal("OLD", approved.StartDeploymentAddressSnapshot);
        Assert.Equal("OLD B", approved.EndDeploymentAddressSnapshot);
        Assert.Equal(5, approved.ApprovedDistanceKmSnapshot);
    }

    [Fact]
    public async Task V18_approval_without_submitted_snapshot_fails_closed()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var trip = new VisitTrip { VisitTripId = 7, EmploymentId = 20 };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TripSnapshotRepository(db).AddApprovedSnapshotAsync(
                trip, new CurrentUserDto(9, "A", "Approver", null, 1, null, null, ["leader"]), default));
        Assert.Contains("SUBMITTED_SNAPSHOT_REQUIRED", ex.Message);
    }

    [Fact]
    public void Visitor_and_leader_use_v18_site_context_without_changing_stop_count_semantics()
    {
        var root = FindRepositoryRoot();
        var visitor = File.ReadAllText(Path.Combine(root, "frontend/src/pages/VisitorPage.tsx"));
        var leader = File.ReadAllText(Path.Combine(root, "frontend/src/pages/LeaderPage.tsx"));
        Assert.Contains("V180_TRIP_CONTEXT_API", visitor);
        Assert.DoesNotContain("/admin/v180/deployment-sites", visitor);
        Assert.Contains("startDeploymentSiteId", visitor);
        Assert.Contains("endDeploymentSiteId", visitor);
        Assert.Contains("t.startDeploymentSiteName", leader);
        Assert.Contains("t.endDeploymentSiteName", leader);
        Assert.Contains("const noMileage=t.stops.length<2", leader);
    }

    private static V180TripContextDto Context(int? primary = null) => new(
        20, new DateOnly(2026, 9, 9), true, "OK", "ok",
        [new(1, "T1", "Team 1", true)], 1,
        [Site(10, primary == 10), Site(11, primary == 11)], primary, primary, primary);

    private static V180TripContextDeploymentSiteDto Site(int id, bool primary) =>
        new(id, 3, "C", "Center", $"S{id}", $"Site {id}", id, $"L{id}", $"Location {id}", "Address", primary);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "backend")) && Directory.Exists(Path.Combine(current.FullName, "frontend")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
