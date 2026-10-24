namespace FieldVisit.Infrastructure;

/// <summary>
/// Future versioned-publish preflight ONLY. No SQL, DI, endpoint, migration,
/// geocoder or write permission. All evidence must be reconstructed from
/// freshly locked server/database rows in ONE serializable transaction.
/// Never accept these flags or rowversions from a client request.
/// </summary>
public static class V180OptionCVersionedPublicationGate
{
    public sealed record LockedEvidence(
        Guid RequestPublicId,
        Guid StageRequestPublicId,
        Guid DecisionKey,
        int LocationId,
        int StagedLocationId,
        int OrganizationId,
        int StagedOrganizationId,
        int? TeamId,
        int? StagedTeamId,
        int RequestedByUserId,
        int ReviewedByUserId,
        string RequestStatus,
        string StageStatus,
        byte[]? ExpectedSourceRowVersion,
        byte[]? LockedSourceRowVersion,
        byte[]? ExpectedRequestRowVersion,
        byte[]? LockedRequestRowVersion,
        V180B3LocationFields Source,
        V180B3LocationFields Proposed,
        V180B3LocationFields Staged,
        string GeocodeStatus,
        string DuplicateReviewStatus,
        bool GeocodeMatchesStagedAddress,
        bool GeocodeCoordinatesValid,
        bool DuplicateEvidenceMatchesStagedVersion,
        bool HistoricalSnapshotsPreserved,
        bool LiveRequesterAuthorized,
        bool LiveIndependentAdminAuthorized,
        bool DecisionKeyUnused);

    public static void RequireReadyForAtomicSwap(LockedEvidence? e)
    {
        if (e is null
            || e.RequestPublicId == Guid.Empty
            || e.StageRequestPublicId != e.RequestPublicId
            || e.DecisionKey == Guid.Empty
            || e.LocationId <= 0 || e.StagedLocationId != e.LocationId
            || e.OrganizationId <= 0
            || e.StagedOrganizationId != e.OrganizationId
            || e.TeamId is null or <= 0 || e.StagedTeamId != e.TeamId
            || e.RequestedByUserId <= 0 || e.ReviewedByUserId <= 0
            || e.RequestedByUserId == e.ReviewedByUserId
            || e.RequestStatus != "Pending" || e.StageStatus != "ReadyForPublish"
            || !SameVersion(e.ExpectedSourceRowVersion, e.LockedSourceRowVersion)
            || !SameVersion(e.ExpectedRequestRowVersion, e.LockedRequestRowVersion)
            || e.Source is null || e.Proposed is null || e.Staged is null
            || e.Staged != e.Proposed
            || !e.LiveRequesterAuthorized || !e.LiveIndependentAdminAuthorized
            || !e.DecisionKeyUnused
            || !e.HistoricalSnapshotsPreserved
            || !e.DuplicateEvidenceMatchesStagedVersion
            || e.DuplicateReviewStatus != "Cleared")
            throw new InvalidOperationException("OPTIONC_VERSIONED_SWAP_NOT_READY");

        // A published identity/address is NEVER overwritten from a
        // geocoder callback or unreviewed Pending request. Geocoding must
        // correspond to this exact staged version, not an earlier address.
        var identityChanged =
            !string.Equals(e.Source.LocationName, e.Proposed.LocationName, StringComparison.Ordinal)
            || !string.Equals(e.Source.City, e.Proposed.City, StringComparison.Ordinal)
            || !string.Equals(e.Source.District, e.Proposed.District, StringComparison.Ordinal)
            || !string.Equals(e.Source.Address, e.Proposed.Address, StringComparison.Ordinal)
            || !string.Equals(e.Source.PlusCode, e.Proposed.PlusCode, StringComparison.Ordinal);
        if (identityChanged && (e.GeocodeStatus != "Succeeded"
            || !e.GeocodeMatchesStagedAddress || !e.GeocodeCoordinatesValid))
            throw new InvalidOperationException("OPTIONC_VERSIONED_GEOCODE_NOT_READY");
    }

    private static bool SameVersion(byte[]? expected, byte[]? actual) =>
        expected is { Length: 8 } && actual is { Length: 8 }
        && expected.SequenceEqual(actual);
}
