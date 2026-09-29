using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180MileageApprovalGovernanceTests
{
    [Fact]
    public void Normal_approval_sources_are_explicit_and_claimed_is_not_company_evidence()
    {
        Assert.Equal("ProviderSuggested", V180MileageGovernanceRules.RequireDecisionSource("ProviderSuggested"));
        Assert.Equal("LeaderAdjusted", V180MileageGovernanceRules.RequireDecisionSource("LeaderAdjusted"));
        Assert.Equal("ManualFallback", V180MileageGovernanceRules.RequireDecisionSource("ManualFallback"));
        Assert.Throws<InvalidOperationException>(() => V180MileageGovernanceRules.RequireDecisionSource(null));
        Assert.Throws<InvalidOperationException>(() => V180MileageGovernanceRules.RequireDecisionSource("Claimed"));
    }

    [Fact]
    public void Correction_distance_decision_excludes_leader_adjusted()
    {
        Assert.Equal("ProviderSuggested", V180MileageGovernanceRules.RequireCorrectionDecisionSource("ProviderSuggested"));
        Assert.Equal("ManualFallback", V180MileageGovernanceRules.RequireCorrectionDecisionSource("ManualFallback"));
        Assert.Throws<InvalidOperationException>(() => V180MileageGovernanceRules.RequireCorrectionDecisionSource("LeaderAdjusted"));
        Assert.Equal("SubmittedSnapshot", V180MileageGovernanceRules.SubmittedSnapshotBasisCode);
        Assert.Equal("CorrectionProposal", V180MileageGovernanceRules.CorrectionProposalBasisCode);
    }
}
