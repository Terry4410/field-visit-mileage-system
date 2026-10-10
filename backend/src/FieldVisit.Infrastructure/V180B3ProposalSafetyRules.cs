namespace FieldVisit.Infrastructure;

/// <summary>Validates staged requests only; does not grant approval or manager rights.</summary>
public static class V180B3ProposalSafetyRules
{
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static bool ForbiddenControl(string? value,bool multiline=false) =>
        value is not null && value.Any(c=>char.IsControl(c)
            && !(multiline && (c=='\r'||c=='\n'||c=='\t')));
    private static V180B3LocationFields Normalize(V180B3LocationFields value) =>
        new(Clean(value.LocationName)??"",Clean(value.City),Clean(value.District),
            Clean(value.Address),Clean(value.PlusCode),Clean(value.TaxId),Clean(value.MasterNote));

    public static (V180B3LocationFields Proposed,string Reason) Validate(
        V180B3LocationFields? proposed,V180B3LocationFields before,string? reason)
    {
        if(proposed is null)throw new InvalidOperationException("B3_PROPOSAL_INVALID");
        var next=Normalize(proposed);
        var why=Clean(reason);
        if(string.IsNullOrWhiteSpace(next.LocationName)
            ||next.LocationName.Length>200
            ||(next.City?.Length??0)>100||(next.District?.Length??0)>100
            ||(next.Address?.Length??0)>1000||(next.PlusCode?.Length??0)>100
            ||(next.TaxId?.Length??0)>20||(next.MasterNote?.Length??0)>1000
            ||(next.Address is null&&next.PlusCode is null)
            ||string.IsNullOrWhiteSpace(why)||why.Length>1000
            ||ForbiddenControl(next.LocationName)||ForbiddenControl(next.City)
            ||ForbiddenControl(next.District)||ForbiddenControl(next.Address)
            ||ForbiddenControl(next.PlusCode)||ForbiddenControl(next.TaxId)
            ||ForbiddenControl(next.MasterNote,true)||ForbiddenControl(why,true))
            throw new InvalidOperationException("B3_PROPOSAL_INVALID");
        if(next==Normalize(before))
            throw new InvalidOperationException("B3_NO_CHANGE");
        return(next,why);
    }

    public static string RequireIndependentReview(
        int submitterId,int reviewerId,string status,byte[] actualRowVersion,
        string? submittedRowVersion,Guid decisionKey,string? reason)
    {
        if(submitterId==reviewerId)
            throw new UnauthorizedAccessException("B3_SELF_REVIEW_DENIED");
        var why=Clean(reason);
        if(decisionKey==Guid.Empty||string.IsNullOrWhiteSpace(why)
            ||why.Length>1000||ForbiddenControl(why,true))
            throw new InvalidOperationException("B3_REASON_REQUIRED");
        byte[] supplied;
        try{supplied=Convert.FromBase64String(submittedRowVersion??"");}
        catch(FormatException){throw new InvalidOperationException("ROWVERSION_CONFLICT");}
        if(supplied.Length!=8||actualRowVersion is null
            ||!actualRowVersion.SequenceEqual(supplied)||status!="Pending")
            throw new InvalidOperationException("ROWVERSION_CONFLICT");
        return why;
    }
}
