using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180B3LocationTeamRulesTests
{
    private static readonly DateOnly Today = new(2026,10,10);

    [Fact]
    public void Current_enabled_same_organization_team_is_eligible()
    {
        Assert.True(V180B3LocationTeamRules.IsEffectiveForOrganization(
            1,1,true,Today.AddDays(-1),Today,Today));
        Assert.True(V180B3LocationTeamRules.IsEffectiveForOrganization(
            1,1,true,null,null,Today));
    }

    [Theory]
    [InlineData(1,2,true,-1,1)]
    [InlineData(1,1,false,-1,1)]
    [InlineData(1,1,true,1,2)]
    [InlineData(1,1,true,-2,-1)]
    [InlineData(null,1,true,-1,1)]
    [InlineData(1,null,true,-1,1)]
    public void Cross_org_disabled_future_expired_or_unscoped_teams_are_denied(
        int? actorOrg,int? teamOrg,bool enabled,int fromDays,int toDays)
    {
        Assert.False(V180B3LocationTeamRules.IsEffectiveForOrganization(
            actorOrg,teamOrg,enabled,Today.AddDays(fromDays),
            Today.AddDays(toDays),Today));
    }
}
