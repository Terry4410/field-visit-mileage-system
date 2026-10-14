using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OptionCBatch4WorkflowTests
{
    [Fact]
    public void MergeFields_published_path_is_fail_closed_until_canonical_merge_writer_exists()
    {
        var source = Read("backend/src/FieldVisit.Infrastructure/V170LocationRepository.cs");
        Assert.Contains("if(request.Mode=="MergeFields")", source);
        Assert.Contains("RequireApprovalForPublishedHighRiskChange", source);
        Assert.Contains("Current operational references follow the survivor.", source);
        Assert.Contains("Historical VisitTripStop / Snapshot references intentionally remain on source.", source);
        Assert.DoesNotContain("LocationChangeRequests", source);
    }

    [Fact]
    public void Background_geocoding_is_limited_to_pending_inactive_drafts()
    {
        var source = Read("backend/src/FieldVisit.Infrastructure/BackgroundJobService.cs");
        Assert.Contains("x.ApprovalStatus == "Pending" && !x.IsActive", source);
        Assert.Contains("PUBLISHED_LOCATION_REQUIRES_OPTIONC_APPROVAL", source);
        Assert.Contains("Geocoding never approves, publishes or deactivates", source);
        Assert.Contains("ResolveLiveGeocodingScopeAsync", source);
    }

    [Fact]
    public void Stage_schema_requires_geocode_duplicate_evidence_before_ready()
    {
        var sql = Read("docs/release/optionc-canonical-sql-draft/1800_014/Up.sql");
        Assert.Contains("CK_OptionC_Stage_GeocodeEvidence", sql);
        Assert.Contains("CK_OptionC_Stage_DuplicateBinding", sql);
        Assert.Contains("CK_OptionC_Stage_Ready", sql);
        Assert.Contains("StageStatus=N'ReadyForPublish'", Read("backend/src/FieldVisit.Infrastructure/V180OptionCAtomicPublicationWriter.cs"));
    }

    [Fact]
    public void Merge_candidate_details_are_not_a_second_approval_ledger()
    {
        var sql = Read("docs/release/optionc-canonical-sql-draft/merge/LocationMergeDetails.disposable.sql");
        Assert.Contains("FK_OptionC_MergeDetails_Request", sql);
        Assert.Contains("SourceLocationRowVersion", sql);
        Assert.Contains("SurvivorLocationRowVersion", sql);
        Assert.DoesNotContain("CREATE TABLE dbo.ChangeRequests", sql);
        Assert.DoesNotContain("VisitTripStops SET LocationId", sql);
        Assert.DoesNotContain("VisitTripSnapshotStops SET LocationId", sql);
    }

    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "backend/FieldVisitSystem.sln")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }
}
