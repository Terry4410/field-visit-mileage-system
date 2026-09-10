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
    public void Approval_copies_latest_submitted_business_basis_and_legacy_has_compatibility_path()
    {
        var source = Source("backend/src/FieldVisit.Infrastructure/V160Repositories.cs");
        Assert.Contains("GetLatestAsync(trip.VisitTripId, \"Submitted\"", source);
        Assert.Contains("CopySnapshot(submitted", source);
        Assert.Contains("else snapshot = await BuildLegacyApprovedSnapshotAsync", source);
        Assert.Contains("ProjectCodeSnapshot = source.ProjectCodeSnapshot", source);
        Assert.Contains("VisitTypeCodeSnapshot = source.VisitTypeCodeSnapshot", source);
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
