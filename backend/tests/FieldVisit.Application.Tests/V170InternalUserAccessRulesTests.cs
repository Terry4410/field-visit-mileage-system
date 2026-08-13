using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170InternalUserAccessRulesTests
{
    private static readonly DateOnly Today =
        new(2026, 8, 13);

    [Fact]
    public void Supervisor_Is_Not_Allowed_For_Internal_User()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170InternalUserAccessRules.Normalize(
                    Request(
                        roles:
                            new[] { "supervisor" }),
                    Today));
    }

    [Fact]
    public void Leader_Requires_Team_Membership()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170InternalUserAccessRules.Normalize(
                    Request(
                        roles:
                            new[] { "leader" },
                        teams:
                            Array.Empty<
                                InternalTeamAssignmentInput>()),
                    Today));
    }

    [Fact]
    public void Team_Membership_Requires_Exactly_One_Primary()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170InternalUserAccessRules.Normalize(
                    Request(
                        teams:
                            new[]
                            {
                                new InternalTeamAssignmentInput(
                                    1,
                                    false),

                                new InternalTeamAssignmentInput(
                                    2,
                                    false)
                            }),
                    Today));
    }

    [Fact]
    public void Retroactive_Change_Requires_Confirmation()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170InternalUserAccessRules.Normalize(
                    Request(
                        effectiveFrom:
                            new DateOnly(
                                2026,
                                8,
                                12),
                        confirmRetroactive:
                            false),
                    Today));
    }

    [Fact]
    public void Valid_Request_Normalizes_Roles()
    {
        var result =
            V170InternalUserAccessRules.Normalize(
                Request(
                    roles:
                        new[]
                        {
                            " Visitor ",
                            "LEADER"
                        }),
                Today);

        Assert.Contains(
            "visitor",
            result.Roles);

        Assert.Contains(
            "leader",
            result.Roles);

        Assert.Single(
            result.TeamAssignments
                .Where(x => x.IsPrimary));
    }

    private static UpdateInternalUserAccessRequest
        Request(
            IReadOnlyList<string>? roles = null,
            IReadOnlyList<
                InternalTeamAssignmentInput>? teams = null,
            DateOnly? effectiveFrom = null,
            bool confirmRetroactive = false)
        => new(
            roles
                ?? new[] { "visitor" },

            teams
                ?? new[]
                {
                    new InternalTeamAssignmentInput(
                        1,
                        true)
                },

            true,

            effectiveFrom
                ?? Today,

            confirmRetroactive);
}
