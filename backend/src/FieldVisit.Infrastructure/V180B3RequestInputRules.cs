namespace FieldVisit.Infrastructure;

/// <summary>
/// Only structural, bounded untrusted-input checks. This does NOT establish
/// current HR/role/team/location authorization, which remains inside the
/// serializable B3 transaction. The service checks feature/schema readiness
/// before calling these helpers so disabled B3 always fails closed as 503.
/// </summary>
public static class V180B3RequestInputRules
{
    private static bool Oversize(string? value,int limit) =>
        value is not null && value.Length > limit;

    public static byte[] RequireSubmission(V180B3SubmitLocation? input)
    {
        if(input is null || input.LocationId<=0 || input.Proposed is null
            ||Oversize(input.Proposed.LocationName,200)
            ||Oversize(input.Proposed.City,100)
            ||Oversize(input.Proposed.District,100)
            ||Oversize(input.Proposed.Address,1000)
            ||Oversize(input.Proposed.PlusCode,100)
            ||Oversize(input.Proposed.TaxId,20)
            ||Oversize(input.Proposed.MasterNote,1000)
            ||Oversize(input.Reason,1000))
            throw new InvalidOperationException("B3_PROPOSAL_INVALID");
        return V180B3RowVersionRules.Parse(input.ExpectedRowVersion);
    }

    public static void RequireReviewTarget(Guid requestPublicId,V180B3Review? input)
    {
        if(requestPublicId==Guid.Empty || input is null
            ||input.DecisionKey==Guid.Empty
            ||Oversize(input.Reason,1000)
            ||string.IsNullOrWhiteSpace(input.Reason))
            throw new InvalidOperationException("B3_REASON_REQUIRED");
        // Canonical 12-character Base64, exactly eight bytes, checked prior
        // to entering a serializable SQL Server transaction.
        V180B3RowVersionRules.Parse(input.RequestRowVersion);
    }
}
