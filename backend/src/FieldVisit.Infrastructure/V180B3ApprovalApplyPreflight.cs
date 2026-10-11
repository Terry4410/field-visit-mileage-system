using System.Text.Json;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Pure, fail-closed preflight for a future atomic B3 approve/apply executor.
/// This class does not authorize a reviewer, write SQL, geocode, or publish.
/// ApproveAsync remains DENY ALL until independently authorized.
/// </summary>
public sealed record V180B3ApprovalApplyPlan(
    Guid RequestPublicId, int LocationId, int ReviewerUserId,
    Guid DecisionKey, string ReviewReason, string BeforeJson,
    string ProposedJson, byte[] ExpectedLocationRowVersion,
    byte[] ExpectedRequestRowVersion);

public static class V180B3ApprovalApplyPreflight
{
    public static V180B3LocationFields RequireValidatedProposal(
        V180B3ChangeRequest request, int organizationId, Guid publicId,
        byte[] currentLocationRowVersion, V180B3LocationFields current)
    {
        V180B3ReviewTargetRules.RequireSupportedTarget(
            request, organizationId, publicId);
        if (request.Status != "Pending"
            || currentLocationRowVersion is not { Length: 8 }
            || !request.ExpectedEntityRowVersion.SequenceEqual(currentLocationRowVersion))
            throw new InvalidOperationException("B3_SOURCE_ROWVERSION_CONFLICT");
        if (string.IsNullOrWhiteSpace(request.BeforeJson)
            || string.IsNullOrWhiteSpace(request.ProposedJson)
            || request.BeforeJson.Length > 10000
            || request.ProposedJson.Length > 10000)
            throw new InvalidOperationException("B3_APPROVAL_PAYLOAD_INVALID");
        try
        {
            RequireExactLocationJson(request.BeforeJson);
            RequireExactLocationJson(request.ProposedJson);
            var before = JsonSerializer.Deserialize<V180B3LocationFields>(
                request.BeforeJson);
            var proposed = JsonSerializer.Deserialize<V180B3LocationFields>(
                request.ProposedJson);
            if (before is null || proposed is null
                || !JsonEquals(before, current))
                throw new InvalidOperationException("B3_APPROVAL_SOURCE_DRIFT");
            var validated = V180B3ProposalSafetyRules.Validate(
                proposed, before, "Independent review preflight").Proposed;
            if (!JsonEquals(validated, proposed))
                throw new InvalidOperationException("B3_APPROVAL_PAYLOAD_NONCANONICAL");
            return validated;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("B3_APPROVAL_PAYLOAD_INVALID");
        }
    }

    /// <summary>
    /// Defensive check for SQL rowversion shape before constructing any
    /// approval plan, including data that may have come from a malformed
    /// isolated fixture rather than the real database.
    /// </summary>
    private static void RequireRequestRowVersion(V180B3ChangeRequest request)
    {
        if (request.RowVersion is not { Length: 8 })
            throw new InvalidOperationException("B3_REQUEST_ROWVERSION_INVALID");
    }

    /// <summary>
    /// Pure approval decision gate. The caller must still revalidate live Admin
    /// authorization inside a serializable SQL transaction and enforce unique
    /// DecisionKey at the database boundary.
    /// </summary>
    public static string RequireIndependentApprovalDecision(
        V180B3ChangeRequest request, int organizationId, Guid publicId,
        int reviewerUserId, V180B3Review review)
    {
        V180B3ReviewTargetRules.RequireSupportedTarget(
            request, organizationId, publicId);
        if (reviewerUserId <= 0)
            throw new UnauthorizedAccessException("B3_ADMIN_REQUIRED");
        V180B3RequestInputRules.RequireReviewTarget(publicId, review);
        RequireRequestRowVersion(request);
        return V180B3ProposalSafetyRules.RequireIndependentReview(
            request.RequestedByUserId, reviewerUserId,
            request.Status, request.RowVersion,
            review.RequestRowVersion, review.DecisionKey, review.Reason);
    }

    /// <summary>
    /// Rejects publication when the current source is not the same owned,
    /// approved and active Customer Location captured at submission.
    /// Caller must still re-check effective team membership and duplicate/
    /// geocoding/history rules inside the future SQL transaction.
    /// </summary>
    public static void RequirePublishedSourceEligibility(
        V180B3ChangeRequest request, int locationId,
        int? locationOrganizationId, int? locationTeamId,
        int? createdByUserId, string? locationType,
        string? approvalStatus, bool isActive)
    {
        if (!int.TryParse(request.EntityId, out var requestedLocationId)
            || requestedLocationId <= 0
            || requestedLocationId != locationId
            || request.OrganizationId <= 0
            || request.TeamId is null or <= 0
            || locationOrganizationId != request.OrganizationId
            || locationTeamId != request.TeamId
            || createdByUserId != request.RequestedByUserId
            || request.RequestedByUserId <= 0
            || locationType != "Customer"
            || approvalStatus != "Approved"
            || !isActive)
            throw new UnauthorizedAccessException(
                "B3_APPROVAL_SOURCE_SCOPE_DENIED");
    }

    /// <summary>
    /// Generates immutable, bounded approval evidence for a future executor.
    /// This is a pure value plan: no SQL write, status change or authorization.
    /// A transactional caller must persist both decision and apply events
    /// atomically with the actual location update.
    /// </summary>
    public static V180B3ApprovalApplyPlan PrepareImmutablePlan(
        V180B3ChangeRequest request, int organizationId, Guid publicId,
        int reviewerUserId, V180B3Review review,
        int locationId, int? locationOrganizationId,
        int? locationTeamId, int? createdByUserId,
        string? locationType, string? approvalStatus, bool isActive,
        byte[] currentLocationRowVersion, V180B3LocationFields current)
    {
        var reason = RequireIndependentApprovalDecision(
            request, organizationId, publicId, reviewerUserId, review);
        RequirePublishedSourceEligibility(request, locationId,
            locationOrganizationId, locationTeamId, createdByUserId,
            locationType, approvalStatus, isActive);
        var proposed = RequireValidatedProposal(
            request, organizationId, publicId, currentLocationRowVersion,
            current);
        return new V180B3ApprovalApplyPlan(
            request.RequestPublicId, locationId, reviewerUserId,
            review.DecisionKey, reason,
            JsonSerializer.Serialize(current),
            JsonSerializer.Serialize(proposed),
            currentLocationRowVersion.ToArray(),
            request.RowVersion.ToArray());
    }

    private static readonly HashSet<string> AllowedFields = new(StringComparer.Ordinal)
    {
        "LocationName", "City", "District", "Address",
        "PlusCode", "TaxId", "MasterNote"
    };

    private static void RequireExactLocationJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("B3_APPROVAL_PAYLOAD_INVALID");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!AllowedFields.Contains(property.Name)
                || !seen.Add(property.Name)
                || property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new InvalidOperationException("B3_APPROVAL_PAYLOAD_INVALID");
        }
        if (seen.Count != AllowedFields.Count)
            throw new InvalidOperationException("B3_APPROVAL_PAYLOAD_INVALID");
    }

    private static bool JsonEquals(
        V180B3LocationFields a, V180B3LocationFields b) =>
        a == b;
}
