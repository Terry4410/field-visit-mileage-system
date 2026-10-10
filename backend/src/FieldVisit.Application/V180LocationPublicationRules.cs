namespace FieldVisit.Application;

/// <summary>
/// Geocoding is a coordinate calculation, never a review decision.
/// An already-approved location keeps its prior state, whereas a Pending
/// location must remain inactive until a separate authorized decision.
/// </summary>
public static class V180LocationPublicationRules
{
    public static void PreserveReviewStateAfterGeocoding(
        FieldVisit.Domain.Entities.Location location)
    {
        if (string.Equals(location.ApprovalStatus, "Pending",
            StringComparison.OrdinalIgnoreCase))
            location.IsActive = false;
        // Never set ApprovalStatus=Approved, IsActive=true, or add a
        // LocationApprovalHistory here. Only the independent review may do so.
    }
}

/// <summary>
/// No unverified inferred/legacy TeamLeaderAssignment may grant mutation
/// or a background job. This code-only candidate has no attested managed
/// team grant source yet. This must be replaced by a reviewed, effective-dated
/// and independently attested grant lookup after IT/Owner sign-off.
/// </summary>
public static class V180B1ManagerGrantProvenance
{
    public static void RequireVerifiedManagerGrant()
        => throw new UnauthorizedAccessException(
            "B1_MANAGER_GRANT_PROVENANCE_PENDING: 管理小組授權尚未由 Owner 核定。");
}
