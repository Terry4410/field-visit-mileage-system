using System.Text.Json;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Pure, fail-closed preflight for a future atomic B3 approve/apply executor.
/// This class does not authorize a reviewer, write SQL, geocode, or publish.
/// ApproveAsync remains DENY ALL until independently authorized.
/// </summary>
public static class V180B3ApprovalApplyPreflight
{
    public static V180B3LocationFields RequireValidatedProposal(
        V180B3ChangeRequest request, int organizationId, Guid publicId,
        byte[] currentLocationRowVersion, V180B3LocationFields current)
    {
        V180B3ReviewTargetRules.RequireSupportedTarget(
            request, organizationId, publicId);
        if (request.Status != "Pending"
            || request.ExpectedEntityRowVersion is not { Length: 8 }
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
            var before = JsonSerializer.Deserialize<V180B3LocationFields>(
                request.BeforeJson);
            var proposed = JsonSerializer.Deserialize<V180B3LocationFields>(
                request.ProposedJson);
            if (before is null || proposed is null
                || !JsonEquals(before, current))
                throw new InvalidOperationException("B3_APPROVAL_SOURCE_DRIFT");
            return V180B3ProposalSafetyRules.Validate(
                proposed, before, "Independent review preflight").Proposed;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("B3_APPROVAL_PAYLOAD_INVALID");
        }
    }

    private static bool JsonEquals(
        V180B3LocationFields a, V180B3LocationFields b) =>
        a == b;
}
