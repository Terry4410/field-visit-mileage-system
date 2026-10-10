using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;
public sealed class V180B2RiskRulesTests
{
    [Fact] public void Own_pending_draft_low_risk_saves_directly()
    {
        Assert.Equal(V180LocationChangeDecision.SavePendingDraft,
            V180B2LocationRiskRules.Classify(true,true,true,false,true,false,false,false,false));
    }
    [Fact] public void Published_address_and_activation_need_admin_review()
    {
        Assert.Equal(V180LocationChangeDecision.SubmitForAdminReview,
            V180B2LocationRiskRules.Classify(true,true,true,false,false,true,false,false,false));
        Assert.Equal(V180LocationChangeDecision.SubmitForAdminReview,
            V180B2LocationRiskRules.Classify(true,true,true,false,true,false,false,false,true));
    }
    [Fact] public void Foreign_and_unattested_team_grants_denied()
    {
        Assert.Equal(V180LocationChangeDecision.Deny,
            V180B2LocationRiskRules.Classify(false,true,true,false,true,false,false,false,false));
        Assert.Equal(V180LocationChangeDecision.Deny,
            V180B2LocationRiskRules.Classify(true,true,false,false,true,false,false,false,false));
        Assert.Equal(V180LocationChangeDecision.Deny,
            V180B2LocationRiskRules.Classify(true,false,true,false,false,true,false,false,false));
    }
}
