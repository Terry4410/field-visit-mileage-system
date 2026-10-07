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

    [Fact]
    public void Normalize_Rejects_Reversed_Hr_Date_Range()
    {
        Assert.Throws<InvalidOperationException>(
            () => V170PeopleQueryRules.Normalize(
                new V170PeopleQueryRequest(
                    HireFrom: new DateOnly(2026, 10, 8),
                    HireTo: new DateOnly(2026, 10, 7))));
    }

    [Fact]
    public void Normalize_Accepts_Historical_Status_And_Data_Issue()
    {
        var result = V170PeopleQueryRules.Normalize(
            new V170PeopleQueryRequest(
                HistoricalEmploymentStatus: " Leave ",
                EmploymentStatusFrom: new DateOnly(2026, 1, 1),
                EmploymentStatusTo: new DateOnly(2026, 6, 30),
                DataIssue: " MissingPrimaryDeploymentSite "));

        Assert.Equal("Leave", result.HistoricalEmploymentStatus);
        Assert.Equal(
            "MissingPrimaryDeploymentSite",
            result.DataIssue);
    }

    [Fact]
    public void Normalize_Rejects_Unknown_Data_Issue()
    {
        Assert.Throws<InvalidOperationException>(
            () => V170PeopleQueryRules.Normalize(
                new V170PeopleQueryRequest(
                    DataIssue: "MissingEverything")));
    }
}
