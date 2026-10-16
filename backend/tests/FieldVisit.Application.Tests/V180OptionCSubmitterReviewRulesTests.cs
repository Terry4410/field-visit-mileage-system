using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OptionCSubmitterReviewRulesTests
{
    [Fact]
    public void Active_visitor_or_dated_leader_may_remain_eligible()
    {
        Assert.True(V180OptionCSubmitterReviewRules.IsEligible(
            true,true,true,true,true,true,false,false));
        Assert.True(V180OptionCSubmitterReviewRules.IsEligible(
            true,true,true,true,false,false,true,true));
    }

    [Theory]
    [InlineData(false,true,true,true,false,false,true,true)]
    [InlineData(true,false,true,true,false,false,true,true)]
    [InlineData(true,true,false,true,false,false,true,true)]
    [InlineData(true,true,true,false,false,false,true,true)]
    [InlineData(true,true,true,true,false,false,true,false)]
    [InlineData(true,true,true,true,false,false,false,true)]
    [InlineData(true,true,true,true,true,false,false,false)]
    public void Missing_any_required_gate_denies_review_apply(
        bool hr,bool org,bool team,bool assignment,
        bool visitorRole,bool visitorMembership,
        bool leaderRole,bool appointment)
    {
        Assert.False(V180OptionCSubmitterReviewRules.IsEligible(
            hr,org,team,assignment,visitorRole,visitorMembership,
            leaderRole,appointment));
    }
}
