using FieldVisit.Application;
using FieldVisit.Domain.Entities;
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
    public void Leader_adjustment_reason_must_contain_meaningful_individual_content()
    {
        Assert.Equal("Dispatch timing required this adjustment.",
            V180MileageGovernanceRules.RequireLeaderAdjustmentReason(" Dispatch timing required this adjustment. "));
        Assert.Throws<InvalidOperationException>(
            () => V180MileageGovernanceRules.RequireLeaderAdjustmentReason("aaaaaaaaaa"));
        Assert.Throws<InvalidOperationException>(
            () => V180MileageGovernanceRules.RequireLeaderAdjustmentReason("batch approve"));
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
    public void Retry_candidates_require_current_route_failure_and_no_current_success()
    {
        var source = ReadRepositoryFile("backend/src/FieldVisit.Application/V180GoogleMileageContracts.cs");
        Assert.Contains("HasRetryEvidence", source);
        Assert.Contains("x.Status == \"Failed\"", source);
        Assert.Contains("!current.Any(x => x.Status == \"Succeeded\")", source);
        var job = ReadRepositoryFile("backend/src/FieldVisit.Infrastructure/BackgroundJobService.cs");
        Assert.Contains("FindEligibleMileageRetryIds", job);
        Assert.Contains("MILEAGE_BATCH_SELECTION_STALE_OR_UNAUTHORIZED", job);
        Assert.Contains("ResolveLiveMileageTeamIdsAsync", job);
        Assert.Contains("MILEAGE_JOB_LEADER_ROLE_REVOKED", job);
        Assert.Contains("IsolationLevel.Serializable", job);
        Assert.Contains("MILEAGE_JOB_POST_PROVIDER_STATE_STALE", job);
    }

    [Fact]
    public void Retry_evidence_is_current_route_only_and_any_current_success_blocks_it()
    {
        var snapshot = new VisitTripSnapshot
        {
            VisitTripSnapshotId = 8,
            VehicleTypeSnapshot = "Motorcycle",
            StartDeploymentSiteCodeSnapshot = "S1",
            StartDeploymentAddressSnapshot = "Start",
            EndDeploymentSiteCodeSnapshot = "S1",
            EndDeploymentAddressSnapshot = "End",
            Stops = [new VisitTripSnapshotStop { StopSequence = 1, AddressSnapshot = "Visited" }]
        };
        var hash = V180MileageCanonicalization.HashRoute(
            V180MileageCanonicalization.BuildSubmittedSnapshotBasis(snapshot));
        var failed = new RouteCalculationAttempt
        {
            VisitTripId = 10,
            Provider = "GoogleMapsRoutes-TestStub",
            BasisType = "SubmittedSnapshot",
            BasisVisitTripSnapshotId = snapshot.VisitTripSnapshotId,
            RequestedVehicleType = "Motorcycle",
            TravelMode = "TWO_WHEELER",
            RequestBasisHash = hash,
            Status = "Failed"
        };
        Assert.True(V180MileageCanonicalization.HasRetryEvidence(snapshot, [failed]));
        var currentSuccess = new RouteCalculationAttempt
        {
            VisitTripId = 10,
            Provider = "GoogleMapsRoutes-TestStub",
            BasisType = "SubmittedSnapshot",
            BasisVisitTripSnapshotId = snapshot.VisitTripSnapshotId,
            RequestedVehicleType = "Motorcycle",
            TravelMode = "TWO_WHEELER",
            RequestBasisHash = hash,
            Status = "Succeeded"
        };
        Assert.False(V180MileageCanonicalization.HasRetryEvidence(snapshot, [failed, currentSuccess]));
        var staleFailure = new RouteCalculationAttempt
        {
            VisitTripId = failed.VisitTripId,
            BasisType = failed.BasisType,
            BasisVisitTripSnapshotId = failed.BasisVisitTripSnapshotId,
            RequestedVehicleType = failed.RequestedVehicleType,
            TravelMode = failed.TravelMode,
            RequestBasisHash = new byte[32],
            Status = "Failed"
        };
        Assert.False(V180MileageCanonicalization.HasRetryEvidence(snapshot, [staleFailure]));
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
        Assert.Contains("item.AdjustmentReason", source);
        Assert.DoesNotContain("\"Batch approve\"", source);
        Assert.DoesNotContain("unchangedLegacyPendingApproval", source);
        Assert.Contains("RequireLeaderApprovalDecisionSource", source);
    }

    [Fact]
    public void Leader_adjustments_require_individual_specific_reason()
    {
        Assert.Equal("Travel time differs due to dispatch conditions.",
            V180MileageGovernanceRules.RequireLeaderAdjustmentReason(" Travel time differs due to dispatch conditions. "));
        Assert.Throws<InvalidOperationException>(() => V180MileageGovernanceRules.RequireLeaderAdjustmentReason(null));
        Assert.Throws<InvalidOperationException>(() => V180MileageGovernanceRules.RequireLeaderAdjustmentReason("Batch approve"));
        Assert.Throws<InvalidOperationException>(() => V180MileageGovernanceRules.RequireLeaderAdjustmentReason("adjusted"));
        Assert.Throws<InvalidOperationException>(() => V180MileageGovernanceRules.RequireLeaderAdjustmentReason(new string('x', 501)));
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
