using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170PeopleQueryRulesTests
{
    [Fact]
    public void Normalize_Defaults_Invalid_Page_And_PageSize()
    {
        var result =
            V170PeopleQueryRules.Normalize(
                new V170PeopleQueryRequest(
                    Page: -3,
                    PageSize: 999));

        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
    }

    [Fact]
    public void Normalize_Maps_Government_To_Supervisor()
    {
        var result =
            V170PeopleQueryRules.Normalize(
                new V170PeopleQueryRequest(
                    Role: "Government"));

        Assert.Equal(
            "supervisor",
            result.Role);
    }

    [Fact]
    public void Normalize_Trims_Keyword()
    {
        var result =
            V170PeopleQueryRules.Normalize(
                new V170PeopleQueryRequest(
                    Keyword: "  王小明  "));

        Assert.Equal(
            "王小明",
            result.Keyword);
    }

    [Fact]
    public void Normalize_Rejects_Invalid_UserType()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170PeopleQueryRules.Normalize(
                    new V170PeopleQueryRequest(
                        UserType: "Vendor")));
    }
}
