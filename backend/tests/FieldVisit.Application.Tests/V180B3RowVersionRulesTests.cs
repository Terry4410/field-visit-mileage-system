using FieldVisit.Infrastructure;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>Offline B4 regression: hostile RowVersion strings are bounded before
/// Base64 decode, and submit/review share one canonical SQL token format.</summary>
public sealed class V180B3RowVersionRulesTests
{
    [Fact]
    public void Exact_eight_byte_tokens_round_trip_for_submit_and_review()
    {
        foreach(var version in new byte[][]{
            new byte[8],
            new byte[]{1,2,3,4,5,6,7,8},
            new byte[]{255,255,255,255,255,255,255,255}
        })
        {
            var encoded=Convert.ToBase64String(version);
            Assert.Equal(12,encoded.Length);
            Assert.Equal(version,V180B3RowVersionRules.Parse(encoded));
            Assert.Equal("OK",V180B3ProposalSafetyRules.RequireIndependentReview(
                1,2,"Pending",version,encoded,Guid.NewGuid(),"OK"));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("AAAAAAAAAA==")]
    [InlineData("AAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAA!")]
    [InlineData("AAAAAAAAAAA= ")]
    [InlineData("AAAAAAAAAA A=")]
    [InlineData("YWJjZA==")]
    public void Malformed_or_noncanonical_rowversion_is_rejected_before_use(string? encoded)
    {
        var ex=Assert.Throws<InvalidOperationException>(()=>
            V180B3RowVersionRules.Parse(encoded));
        Assert.Equal("ROWVERSION_CONFLICT",ex.Message);
    }

    [Fact]
    public void Multi_megabyte_attacker_input_is_denied_without_large_base64_decode()
    {
        var huge=new string('A',3_000_000);
        Assert.Equal("ROWVERSION_CONFLICT",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3RowVersionRules.Parse(huge)).Message);
        Assert.Equal("ROWVERSION_CONFLICT",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3ProposalSafetyRules.RequireIndependentReview(
                    1,2,"Pending",new byte[8],huge,Guid.NewGuid(),"Reason")).Message);
    }

    [Fact]
    public void Mismatched_correctly_encoded_token_does_not_review()
    {
        var encoded=Convert.ToBase64String(new byte[]{0,1,2,3,4,5,6,7});
        Assert.Equal("ROWVERSION_CONFLICT",
            Assert.Throws<InvalidOperationException>(()=>
                V180B3ProposalSafetyRules.RequireIndependentReview(
                    1,2,"Pending",new byte[8],encoded,Guid.NewGuid(),"Reason")).Message);
    }
}
