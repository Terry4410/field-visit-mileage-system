namespace FieldVisit.Infrastructure;

/// <summary>Field-level policy for direct edits to an already published Location.</summary>
public static class V180OptionCLocationMaintenanceRisk
{
    public static bool HasHighRiskChange(
        string? currentCity, string? currentDistrict, string? currentAddress,
        string? currentPlusCode, string? currentTaxId,
        string? proposedCity, string? proposedDistrict, string? proposedAddress,
        string? proposedPlusCode, string? proposedTaxId) =>
        Changed(currentCity, proposedCity)
        || Changed(currentDistrict, proposedDistrict)
        || Changed(currentAddress, proposedAddress)
        || Changed(currentPlusCode, proposedPlusCode)
        || Changed(currentTaxId, proposedTaxId);

    public static void RequireApprovalForPublishedHighRiskChange(
        bool isPublished, bool hasHighRiskChange)
    {
        if (isPublished && hasHighRiskChange)
            throw new InvalidOperationException(
                "LOCATION_HIGH_RISK_CHANGE_REQUIRES_OPTIONC_APPROVAL");
    }

    /// <summary>
    /// Legacy maintenance has read-scope based authorization and therefore
    /// cannot safely apply a published note. Frozen Scheme A policy permits
    /// direct LowRisk save only through the candidate transaction that proves
    /// live write scope and atomically appends request/version/audit evidence.
    /// </summary>
    public static void RequireAuthorizedCandidateForPublishedLowRiskChange(
        bool isPublished, bool hasLowRiskChange)
    {
        if (isPublished && hasLowRiskChange)
            throw new InvalidOperationException(
                "LOCATION_LOW_RISK_CHANGE_REQUIRES_AUTHORIZED_DIRECT_SAVE");
    }

    private static bool Changed(string? current, string? proposed) =>
        !string.Equals(current, proposed, StringComparison.Ordinal);
}
