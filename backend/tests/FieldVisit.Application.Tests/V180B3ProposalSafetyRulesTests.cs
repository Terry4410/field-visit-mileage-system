using FieldVisit.Infrastructure;
using Xunit;

namespace FieldVisit.Application.Tests;
public sealed class V180B3ProposalSafetyRulesTests
{
    private static V180B3LocationFields Existing() =>
        new("Original","Taipei","Xinyi","Road 1",null,"12345678","old");

    [Fact] public void Normalized_change_stages_without_publishing()
    {
        var result=V180B3ProposalSafetyRules.Validate(
            Existing() with {LocationName=" New ",MasterNote=" memo "},Existing()," reason ");
        Assert.Equal("New",result.Proposed.LocationName);
        Assert.Equal("memo",result.Proposed.MasterNote);
        Assert.Equal("reason",result.Reason);
    }

    [Fact] public void No_op_and_trim_only_changes_are_denied()
    {
        Assert.Throws<InvalidOperationException>(()=>V180B3ProposalSafetyRules.Validate(
            Existing(),Existing(),"reason"));
        Assert.Throws<InvalidOperationException>(()=>V180B3ProposalSafetyRules.Validate(
            Existing() with {LocationName=" Original "},Existing(),"reason"));
    }

    [Fact] public void Invalid_and_unlocated_proposals_are_denied()
    {
        Assert.Throws<InvalidOperationException>(()=>V180B3ProposalSafetyRules.Validate(
            Existing() with {LocationName="Changed",Address=" ",PlusCode=null},Existing(),"reason"));
        Assert.Throws<InvalidOperationException>(()=>V180B3ProposalSafetyRules.Validate(
            Existing() with {LocationName=new string('X',201)},Existing(),"reason"));
        Assert.Throws<InvalidOperationException>(()=>V180B3ProposalSafetyRules.Validate(
            Existing() with {LocationName="Unsafe\u0000Name"},Existing(),"reason"));
        Assert.Throws<InvalidOperationException>(()=>V180B3ProposalSafetyRules.Validate(
            Existing() with {LocationName="Changed"},Existing(),"\u0001"));
        Assert.Throws<InvalidOperationException>(()=>V180B3ProposalSafetyRules.Validate(
            Existing() with {LocationName="Changed"},Existing(),"  "));
    }

    [Fact] public void Review_cannot_be_self_approved_replayed_or_stale()
    {
        var version=new byte[]{1,2,3,4,5,6,7,8};
        var encoded=Convert.ToBase64String(version);
        Assert.Equal("reason",V180B3ProposalSafetyRules.RequireIndependentReview(
            1,2,"Pending",version,encoded,Guid.NewGuid()," reason "));
        Assert.Throws<UnauthorizedAccessException>(()=>V180B3ProposalSafetyRules.RequireIndependentReview(
            1,1,"Pending",version,encoded,Guid.NewGuid(),"reason"));
        Assert.Throws<InvalidOperationException>(()=>V180B3ProposalSafetyRules.RequireIndependentReview(
            1,2,"Rejected",version,encoded,Guid.NewGuid(),"reason"));
        Assert.Throws<InvalidOperationException>(()=>V180B3ProposalSafetyRules.RequireIndependentReview(
            1,2,"Pending",version,"not-base64",Guid.NewGuid(),"reason"));
        Assert.Throws<InvalidOperationException>(()=>V180B3ProposalSafetyRules.RequireIndependentReview(
            1,2,"Pending",version,encoded,Guid.Empty,"reason"));
        Assert.Throws<InvalidOperationException>(()=>V180B3ProposalSafetyRules.RequireIndependentReview(
            1,2,"Pending",new byte[]{0,0,0,0,0,0,0,0},encoded,Guid.NewGuid(),"reason"));
    }
}
