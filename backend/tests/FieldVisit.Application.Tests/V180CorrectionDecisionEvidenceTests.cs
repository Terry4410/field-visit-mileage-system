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

    private static CorrectionProposal Proposal(string stopAddress) => new(
        new DateOnly(2026, 9, 29),
        new TimeOnly(9, 0),
        new TimeOnly(10, 0),
        "note",
        10m,
        12m,
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
}
