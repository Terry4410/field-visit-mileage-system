using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170LocationSearchRulesTests
{
    [Fact]
    public void Normalize_UsesDefaultPaging()
    {
        var result =
            V170LocationSearchRules.Normalize(
                new V170LocationSearchRequest(
                    null,
                    null,
                    null,
                    null,
                    0,
                    0));

        Assert.Equal(1, result.Page);
        Assert.Equal(
            V170LocationSearchRules.DefaultPageSize,
            result.PageSize);
    }

    [Fact]
    public void Normalize_TrimsSearchFilters()
    {
        var result =
            V170LocationSearchRules.Normalize(
                new V170LocationSearchRequest(
                    "  客戶A  ",
                    " 台北市 ",
                    " 內湖區 ",
                    null,
                    1,
                    20));

        Assert.Equal("客戶A", result.Query);
        Assert.Equal("台北市", result.City);
        Assert.Equal("內湖區", result.District);
    }

    [Fact]
    public void Normalize_ClampsPageSize()
    {
        var result =
            V170LocationSearchRules.Normalize(
                new V170LocationSearchRequest(
                    null,
                    null,
                    null,
                    null,
                    1,
                    999));

        Assert.Equal(
            V170LocationSearchRules.MaxPageSize,
            result.PageSize);
    }

    [Fact]
    public void Normalize_RejectsInvalidProjectId()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V170LocationSearchRules.Normalize(
                new V170LocationSearchRequest(
                    null,
                    null,
                    null,
                    0,
                    1,
                    20)));
    }

    [Fact]
    public void Normalize_RejectsOverlongQuery()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V170LocationSearchRules.Normalize(
                new V170LocationSearchRequest(
                    new string('A', 201),
                    null,
                    null,
                    null,
                    1,
                    20)));
    }

    [Theory]
    [InlineData("visitor")]
    [InlineData("leader")]
    [InlineData("admin")]
    public void EnsurePickerRole_AllowsInternalPickerRoles(
        string role)
    {
        V170LocationSearchRules.EnsurePickerRole(
            new[] { role });
    }

    [Fact]
    public void EnsurePickerRole_RejectsSupervisorOnly()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            V170LocationSearchRules.EnsurePickerRole(
                new[] { "supervisor" }));
    }
}
