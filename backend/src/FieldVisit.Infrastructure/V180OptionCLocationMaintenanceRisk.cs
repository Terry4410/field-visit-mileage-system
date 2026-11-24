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

    // First fill is permitted only through the isolated atomic writer;
    // this does not change the legacy high-risk classifier.
    public static string RequireFirstTaiwanTaxId(string? current, string? proposed)
    {
        if (!string.IsNullOrWhiteSpace(current))
            throw new InvalidOperationException("LOCATION_TAXID_ALREADY_SET_REQUIRES_REVIEW");
        var value = proposed?.Trim();
        if (value is null || value.Length != 8 || value.Any(c => c < '0' || c > '9')
            || value == "00000000")
            throw new InvalidOperationException("LOCATION_TAXID_INVALID");
        int[] weights = [1,2,1,2,1,2,4,1];
        var sum = 0;
        for (var i = 0; i < 8; i++)
        {
            var product = (value[i] - '0') * weights[i];
            sum += product / 10 + product % 10;
        }
        if (sum % 5 != 0 && !(value[6] == '7' && (sum + 1) % 5 == 0))
            throw new InvalidOperationException("LOCATION_TAXID_INVALID");
        return value;
    }

    private static bool Changed(string? current, string? proposed) =>
        !string.Equals(current, proposed, StringComparison.Ordinal);
}
