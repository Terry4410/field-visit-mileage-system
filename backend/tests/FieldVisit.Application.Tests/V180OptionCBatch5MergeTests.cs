using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OptionCBatch5MergeTests
{
    [Fact]
    public void Merge_submission_uses_canonical_request_and_additive_detail()
    {
        var service = Read("backend/src/FieldVisit.Infrastructure/V180OptionCMergeService.cs");
        Assert.Contains("LocationMergeDetails", service);
        Assert.Contains("IsolationLevel.Serializable", service);
        Assert.Contains("SourceLocationRowVersion", service);
        Assert.Contains("SurvivorLocationRowVersion", service);
        Assert.Contains("EventType = \"Submitted\"", service);
        Assert.Contains("V180OptionCStageDraftWriter", service);
    }

    [Fact]
    public void Merge_writer_locks_in_deterministic_order_and_preserves_history()
    {
        var writer = Read("backend/src/FieldVisit.Infrastructure/V180OptionCMergePublicationWriter.cs");
        Assert.Contains("UPDLOCK,HOLDLOCK", writer);
        Assert.Contains("IF @source < @survivor", writer);
        Assert.DoesNotContain("VisitTrip", writer);
        Assert.DoesNotContain("VisitTripSnapshot", writer);
        Assert.Contains("LocationPublishedVersions", writer);
        Assert.Contains("StageStatus=N'Published'", writer);
        Assert.Contains("EventType,ActorUserId", writer);
    }

    [Fact]
    public void Candidate_http_surface_has_merge_submission_and_reuses_decision_routes()
    {
        var controller = Read("backend/src/FieldVisit.Api/Controllers/V180OptionCCandidateController.cs");
        Assert.Contains("locations/merge-fields", controller);
        Assert.Contains("requests/{id:guid}/approve", controller);
        Assert.Contains("requests/{id:guid}/reject", controller);
        Assert.Contains("requests/{id:guid}/reconcile", controller);
    }

    [Fact]
    public void Merge_detail_schema_is_additive_and_disposable()
    {
        var sql = Read("docs/release/optionc-canonical-sql-draft/merge/LocationMergeDetails.disposable.sql");
        Assert.Contains("FK_OptionC_MergeDetails_Request", sql);
        Assert.Contains("SourceLocationRowVersion", sql);
        Assert.Contains("SurvivorLocationRowVersion", sql);
        Assert.Contains("DB_NAME() NOT LIKE N'OptionCIsolated[_]%'", sql);
        Assert.DoesNotContain("CREATE TABLE dbo.ChangeRequests", sql);
        Assert.DoesNotContain("UPDATE dbo.VisitTripStops", sql);
        Assert.DoesNotContain("UPDATE dbo.VisitTripSnapshotStops", sql);
    }

    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "backend/FieldVisitSystem.sln")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }
}
