namespace FieldVisit.Application;

/// <summary>
/// B2/B3 classification is not a permission grant. Caller must supply independently
/// verified, effective-dated team membership and attested management grants.
/// No UI/JWT team scope is accepted as manager evidence.
/// </summary>
public enum V180LocationChangeDecision { Deny, SavePendingDraft, SubmitForAdminReview }

public static class V180B2LocationRiskRules
{
    public static V180LocationChangeDecision Classify(
        bool sameOrganization, bool effectiveTeamMember,
        bool ownsLocation, bool verifiedManagerGrant,
        bool isPending, bool isActive,
        bool changesOwnerTeam, bool changesType, bool requestsActivation)
    {
        if (!sameOrganization || !effectiveTeamMember ||
            !(ownsLocation || verifiedManagerGrant))
            return V180LocationChangeDecision.Deny;
        if (changesOwnerTeam || changesType || requestsActivation ||
            !isPending || isActive)
            return V180LocationChangeDecision.SubmitForAdminReview;
        return V180LocationChangeDecision.SavePendingDraft;
    }
}
