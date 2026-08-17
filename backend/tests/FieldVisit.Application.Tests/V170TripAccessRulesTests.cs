using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170TripAccessRulesTests
{
    private static CurrentUserDto Supervisor(
        int? organizationId = 2)
        => new(
            UserId: 10,
            EmployeeNo: "gov01",
            DisplayName: "督導人員",
            Email: "gov01@example.com",
            OrganizationId: organizationId,
            TeamId: null,
            TeamName: null,
            Roles: new[] { "supervisor" });

    [Fact]
    public void OrganizationScope_AllowsAnyTeamInSameOrganization()
    {
        var allowed =
            V170TripAccessRules.CanSupervisorReadTrip(
                Supervisor(),
                tripOrganizationId: 2,
                tripTeamId: 999,
                new V170ReadScope(
                    OrganizationWide: true,
                    TeamIds: Array.Empty<int>()));

        Assert.True(allowed);
    }

    [Fact]
    public void TeamScope_AllowsAuthorizedTeam()
    {
        var allowed =
            V170TripAccessRules.CanSupervisorReadTrip(
                Supervisor(),
                tripOrganizationId: 2,
                tripTeamId: 4,
                new V170ReadScope(
                    OrganizationWide: false,
                    TeamIds: new[] { 4, 5 }));

        Assert.True(allowed);
    }

    [Fact]
    public void TeamScope_DeniesUnauthorizedTeam()
    {
        var allowed =
            V170TripAccessRules.CanSupervisorReadTrip(
                Supervisor(),
                tripOrganizationId: 2,
                tripTeamId: 6,
                new V170ReadScope(
                    OrganizationWide: false,
                    TeamIds: new[] { 4, 5 }));

        Assert.False(allowed);
    }

    [Fact]
    public void TeamScope_DeniesTripWithoutTeam()
    {
        var allowed =
            V170TripAccessRules.CanSupervisorReadTrip(
                Supervisor(),
                tripOrganizationId: 2,
                tripTeamId: null,
                new V170ReadScope(
                    OrganizationWide: false,
                    TeamIds: new[] { 4 }));

        Assert.False(allowed);
    }

    [Fact]
    public void DeniesOtherOrganization_EvenWithOrganizationWideScope()
    {
        var allowed =
            V170TripAccessRules.CanSupervisorReadTrip(
                Supervisor(),
                tripOrganizationId: 3,
                tripTeamId: 4,
                new V170ReadScope(
                    OrganizationWide: true,
                    TeamIds: Array.Empty<int>()));

        Assert.False(allowed);
    }

    [Fact]
    public void DeniesSupervisorWithoutOrganization()
    {
        var allowed =
            V170TripAccessRules.CanSupervisorReadTrip(
                Supervisor(organizationId: null),
                tripOrganizationId: 2,
                tripTeamId: 4,
                new V170ReadScope(
                    OrganizationWide: false,
                    TeamIds: new[] { 4 }));

        Assert.False(allowed);
    }
}
