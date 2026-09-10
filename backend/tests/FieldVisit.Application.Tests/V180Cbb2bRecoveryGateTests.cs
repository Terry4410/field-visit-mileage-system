using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180Cbb2bRecoveryGateTests
{
    [Fact]
    public void V18_submit_uses_visit_date_authority_while_legacy_retains_v17_rule()
    {
        var source = Source("backend/src/FieldVisit.Application/TripService.cs");
        var v18 = source.IndexOf("if (trip.EmploymentId.HasValue)", StringComparison.Ordinal);
        var resolve = source.IndexOf("tripContext.ResolveAsync(user, trip.VisitDate, trip.TeamId, ct)", v18, StringComparison.Ordinal);
        var legacy = source.IndexOf("V170TripTeamSelectionRules.EnsureStillAllowed(user, trip.TeamId)", resolve, StringComparison.Ordinal);
        Assert.True(v18 >= 0 && resolve > v18 && legacy > resolve);
    }

    [Fact]
    public async Task Approval_copies_latest_submitted_business_basis_and_legacy_has_compatibility_path()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var older = SubmittedSnapshot(1, 7, 1, "P-OLD", "Old Project", "VT-OLD", "Old Visit Type");
        var latest = SubmittedSnapshot(2, 7, 3, "P-FROZEN", "Frozen Project", "VT-FROZEN", "Frozen Visit Type");
        db.VisitTripSnapshots.AddRange(older, latest);
        await db.SaveChangesAsync();

        var trip = new VisitTrip
        {
            VisitTripId = 7,
            EmploymentId = 20,
            Status = "Approved",
            ApprovedAt = DateTime.UtcNow,
            MileageCalculation = new MileageCalculation
            {
                ApprovedDistanceKm = 5,
                RatePerKmSnapshot = 2,
                ApprovedAmount = 10
            }
        };

        await new TripSnapshotRepository(db).AddApprovedSnapshotAsync(
            trip,
            new CurrentUserDto(9, "A", "Approver", null, 1, null, null, ["leader"]),
            default);
        await db.SaveChangesAsync();

        var approved = await db.VisitTripSnapshots.Include(x => x.Stops)
            .SingleAsync(x => x.SnapshotType == "Approved");
        var stop = Assert.Single(approved.Stops);

        Assert.Equal(4, approved.SnapshotVersion);
        Assert.Equal("Frozen Team", approved.TeamNameSnapshot);
        Assert.Equal("Frozen Start Address", approved.StartDeploymentAddressSnapshot);
        Assert.Equal("Frozen End Address", approved.EndDeploymentAddressSnapshot);
        Assert.Equal("Frozen Location", stop.LocationNameSnapshot);
        Assert.Equal("Frozen Location Address", stop.AddressSnapshot);
        Assert.Equal("P-FROZEN", stop.ProjectCodeSnapshot);
        Assert.Equal("Frozen Project", stop.ProjectNameSnapshot);
        Assert.Equal("VT-FROZEN", stop.VisitTypeCodeSnapshot);
        Assert.Equal("Frozen Visit Type", stop.VisitTypeNameSnapshot);

        var source = Source("backend/src/FieldVisit.Infrastructure/V160Repositories.cs");
        Assert.Contains("else snapshot = await BuildLegacyApprovedSnapshotAsync", source);
    }

    [Fact]
    public void V18_correction_visit_date_change_is_rejected_without_rewriting_legacy_repository_rules()
    {
        var service = Source("backend/src/FieldVisit.Application/V160FinalService.cs");
        Assert.Contains("if (trip.EmploymentId.HasValue)", service);
        Assert.Contains("request.Proposal.VisitDate != draft.Proposal.VisitDate", service);
        Assert.Contains("V180_CORRECTION_VISIT_DATE_NOT_SUPPORTED", service);

        var repository = Source("backend/src/FieldVisit.Infrastructure/V160FinalRepository.cs");
        Assert.Contains("ShouldPreserveSnapshotRate", repository);
        Assert.Contains("re-evaluate rate using the corrected business date", repository);
    }

    [Fact]
    public void Submit_and_approve_queue_business_writes_before_one_savechanges_commit()
    {
        var submit = Method(Source("backend/src/FieldVisit.Application/TripService.cs"),
            "public async Task<TripDto> SubmitAsync", "public async Task<TimeOverlapResult> CheckOverlapAsync");
        Assert.Contains("AddHistoryAsync", submit);
        Assert.Contains("AuditAsync", submit);
        Assert.Contains("AddSubmittedSnapshotAsync", submit);
        Assert.Equal(1, Count(submit, "uow.SaveChangesAsync(ct)"));

        var approve = Method(Source("backend/src/FieldVisit.Application/LeaderService.cs"),
            "public async Task<TripDto> ApproveAsync", "public async Task<TripDto> ReturnAsync");
        Assert.Contains("AddApprovalAsync", approve);
        Assert.Contains("AddStatusHistoryAsync", approve);
        Assert.Contains("AddAuditAsync", approve);
        Assert.Contains("AddApprovedSnapshotAsync", approve);
        Assert.Equal(1, Count(approve, "uow.SaveChangesAsync(ct)"));
    }

    [Fact]
    public void Concurrency_guards_cover_trip_rowversion_and_snapshot_version_uniqueness()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var trip = db.Model.FindEntityType(typeof(VisitTrip))!;
        Assert.True(trip.FindProperty(nameof(VisitTrip.RowVersion))!.IsConcurrencyToken);
        var snapshot = db.Model.FindEntityType(typeof(VisitTripSnapshot))!;
        Assert.Contains(snapshot.GetIndexes(), x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(VisitTripSnapshot.VisitTripId), nameof(VisitTripSnapshot.SnapshotVersion) }));
    }

    [Fact]
    public void Frozen_schema_findings_are_visible_but_not_modified_by_runtime_correction()
    {
        var siteMigration = Source("database/migrations/1800_003_deployment_sites/Up.sql");
        Assert.Contains("i.IsPrimary=1 AND x.IsPrimary=1", siteMigration);
        Assert.DoesNotContain("i.IsPrimary=0 AND x.IsPrimary=0", siteMigration);

        var teamCenterMigration = Source("database/migrations/1800_001_organization_center_team_lifecycle/Up.sql");
        Assert.DoesNotContain("TeamDeploymentSiteAssignments", teamCenterMigration);
    }

    private static VisitTripSnapshot SubmittedSnapshot(
        long id, long tripId, int version,
        string projectCode, string projectName,
        string visitTypeCode, string visitTypeName)
    {
        var snapshot = new VisitTripSnapshot
        {
            VisitTripSnapshotId = id,
            VisitTripId = tripId,
            SnapshotVersion = version,
            SnapshotType = "Submitted",
            TripNo = "T1",
            UserId = 1,
            PersonIdSnapshot = 10,
            EmploymentIdSnapshot = 20,
            EmployeeNoSnapshot = "E1",
            DisplayNameSnapshot = "Visitor",
            OrganizationId = 1,
            OrganizationNameSnapshot = "Frozen Org",
            TeamId = 100,
            TeamCodeSnapshot = "T-FROZEN",
            TeamNameSnapshot = "Frozen Team",
            CenterIdSnapshot = 200,
            CenterCodeSnapshot = "C-FROZEN",
            CenterNameSnapshot = "Frozen Center",
            StartDeploymentSiteIdSnapshot = 300,
            StartDeploymentSiteCodeSnapshot = "S-START",
            StartDeploymentSiteNameSnapshot = "Frozen Start Site",
            StartDeploymentLocationIdSnapshot = 400,
            StartDeploymentAddressSnapshot = "Frozen Start Address",
            EndDeploymentSiteIdSnapshot = 301,
            EndDeploymentSiteCodeSnapshot = "S-END",
            EndDeploymentSiteNameSnapshot = "Frozen End Site",
            EndDeploymentLocationIdSnapshot = 401,
            EndDeploymentAddressSnapshot = "Frozen End Address",
            VisitDate = new DateOnly(2026, 9, 1),
            StatusSnapshot = "Submitted",
            CreatedAt = DateTime.UtcNow
        };
        snapshot.Stops.Add(new VisitTripSnapshotStop
        {
            StopSequence = 1,
            LocationId = 500,
            LocationCodeSnapshot = "L-FROZEN",
            LocationNameSnapshot = "Frozen Location",
            AddressSnapshot = "Frozen Location Address",
            ProjectId = 600,
            ProjectCodeSnapshot = projectCode,
            ProjectNameSnapshot = projectName,
            VisitTypeId = 700,
            VisitTypeCodeSnapshot = visitTypeCode,
            VisitTypeNameSnapshot = visitTypeName,
            VisitPurposeSnapshot = "Frozen Purpose",
            NotesSnapshot = "Frozen Stop Notes",
            CreatedAt = DateTime.UtcNow
        });
        return snapshot;
    }

    private static string Method(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
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
