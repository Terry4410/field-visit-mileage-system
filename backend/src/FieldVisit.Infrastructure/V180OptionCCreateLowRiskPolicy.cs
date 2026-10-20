namespace FieldVisit.Infrastructure;

/// <summary>
/// Candidate-only operation/risk classification. This is not authorization,
/// approval evidence, or a publication writer. Callers must still enforce
/// live HR/team scope, independent Leader review, stage/ledger invariants and
/// isolated execution policy inside the publication transaction.
/// </summary>
public static class V180OptionCCreateLowRiskPolicy
{
    public const string Create = "CreateLocation";
    public const string Update = "UpdatePublishedLocation";
    public const string High = "High";
    public const string Low = "Low";

    // New addresses/identities are never automatically classified as Low.
    public static string ClassifyCreate(string? name, string? address,
        int? organizationId, int? teamId)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address)
            || organizationId is not > 0 || teamId is not > 0)
            throw new InvalidOperationException("OPTIONC_CREATE_IDENTITY_INCOMPLETE");
        return High;
    }

    public static string ClassifyPublishedUpdate(
        string? currentCity, string? currentDistrict, string? currentAddress,
        string? currentPlusCode, string? currentTaxId,
        string? proposedCity, string? proposedDistrict, string? proposedAddress,
        string? proposedPlusCode, string? proposedTaxId,
        bool published, bool changedAnyField)
    {
        if (!published || !changedAnyField
            || string.IsNullOrWhiteSpace(proposedAddress))
            throw new InvalidOperationException("OPTIONC_UPDATE_SOURCE_OR_CHANGE_INVALID");
        return V180OptionCLocationMaintenanceRisk.HasHighRiskChange(
            currentCity, currentDistrict, currentAddress, currentPlusCode, currentTaxId,
            proposedCity, proposedDistrict, proposedAddress, proposedPlusCode, proposedTaxId)
            ? High : Low;
    }

    /// <summary>
    /// Pure shape gate, NOT a replacement for transaction-bound authorization.
    /// Create requires a reserved, inactive Pending Location and its RowVersion.
    /// Updates require an existing Approved/Active published Location.
    /// </summary>
    public static void RequireSourceShape(string operation, string risk,
        bool sourceExists, bool isActive, string? approvalStatus,
        byte[]? expectedRowVersion, bool beforeJsonPresent)
    {
        if (expectedRowVersion is not { Length: 8 } || !sourceExists)
            throw new InvalidOperationException("OPTIONC_SOURCE_VERSION_REQUIRED");
        if (operation == Create)
        {
            if (risk != High || isActive || approvalStatus != "Pending"
                || beforeJsonPresent)
                throw new InvalidOperationException("OPTIONC_CREATE_RESERVATION_INVALID");
            return;
        }
        if (operation == Update)
        {
            if (risk is not (High or Low) || !isActive
                || approvalStatus != "Approved" || !beforeJsonPresent)
                throw new InvalidOperationException("OPTIONC_PUBLISHED_SOURCE_INVALID");
            return;
        }
        throw new InvalidOperationException("OPTIONC_OPERATION_NOT_SUPPORTED");
    }

    public static void RequireIndependentLeaderDecision(
        int requesterUserId, int reviewerUserId, bool leaderApproved)
    {
        if (requesterUserId <= 0 || reviewerUserId <= 0
            || requesterUserId == reviewerUserId || !leaderApproved)
            throw new InvalidOperationException("OPTIONC_LEADER_REVIEW_REQUIRED");
    }

    public static void RequireAdminForHighRisk(string risk, bool independentAdminApproved)
    {
        if (risk is not (High or Low) || (risk == High && !independentAdminApproved))
            throw new InvalidOperationException("OPTIONC_HIGH_RISK_ADMIN_REQUIRED");
    }
}
