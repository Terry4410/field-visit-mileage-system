using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OptionCLeaderDelegationRulesTests
{
    [Theory]
    [InlineData("2026-10-13", "2026-10-01", "2026-10-31", true)]
    [InlineData("2026-10-13", "2026-10-14", "2026-10-31", false)]
    [InlineData("2026-10-13", "2026-09-01", "2026-10-12", false)]
    public void Delegation_window_must_be_current(string date, string from, string to, bool expected)
    {
        Assert.Equal(expected, V180OptionCLeaderDelegationRules.IsActive(
            DateOnly.Parse(from), DateOnly.Parse(to), DateOnly.Parse(date)));
    }

    [Fact]
    public void Delegation_never_grants_unscoped_team_access()
    {
        Assert.False(V180OptionCLeaderDelegationRules.IsAuthorized(
            true, true, true, false, false, true));
        Assert.False(V180OptionCLeaderDelegationRules.IsAuthorized(
            true, true, true, true, false, true));
        Assert.False(V180OptionCLeaderDelegationRules.IsAuthorized(
            true, true, true, true, true, false));
        Assert.True(V180OptionCLeaderDelegationRules.IsAuthorized(
            true, true, true, true, true, true));
    }
}
