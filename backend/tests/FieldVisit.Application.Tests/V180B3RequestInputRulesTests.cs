using FieldVisit.Infrastructure;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// B4 negative input preflight checks: pure, offline, no DB or feature enable.
/// </summary>
public sealed class V180B3RequestInputRulesTests
{
    private static readonly string ValidVersion=Convert.ToBase64String(new byte[8]);
    private static V180B3SubmitLocation Submit(string? version=null) =>
        new(123,version??ValidVersion,"Review reason",
            new V180B3LocationFields("Location","Taipei","Xinyi",
                "123 Main St",null,null,"Short note"));

    [Fact]
    public void Canonical_submission_is_bounded_and_returns_eight_byte_version()
        => Assert.Equal(new byte[8],
            V180B3RequestInputRules.RequireSubmission(Submit()));

    [Fact]
    public void Null_or_invalid_location_cannot_reach_location_queries()
    {
        Assert.Equal("B3_PROPOSAL_INVALID",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3RequestInputRules.RequireSubmission(null)).Message);
        Assert.Equal("B3_PROPOSAL_INVALID",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3RequestInputRules.RequireSubmission(Submit() with
                    {LocationId=0})).Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData("AAAAAAAAAAAA")]
    public void Noncanonical_source_version_is_rejected_without_DB(string token)
        => Assert.Equal("ROWVERSION_CONFLICT",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3RequestInputRules.RequireSubmission(
                    Submit() with {ExpectedRowVersion=token})).Message);

    [Theory]
    [InlineData("LocationName",200)]
    [InlineData("City",100)]
    [InlineData("District",100)]
    [InlineData("Address",1000)]
    [InlineData("PlusCode",100)]
    [InlineData("TaxId",20)]
    [InlineData("MasterNote",1000)]
    public void Every_client_field_has_an_exact_bounded_preflight_cap(
        string field,int limit)
    {
        var proposed=Submit().Proposed;
        var atLimit=new string('x',limit);
        var over=new string(' ',limit+1);
        V180B3LocationFields Edit(string value)=>field switch
        {
            "LocationName"=>proposed with {LocationName=value},
            "City"=>proposed with {City=value},
            "District"=>proposed with {District=value},
            "Address"=>proposed with {Address=value},
            "PlusCode"=>proposed with {PlusCode=value},
            "TaxId"=>proposed with {TaxId=value},
            "MasterNote"=>proposed with {MasterNote=value},
            _=>throw new InvalidOperationException("unknown test field")
        };
        Assert.Equal(8,V180B3RequestInputRules.RequireSubmission(
            Submit() with {Proposed=Edit(atLimit)}).Length);
        Assert.Equal("B3_PROPOSAL_INVALID",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3RequestInputRules.RequireSubmission(
                    Submit() with {Proposed=Edit(over)})).Message);
    }

    [Fact]
    public void Review_reason_boundary_is_enforced_without_entering_a_transaction()
    {
        var id=Guid.NewGuid();
        var key=Guid.NewGuid();
        V180B3RequestInputRules.RequireReviewTarget(
            id,new V180B3Review(ValidVersion,key,new string('x',1000)));
        Assert.Equal("B3_REASON_REQUIRED",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3RequestInputRules.RequireReviewTarget(
                    id,new V180B3Review(ValidVersion,key,new string('x',1001)))).Message);
    }

    [Fact]
    public void Massive_proposal_and_reason_are_denied_before_transaction()
    {
        var huge=new string(' ',2_000_000);
        Assert.Equal("B3_PROPOSAL_INVALID",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3RequestInputRules.RequireSubmission(Submit() with
                    {Proposed=Submit().Proposed with {MasterNote=huge}})).Message);
        Assert.Equal("B3_PROPOSAL_INVALID",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3RequestInputRules.RequireSubmission(Submit() with
                    {Reason=huge})).Message);
    }

    [Fact]
    public void Null_empty_key_and_huge_review_reason_cannot_start_decision_transaction()
    {
        var requestId=Guid.NewGuid();
        Assert.Equal("B3_REASON_REQUIRED",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3RequestInputRules.RequireReviewTarget(requestId,null)).Message);
        Assert.Equal("B3_REASON_REQUIRED",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3RequestInputRules.RequireReviewTarget(Guid.Empty,
                    new V180B3Review(ValidVersion,Guid.NewGuid(),"Reason"))).Message);
        Assert.Equal("B3_REASON_REQUIRED",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3RequestInputRules.RequireReviewTarget(requestId,
                    new V180B3Review(ValidVersion,Guid.Empty,"Reason"))).Message);
        Assert.Equal("B3_REASON_REQUIRED",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3RequestInputRules.RequireReviewTarget(requestId,
                    new V180B3Review(ValidVersion,Guid.NewGuid(),
                        new string(' ',2_000_000)))).Message);
    }

    [Fact]
    public void Review_version_is_rejected_before_decision_transaction()
    {
        Assert.Equal("ROWVERSION_CONFLICT",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3RequestInputRules.RequireReviewTarget(
                    Guid.NewGuid(),new V180B3Review("not-base64",
                        Guid.NewGuid(),"Reason"))).Message);
        V180B3RequestInputRules.RequireReviewTarget(Guid.NewGuid(),
            new V180B3Review(ValidVersion,Guid.NewGuid(),"Reason"));
    }
}
