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
    /// Legacy maintenance must not directly apply even a note-only change to
    /// an approved Location. Scheme A requires the canonical request,
    /// independent Leader first-level review, duplicate evidence and atomic
    /// versioned publication for LowRisk as well as HighRisk.
    /// </summary>
    public static void RequireCanonicalForPublishedLowRiskChange(
        bool isPublished, bool hasLowRiskChange)
    {
        if (isPublished && hasLowRiskChange)
            throw new InvalidOperationException(
                "LOCATION_LOW_RISK_CHANGE_REQUIRES_OPTIONC_LEADER_REVIEW");
    }

    private static bool Changed(string? current, string? proposed) =>
        !string.Equals(current, proposed, StringComparison.Ordinal);
}
