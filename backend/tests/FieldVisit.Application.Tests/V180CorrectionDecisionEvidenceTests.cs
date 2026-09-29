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
    public void Correction_hash_is_not_submitted_snapshot_hash_when_final_stops_change()
    {
        var snapshot = BaseSnapshot();
        snapshot.Stops = [new VisitTripSnapshotStop { StopSequence = 1, AddressSnapshot = "Old address" }];

        var submitted = V180MileageCanonicalization.HashRoute(
            V180MileageCanonicalization.BuildSubmittedSnapshotBasis(snapshot));
        var corrected = V180MileageCanonicalization.HashCorrectionProposal(snapshot, Proposal("Final address"));

        Assert.NotEqual(submitted, corrected);
    }

    [Fact]
    public void Same_route_with_changed_approved_distance_is_domain_separated_from_submitted_hash()
    {
        var snapshot = BaseSnapshot();
        snapshot.Stops =
        [
            new VisitTripSnapshotStop
            {
                StopSequence = 1,
                AddressSnapshot = "Final address"
            }
        ];

        var submitted = V180MileageCanonicalization.HashRoute(
            V180MileageCanonicalization.BuildSubmittedSnapshotBasis(snapshot));
        var corrected = V180MileageCanonicalization.HashCorrectionProposal(
            snapshot,
            Proposal("Final address", 12m));

        Assert.NotEqual(submitted, corrected);
    }

    [Fact]
    public void Different_final_approved_distance_values_produce_different_hashes()
    {
        var snapshot = BaseSnapshot();
        var first = V180MileageCanonicalization.HashCorrectionProposal(
            snapshot,
            Proposal("Final address", 12m));
        var second = V180MileageCanonicalization.HashCorrectionProposal(
            snapshot,
            Proposal("Final address", 13m));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void No_correction_route_attempt_resolves_to_manual_fallback()
    {
        Assert.Equal(
            "ManualFallback",
            V180MileageGovernanceRules.ResolveCorrectionDecisionSource(
                null,
                hasRouteCalculationAttempt: false));

        Assert.Equal(
            "ManualFallback",
            V180MileageGovernanceRules.ResolveCorrectionDecisionSource(
                "ManualFallback",
                hasRouteCalculationAttempt: false));

        Assert.Throws<InvalidOperationException>(
            () => V180MileageGovernanceRules.ResolveCorrectionDecisionSource(
                "ProviderSuggested",
                hasRouteCalculationAttempt: false));
    }

    [Fact]
    public void Existing_correction_route_attempt_allows_only_provider_suggested()
    {
        Assert.Equal(
            "ProviderSuggested",
            V180MileageGovernanceRules.ResolveCorrectionDecisionSource(
                null,
                hasRouteCalculationAttempt: true));

        Assert.Equal(
            "ProviderSuggested",
            V180MileageGovernanceRules.ResolveCorrectionDecisionSource(
                "ProviderSuggested",
                hasRouteCalculationAttempt: true));

        Assert.Throws<InvalidOperationException>(
            () => V180MileageGovernanceRules.ResolveCorrectionDecisionSource(
                "ManualFallback",
                hasRouteCalculationAttempt: true));

        var source = ReadRepositoryFile(
            "backend/src/FieldVisit.Infrastructure/V160FinalRepository.cs");
        Assert.Contains(
            "selectedAttempt.CalculationReason != \"CorrectionRecalculate\"",
            source);
        Assert.Contains(
            "!selectedAttempt.RequestBasisHash.SequenceEqual(approvalBasisHash)",
            source);
        Assert.Contains(
            "selectedAttempt.RequestedVehicleType != expectedVehicle",
            source);
        Assert.Contains(
            "selectedAttempt.TravelMode != expectedTravelMode",
            source);
        Assert.DoesNotContain("new RouteCalculationAttempt", source);
    }

    [Fact]
    public void Distance_changed_correction_keeps_admin_close_as_final_decision_actor_and_time()
    {
        var source = ReadRepositoryFile(
            "backend/src/FieldVisit.Infrastructure/V160FinalRepository.cs");

        Assert.Contains("approverUserId = row.AdminClosedByUserId", source);
        Assert.Contains("distanceApprovedAt = row.AdminClosedAt", source);
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
