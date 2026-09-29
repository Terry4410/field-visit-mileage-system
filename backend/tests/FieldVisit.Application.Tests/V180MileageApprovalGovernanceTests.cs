using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180MileageApprovalGovernanceTests
{
    [Fact]
    public void Normal_approval_sources_are_explicit_and_claimed_is_not_company_evidence()
    {
        Assert.Equal("ProviderSuggested", V180MileageGovernanceRules.RequireDecisionSource("ProviderSuggested"));
        Assert.Equal("LeaderAdjusted", V180MileageGovernanceRules.RequireDecisionSource("LeaderAdjusted"));
        Assert.Equal("ManualFallback", V180MileageGovernanceRules.RequireDecisionSource("ManualFallback"));
        Assert.Throws<InvalidOperationException>(() => V180MileageGovernanceRules.RequireDecisionSource(null));
        Assert.Throws<InvalidOperationException>(() => V180MileageGovernanceRules.RequireDecisionSource("Claimed"));
    }

    [Fact]
    public void Correction_distance_decision_excludes_leader_adjusted()
    {
        Assert.Equal("ProviderSuggested", V180MileageGovernanceRules.RequireCorrectionDecisionSource("ProviderSuggested"));
        Assert.Equal("ManualFallback", V180MileageGovernanceRules.RequireCorrectionDecisionSource("ManualFallback"));
        Assert.Throws<InvalidOperationException>(() => V180MileageGovernanceRules.RequireCorrectionDecisionSource("LeaderAdjusted"));
        Assert.Equal("SubmittedSnapshot", V180MileageGovernanceRules.SubmittedSnapshotBasisCode);
        Assert.Equal("CorrectionProposal", V180MileageGovernanceRules.CorrectionProposalBasisCode);
    }

    [Fact]
    public void Submitted_trip_cannot_be_approved_directly()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => V180MileageGovernanceRules.EnsureLeaderApprovalState("Submitted"));

        Assert.Contains("F_B_APPROVAL_STATE_REQUIRED", ex.Message);
        V180MileageGovernanceRules.EnsureLeaderApprovalState("PendingApproval");
    }

    [Fact]
    public void Pending_approval_without_decision_evidence_fails_closed()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => V180MileageGovernanceRules.RequireLeaderApprovalDecisionSource(null));

        Assert.Contains("F_B_APPROVAL_EVIDENCE_REQUIRED", ex.Message);
    }

    [Fact]
    public void Batch_approval_forwards_evidence_and_has_no_legacy_bypass()
    {
        var source = ReadRepositoryFile(
            "backend/src/FieldVisit.Application/LeaderService.cs");

        Assert.Contains("item.DistanceDecisionSource", source);
        Assert.Contains("item.RouteCalculationAttemptId", source);
        Assert.DoesNotContain("unchangedLegacyPendingApproval", source);
        Assert.Contains("RequireLeaderApprovalDecisionSource", source);
    }

    [Fact]
    public void Mileage_job_failure_uses_stable_ids_and_requeries_after_clear()
    {
        var source = ReadRepositoryFile(
            "backend/src/FieldVisit.Infrastructure/BackgroundJobService.cs");

        Assert.Contains("ProcessMileageAsync(Guid jobId", source);
        Assert.Contains(".Select(x => x.VisitTripId)", source);
        Assert.Contains("db.ChangeTracker.Clear();", source);
        Assert.Contains("SingleAsync(x => x.VisitTripId == tripId", source);
        Assert.Contains("SingleAsync(x => x.BackgroundJobItemId == itemId", source);
        Assert.Contains("SingleAsync(x => x.BackgroundJobId == jobId", source);
        Assert.Contains("terminalJob.Status", source);
        Assert.DoesNotContain("job.Status = job.FailedCount", source);
        Assert.DoesNotContain("calc.ApprovedDistanceKm = result.DistanceKm", source);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return File.ReadAllText(
                    Path.Combine(
                        current.FullName,
                        relativePath.Replace('/', Path.DirectorySeparatorChar)));
            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
