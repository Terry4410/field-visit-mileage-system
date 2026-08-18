using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170TripTeamSelectionRulesTests
{
    private static CurrentUserDto User(
        int? primaryTeamId,
        params TeamScopeDto[] teams)
        => new(
            7,
            "visitor01",
            "王小明",
            null,
            2,
            primaryTeamId,
            teams.FirstOrDefault(x => x.TeamId == primaryTeamId)?.TeamName,
            ["visitor"],
            teams);

    [Fact]
    public void NoExplicitTeam_DefaultsToPrimaryTeam()
    {
        var user = User(
            9,
            new TeamScopeDto(9, "北區第一組", true),
            new TeamScopeDto(8, "南投就業中心", false));

        Assert.Equal(
            9,
            V170TripTeamSelectionRules.Resolve(
                user,
                null));
    }

    [Fact]
    public void ExplicitSecondaryTeam_IsAllowed()
    {
        var user = User(
            9,
            new TeamScopeDto(9, "北區第一組", true),
            new TeamScopeDto(8, "南投就業中心", false));

        Assert.Equal(
            8,
            V170TripTeamSelectionRules.Resolve(
                user,
                8));
    }

    [Fact]
    public void UnassignedTeam_IsRejected()
    {
        var user = User(
            9,
            new TeamScopeDto(9, "北區第一組", true),
            new TeamScopeDto(8, "南投就業中心", false));

        Assert.Throws<UnauthorizedAccessException>(
            () => V170TripTeamSelectionRules.Resolve(
                user,
                999));
    }

    [Fact]
    public void SubmissionRejectsTeamThatIsNoLongerAssigned()
    {
        var user = User(
            9,
            new TeamScopeDto(9, "北區第一組", true));

        Assert.Throws<UnauthorizedAccessException>(
            () => V170TripTeamSelectionRules.EnsureStillAllowed(
                user,
                8));
    }
}
