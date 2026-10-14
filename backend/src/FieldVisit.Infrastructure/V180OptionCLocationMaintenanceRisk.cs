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

    private static bool Changed(string? current, string? proposed) =>
        !string.Equals(current, proposed, StringComparison.Ordinal);
}
