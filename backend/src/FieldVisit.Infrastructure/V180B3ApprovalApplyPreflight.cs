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
    byte[] ExpectedRequestRowVersion)
{
    // A plan is a preflight value object only. It is not an execution token.
    // Array properties are copies at construction, but consumers must not
    // treat the plan as trusted across transaction boundaries.
}

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
            || request.ExpectedEntityRowVersion is not { Length: 8 }
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
        if (currentLocationRowVersion is not { Length: 8 })
            throw new InvalidOperationException("B3_SOURCE_ROWVERSION_CONFLICT");
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

    /// <summary>
    /// Pure transaction-boundary guard for a future executor. A preflight plan
    /// is not authority: reject if either request or source changed after
    /// planning. Caller MUST still re-read both records under SQL locks,
    /// re-evaluate live authorization and all publication invariants.
    /// </summary>
    public static void RequirePlanVersionsStillCurrent(
        V180B3ApprovalApplyPlan plan, Guid requestPublicId,
        int locationId, byte[]? requestRowVersion,
        byte[]? locationRowVersion)
    {
        if (string.IsNullOrWhiteSpace(plan.BeforeJson)
            || string.IsNullOrWhiteSpace(plan.ProposedJson)
            || plan.BeforeJson.Length > 10000
            || plan.ProposedJson.Length > 10000)
            throw new InvalidOperationException("B3_APPROVAL_PLAN_INVALID");
        try
        {
            RequireExactLocationJson(plan.BeforeJson);
            RequireExactLocationJson(plan.ProposedJson);
            var before = JsonSerializer.Deserialize<V180B3LocationFields>(plan.BeforeJson);
            var proposed = JsonSerializer.Deserialize<V180B3LocationFields>(plan.ProposedJson);
            if (before is null || proposed is null
                || !JsonEquals(V180B3ProposalSafetyRules.Validate(
                    proposed, before, "Independent review preflight").Proposed, proposed))
                throw new InvalidOperationException("B3_APPROVAL_PLAN_INVALID");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("B3_APPROVAL_PLAN_INVALID");
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException("B3_APPROVAL_PLAN_INVALID");
        }
        if (plan.DecisionKey == Guid.Empty
            || plan.ReviewerUserId <= 0
            || string.IsNullOrWhiteSpace(plan.ReviewReason)
            || plan.ReviewReason.Length > 1000
            || plan.ReviewReason != plan.ReviewReason.Trim()
            || plan.ReviewReason.Any(ch => char.IsControl(ch)
                && ch != '\r' && ch != '\n' && ch != '\t'))
            throw new InvalidOperationException("B3_APPROVAL_PLAN_INVALID");
        if (requestPublicId == Guid.Empty
            || plan.RequestPublicId != requestPublicId
            || locationId <= 0 || plan.LocationId != locationId
            || plan.ExpectedRequestRowVersion is not { Length: 8 }
            || plan.ExpectedLocationRowVersion is not { Length: 8 }
            || requestRowVersion is not { Length: 8 }
            || locationRowVersion is not { Length: 8 }
            || !plan.ExpectedRequestRowVersion.SequenceEqual(requestRowVersion)
            || !plan.ExpectedLocationRowVersion.SequenceEqual(locationRowVersion))
            throw new InvalidOperationException("B3_APPROVAL_PLAN_STALE");
    }

    /// <summary>
    /// A preflight plan is not a trusted audit source. A future SQL executor
    /// must compare the plan against freshly locked source fields and the
    /// freshly loaded Pending request before any write.
    /// </summary>
    public static void RequirePlanSnapshotsStillCurrent(
        V180B3ApprovalApplyPlan plan,
        V180B3LocationFields lockedCurrentLocation,
        byte[]? lockedLocationRowVersion,
        V180B3ChangeRequest lockedRequest)
    {
        if (lockedRequest.RequestPublicId != plan.RequestPublicId
            || lockedRequest.OrganizationId <= 0
            || lockedRequest.TeamId is null or <= 0
            || lockedRequest.EntityKind != "Location"
            || lockedRequest.OperationCode != "UpdatePublishedLocation"
            || lockedRequest.RiskCode != "High"
            || lockedRequest.RequestedByUserId <= 0
            || lockedRequest.RequestedByUserId == plan.ReviewerUserId
            || !int.TryParse(lockedRequest.EntityId, out var requestLocationId)
            || requestLocationId != plan.LocationId
            || lockedRequest.Status != "Pending"
            || lockedRequest.ReviewedByUserId is not null
            || lockedRequest.ReviewedAt is not null
            || lockedRequest.AppliedAt is not null)
            throw new InvalidOperationException("B3_APPROVAL_PLAN_STALE");
        RequirePlanVersionsStillCurrent(
            plan, lockedRequest.RequestPublicId, requestLocationId,
            lockedRequest.RowVersion, lockedLocationRowVersion);
        if (lockedRequest.ExpectedEntityRowVersion is not { Length: 8 }
            || !lockedRequest.ExpectedEntityRowVersion.SequenceEqual(lockedLocationRowVersion!))
            throw new InvalidOperationException("B3_APPROVAL_PLAN_STALE");
        try
        {
            RequireExactLocationJson(plan.BeforeJson);
            RequireExactLocationJson(plan.ProposedJson);
            RequireExactLocationJson(lockedRequest.BeforeJson ?? "");
            RequireExactLocationJson(lockedRequest.ProposedJson);
            var before = JsonSerializer.Deserialize<V180B3LocationFields>(plan.BeforeJson);
            var proposed = JsonSerializer.Deserialize<V180B3LocationFields>(plan.ProposedJson);
            var requestBefore = JsonSerializer.Deserialize<V180B3LocationFields>(lockedRequest.BeforeJson);
            var requestProposed = JsonSerializer.Deserialize<V180B3LocationFields>(lockedRequest.ProposedJson);
            if (before is null || proposed is null || requestBefore is null
                || requestProposed is null || !JsonEquals(before, lockedCurrentLocation)
                || !JsonEquals(before, requestBefore)
                || !JsonEquals(proposed, requestProposed))
                throw new InvalidOperationException("B3_APPROVAL_PLAN_STALE");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("B3_APPROVAL_PLAN_STALE");
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException("B3_APPROVAL_PLAN_STALE");
        }
    }

    /// <summary>
    /// Must run against a freshly locked location row, not against the
    /// request's untrusted copied scope. No database writes are performed.
    /// </summary>
    public static void RequireLockedPublishedLocationScope(
        V180B3ApprovalApplyPlan plan, V180B3ChangeRequest request,
        int lockedLocationId, int? lockedOrganizationId, int? lockedTeamId,
        int? lockedCreatedByUserId, string? lockedLocationType,
        string? lockedApprovalStatus, bool lockedIsActive)
    {
        if (plan.RequestPublicId != request.RequestPublicId
            || plan.LocationId != lockedLocationId)
            throw new UnauthorizedAccessException("B3_APPROVAL_SOURCE_SCOPE_DENIED");
        RequirePublishedSourceEligibility(
            request, lockedLocationId, lockedOrganizationId, lockedTeamId,
            lockedCreatedByUserId, lockedLocationType, lockedApprovalStatus,
            lockedIsActive);
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
