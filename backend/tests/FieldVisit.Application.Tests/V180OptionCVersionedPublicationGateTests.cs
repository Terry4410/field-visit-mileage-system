using FieldVisit.Infrastructure;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OptionCVersionedPublicationGateTests
{
    private static readonly V180B3LocationFields Old =
        new("Old site", "Taipei", "Xinyi", "Old address", null, "12345678", "Old note");
    private static readonly V180B3LocationFields Next =
        Old with { Address = "New address" };
    private static V180OptionCVersionedPublicationGate.LockedEvidence Valid() => new(
        RequestPublicId: Guid.NewGuid(), StageRequestPublicId: Guid.Empty,
        DecisionKey: Guid.NewGuid(), LocationId: 42, StagedLocationId: 42,
        OrganizationId: 1, StagedOrganizationId: 1, TeamId: 10,
        StagedTeamId: 10, RequestedByUserId: 7, ReviewedByUserId: 8,
        RequestStatus: "Pending", StageStatus: "ReadyForPublish",
        ExpectedSourceRowVersion: [1,2,3,4,5,6,7,8],
        LockedSourceRowVersion: [1,2,3,4,5,6,7,8],
        ExpectedRequestRowVersion: [8,7,6,5,4,3,2,1],
        LockedRequestRowVersion: [8,7,6,5,4,3,2,1],
        Source: Old, Proposed: Next, Staged: Next,
        GeocodeStatus: "Succeeded", DuplicateReviewStatus: "Cleared",
        GeocodeMatchesStagedAddress: true, GeocodeCoordinatesValid: true,
        DuplicateEvidenceMatchesStagedVersion: true,
        HistoricalSnapshotsPreserved: true, LiveRequesterAuthorized: true,
        LiveIndependentAdminAuthorized: true, DecisionKeyUnused: true);
    private static V180OptionCVersionedPublicationGate.LockedEvidence Good()
    {
        var x = Valid();
        return x with { StageRequestPublicId = x.RequestPublicId };
    }

    [Fact]
    public void Fully_reviewed_staged_address_can_pass_pure_preflight() =>
        V180OptionCVersionedPublicationGate.RequireReadyForAtomicSwap(Good());

    [Fact]
    public void No_request_or_wrong_stage_cannot_publish()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCVersionedPublicationGate.RequireReadyForAtomicSwap(null));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCVersionedPublicationGate.RequireReadyForAtomicSwap(Valid()));
    }

    [Theory]
    [InlineData("self-review")]
    [InlineData("request-state")]
    [InlineData("stage-state")]
    [InlineData("stage-data")]
    [InlineData("org")]
    [InlineData("team")]
    [InlineData("location")]
    [InlineData("source-rowversion")]
    [InlineData("request-rowversion")]
    [InlineData("history")]
    [InlineData("requester-revoked")]
    [InlineData("admin-revoked")]
    [InlineData("decision-replay")]
    [InlineData("duplicate-review")]
    [InlineData("duplicate-evidence")]
    public void Unsafe_staging_or_approval_is_fail_closed(string condition)
    {
        var e = Good();
        e = condition switch
        {
            "self-review" => e with { ReviewedByUserId = 7 },
            "request-state" => e with { RequestStatus = "Applied" },
            "stage-state" => e with { StageStatus = "PendingGeocode" },
            "stage-data" => e with { Staged = Old },
            "org" => e with { StagedOrganizationId = 2 },
            "team" => e with { StagedTeamId = 11 },
            "location" => e with { StagedLocationId = 43 },
            "source-rowversion" => e with { LockedSourceRowVersion = new byte[8] },
            "request-rowversion" => e with { LockedRequestRowVersion = new byte[8] },
            "history" => e with { HistoricalSnapshotsPreserved = false },
            "requester-revoked" => e with { LiveRequesterAuthorized = false },
            "admin-revoked" => e with { LiveIndependentAdminAuthorized = false },
            "decision-replay" => e with { DecisionKeyUnused = false },
            "duplicate-review" => e with { DuplicateReviewStatus = "Pending" },
            "duplicate-evidence" => e with { DuplicateEvidenceMatchesStagedVersion = false },
            _ => throw new InvalidOperationException()
        };
        Assert.Equal("OPTIONC_VERSIONED_SWAP_NOT_READY",
            Assert.Throws<InvalidOperationException>(() =>
                V180OptionCVersionedPublicationGate.RequireReadyForAtomicSwap(e)).Message);
    }

    [Theory]
    [InlineData("geocode-pending")]
    [InlineData("geocode-stale")]
    [InlineData("coordinates-invalid")]
    public void Address_change_requires_fresh_matching_successful_geocode(string condition)
    {
        var e = Good();
        e = condition switch
        {
            "geocode-pending" => e with { GeocodeStatus = "Pending" },
            "geocode-stale" => e with { GeocodeMatchesStagedAddress = false },
            "coordinates-invalid" => e with { GeocodeCoordinatesValid = false },
            _ => throw new InvalidOperationException()
        };
        Assert.Equal("OPTIONC_VERSIONED_GEOCODE_NOT_READY",
            Assert.Throws<InvalidOperationException>(() =>
                V180OptionCVersionedPublicationGate.RequireReadyForAtomicSwap(e)).Message);
    }

    [Fact]
    public void Metadata_only_change_needs_no_address_geocode_but_still_independent_review()
    {
        var e = Good() with {
            Proposed = Old with { MasterNote = "Reviewed note" },
            Staged = Old with { MasterNote = "Reviewed note" },
            GeocodeStatus = "NotRequired",
            GeocodeMatchesStagedAddress = false,
            GeocodeCoordinatesValid = false
        };
        V180OptionCVersionedPublicationGate.RequireReadyForAtomicSwap(e);
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCVersionedPublicationGate.RequireReadyForAtomicSwap(
                e with { LiveIndependentAdminAuthorized = false }));
    }
}
