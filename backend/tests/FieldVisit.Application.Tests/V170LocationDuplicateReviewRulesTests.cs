using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170LocationDuplicateReviewRulesTests
{
    [Fact]
    public void Normalize_Defaults_To_Pending()
    {
        var result = V170LocationDuplicateReviewRules.Normalize(
            new V170LocationDuplicateReviewRequest(
                Status: "",
                Page: 0,
                PageSize: 999));

        Assert.Equal("Pending", result.Status);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public void Normalize_Uses_Canonical_Status_And_Trims_Search()
    {
        var result = V170LocationDuplicateReviewRules.Normalize(
            new V170LocationDuplicateReviewRequest(
                Status: " useexisting ",
                Q: "  ABC 公司  "));

        Assert.Equal("UseExisting", result.Status);
        Assert.Equal("ABC 公司", result.Q);
    }

    [Fact]
    public void Normalize_Rejects_Unknown_Status()
    {
        Assert.Throws<InvalidOperationException>(
            () => V170LocationDuplicateReviewRules.Normalize(
                new V170LocationDuplicateReviewRequest(
                    Status: "Ignored")));
    }
}
