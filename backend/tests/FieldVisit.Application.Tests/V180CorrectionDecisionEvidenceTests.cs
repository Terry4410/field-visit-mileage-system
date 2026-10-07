using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180CorrectionDecisionEvidenceTests
{
    private static VisitTripSnapshot BaseSnapshot() => new()
    {
        VehicleTypeSnapshot = "Car",
        StartDeploymentSiteCodeSnapshot = "START",
        StartDeploymentAddressSnapshot = "Start address",
        EndDeploymentSiteCodeSnapshot = "END",
        EndDeploymentAddressSnapshot = "End address"
    };

    private static CorrectionProposal Proposal(
        string stopAddress,
        decimal approvedDistanceKm = 12m) => new(
        new DateOnly(2026, 9, 29),
        new TimeOnly(9, 0),
        new TimeOnly(10, 0),
        "note",
        10m,
        approvedDistanceKm,
        5m,
        60m,
        [new CorrectionStopProposal(1, "L1", "Stop", stopAddress, null, null, null, null, null, null)]);

    [Fact]
    public void Final_correction_proposal_hash_is_deterministic_and_uses_final_route_basis()
    {
        var first = V180MileageCanonicalization.HashCorrectionProposal(BaseSnapshot(), Proposal("Final address"));
        var second = V180MileageCanonicalization.HashCorrectionProposal(BaseSnapshot(), Proposal("Final address"));
        var changed = V180MileageCanonicalization.HashCorrectionProposal(BaseSnapshot(), Proposal("Changed address"));
        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void One_stop_correction_proposal_is_a_valid_route_basis()
    {
        var basis = V180MileageCanonicalization.BuildCorrectionProposalBasis(
            BaseSnapshot(),
            Proposal("Only visit stop"));
        Assert.Single(basis.Stops);
        Assert.Equal(1, basis.Stops[0].StopSequence);
        Assert.Equal("Only visit stop", basis.Stops[0].AddressSnapshot);
    }

    [Fact]
    public void Correction_hash_is_not_submitted_snapshot_hash_when_final_stops_change()
    {
        var snapshot = BaseSnapshot();
        snapshot.Stops = [new VisitTripSnapshotStop { StopSequence = 1, AddressSnapshot = "Old address" }];
        var submitted = V180MileageCanonicalization.HashRoute(V180MileageCanonicalization.BuildSubmittedSnapshotBasis(snapshot));
        var corrected = V180MileageCanonicalization.HashCorrectionProposal(snapshot, Proposal("Final address"));
        Assert.NotEqual(submitted, corrected);
    }

    [Fact]
    public void Same_route_with_changed_approved_distance_is_domain_separated_from_submitted_hash()
    {
        var snapshot = BaseSnapshot();
        snapshot.Stops = [new VisitTripSnapshotStop { StopSequence = 1, AddressSnapshot = "Final address" }];
        var submitted = V180MileageCanonicalization.HashRoute(V180MileageCanonicalization.BuildSubmittedSnapshotBasis(snapshot));
        var corrected = V180MileageCanonicalization.HashCorrectionProposal(snapshot, Proposal("Final address", 12m));
        Assert.NotEqual(submitted, corrected);
    }

    [Fact]
    public void Different_final_approved_distance_values_produce_different_hashes()
    {
        var snapshot = BaseSnapshot();
        var first = V180MileageCanonicalization.HashCorrectionProposal(snapshot, Proposal("Final address", 12m));
        var second = V180MileageCanonicalization.HashCorrectionProposal(snapshot, Proposal("Final address", 13m));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void No_correction_route_attempt_resolves_to_manual_fallback()
    {
        Assert.Equal("ManualFallback", V180MileageGovernanceRules.ResolveCorrectionDecisionSource(null, false));
        Assert.Equal("ManualFallback", V180MileageGovernanceRules.ResolveCorrectionDecisionSource("ManualFallback", false));
        Assert.Throws<InvalidOperationException>(() => V180MileageGovernanceRules.ResolveCorrectionDecisionSource("ProviderSuggested", false));
    }

    [Fact]
    public void Existing_correction_route_attempt_allows_only_provider_suggested()
    {
        Assert.Equal("ProviderSuggested", V180MileageGovernanceRules.ResolveCorrectionDecisionSource(null, true));
        Assert.Equal("ProviderSuggested", V180MileageGovernanceRules.ResolveCorrectionDecisionSource("ProviderSuggested", true));
        Assert.Throws<InvalidOperationException>(() => V180MileageGovernanceRules.ResolveCorrectionDecisionSource("ManualFallback", true));

        var source = ReadRepositoryFile("backend/src/FieldVisit.Infrastructure/V160FinalRepository.cs");
        Assert.Contains("selectedAttempt.CalculationReason != \"CorrectionRecalculate\"", source);
        Assert.Contains("!selectedAttempt.RequestBasisHash.SequenceEqual(approvalBasisHash)", source);
        Assert.Contains("selectedAttempt.RequestedVehicleType != expectedVehicle", source);
        Assert.Contains("selectedAttempt.TravelMode != expectedTravelMode", source);
        Assert.DoesNotContain("new RouteCalculationAttempt", source);
    }

    [Fact]
    public void One_stop_correction_is_not_downgraded_to_not_applicable()
    {
        var source = ReadRepositoryFile("backend/src/FieldVisit.Infrastructure/V160FinalRepository.cs");
        Assert.Contains("request.Proposal.Stops.Count >= V170TripMileageRules.MinimumVisitStopCount", source);
        Assert.Contains("stops.Count < V170TripMileageRules.MinimumVisitStopCount", source);
        Assert.DoesNotContain("One-stop corrections remain NotApplicable", source);
        Assert.DoesNotContain("request.Proposal.Stops.Count >= 2", source);
    }

    [Fact]
    public void Route_affecting_stop_changes_are_forced_through_admin_close()
    {
        var source = ReadRepositoryFile("backend/src/FieldVisit.Infrastructure/V160FinalRepository.FinalGap.cs");
        Assert.Contains("RequiresAdminClose(List<CorrectionRequestChange> changes)", source);
        Assert.Contains("StopsAffectRoute(changes)", source);
        Assert.Contains("x.StopSequence", source);
        Assert.Contains("Normalize(x.Address)", source);
    }

    [Fact]
    public void Correction_recalculation_fails_closed_when_google_distance_differs()
    {
        var source = ReadRepositoryFile("backend/src/FieldVisit.Infrastructure/V180CorrectionMileageService.cs");
        Assert.Contains("\"CorrectionRecalculate\"", source);
        Assert.Contains("HashCorrectionProposal(baseSnapshot, proposal)", source);
        Assert.Contains("V170TripMileageRules.MinimumVisitStopCount", source);
        Assert.Contains("CORRECTION_DISTANCE_MISMATCH", source);
        Assert.Contains("DistanceToleranceKm", source);
        Assert.Contains("PROVIDER_CANCELLED", source);
    }

    [Fact]
    public void Correction_close_rejects_manual_fallback_after_any_usable_google_result()
    {
        var source = ReadRepositoryFile("backend/src/FieldVisit.Infrastructure/V180CorrectionClosureService.cs");
        Assert.Contains("CORRECTION_GOOGLE_SUCCESS_EXISTS", source);
        Assert.Contains("CORRECTION_DISTANCE_MISMATCH", source);
        Assert.Contains("CORRECTION_GOOGLE_FAILURE_REQUIRED", source);
        Assert.Contains("CORRECTION_MANUAL_FALLBACK_REQUIRED", source);
        Assert.Contains("snapshot.SystemDistanceKmSnapshot", source);
        Assert.Contains("V180MileageGovernanceRules.CorrectionProposalBasisCode", source);
    }

    [Fact]
    public void Distance_changed_correction_keeps_admin_close_as_final_decision_actor_and_time()
    {
        var source = ReadRepositoryFile("backend/src/FieldVisit.Infrastructure/V160FinalRepository.cs");
        Assert.Contains("approverUserId = row.AdminClosedByUserId", source);
        Assert.Contains("distanceApprovedAt = row.AdminClosedAt", source);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return File.ReadAllText(Path.Combine(current.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            current = current.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
