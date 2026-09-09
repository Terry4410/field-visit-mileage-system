using FieldVisit.Api.Controllers;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OrganizationPeopleFoundationTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 9);

    [Fact]
    public void Effective_period_boundaries_are_inclusive_and_null_end_is_open()
    {
        Assert.True(V180AsOfRules.IsEffective(AsOf, AsOf, AsOf));
        Assert.True(V180AsOfRules.IsEffective(AsOf, null, AsOf));
        Assert.True(V180AsOfRules.IsEffective(AsOf.AddDays(-1), AsOf, AsOf));
        Assert.False(V180AsOfRules.IsEffective(AsOf.AddDays(1), null, AsOf));
        Assert.False(V180AsOfRules.IsEffective(AsOf.AddDays(-2), AsOf.AddDays(-1), AsOf));
    }

    [Fact]
    public void Status_excludes_future_and_inactive_periods()
    {
        var rows = new[]
        {
            Status(1, AsOf.AddDays(-5), AsOf.AddDays(-1)),
            Status(2, AsOf.AddDays(1), null)
        };
        Assert.Null(V180AsOfRules.EmploymentStatus(rows, AsOf));
    }

    [Fact]
    public void Ambiguous_current_status_fails_closed()
    {
        var rows = new[] { Status(1, AsOf.AddDays(-1), null), Status(2, AsOf, null) };
        Assert.Throws<InvalidOperationException>(() => V180AsOfRules.EmploymentStatus(rows, AsOf));
    }

    [Fact]
    public void Exactly_one_primary_team_is_required()
    {
        var one = Membership(1, 10, true);
        Assert.Same(one, V180AsOfRules.PrimaryTeam([one, Membership(2, 11, false)], AsOf));
        Assert.Throws<InvalidOperationException>(() => V180AsOfRules.PrimaryTeam(
            [one, Membership(3, 12, true)], AsOf));
    }

    [Fact]
    public void Duplicate_current_membership_for_same_team_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => V180AsOfRules.TeamMemberships(
            [Membership(1, 10, false), Membership(2, 10, false)], AsOf));
    }

    [Fact]
    public void Roles_are_resolved_at_inclusive_boundaries_and_duplicate_role_fails_closed()
    {
        var current = new EmploymentRoleAssignment { EmploymentRoleAssignmentId = 1,
            EmploymentId = 1, RoleId = 2, EffectiveFrom = AsOf, EffectiveTo = AsOf };
        var future = new EmploymentRoleAssignment { EmploymentRoleAssignmentId = 2,
            EmploymentId = 1, RoleId = 3, EffectiveFrom = AsOf.AddDays(1) };
        Assert.Same(current, Assert.Single(V180AsOfRules.EmploymentRoles([current, future], AsOf)));
        Assert.Throws<InvalidOperationException>(() => V180AsOfRules.EmploymentRoles(
            [current, new EmploymentRoleAssignment { EmploymentRoleAssignmentId = 3,
                EmploymentId = 1, RoleId = 2, EffectiveFrom = AsOf }], AsOf));
    }

    [Fact]
    public void Team_center_and_leader_are_resolved_as_of_date()
    {
        var center = new TeamCenterAssignment { TeamCenterAssignmentId = 1, TeamId = 10,
            CenterId = 20, EffectiveFrom = AsOf, EffectiveTo = AsOf };
        Assert.Same(center, V180AsOfRules.TeamCenter([center], AsOf));

        var leader = new TeamLeaderAssignment { TeamLeaderAssignmentId = 1, TeamId = 10,
            EmploymentId = 30, EffectiveFrom = AsOf, EffectiveTo = null };
        Assert.Single(V180AsOfRules.TeamLeaders([leader], AsOf));
    }

    [Fact]
    public void Delegation_boundaries_are_inclusive_and_ambiguity_fails_closed()
    {
        var delegation = new TeamLeaderDelegation { TeamLeaderDelegationId = 1,
            TeamLeaderAssignmentId = 10, DelegateEmploymentId = 20,
            EffectiveFrom = AsOf, EffectiveTo = AsOf };
        Assert.Same(delegation, V180AsOfRules.DelegatedLeader([delegation], AsOf));
        Assert.Throws<InvalidOperationException>(() => V180AsOfRules.DelegatedLeader(
            [delegation, new TeamLeaderDelegation { TeamLeaderDelegationId = 2,
                TeamLeaderAssignmentId = 10, DelegateEmploymentId = 21,
                EffectiveFrom = AsOf, EffectiveTo = AsOf }], AsOf));
    }

    [Fact]
    public void Identity_resolution_uses_stable_keys_and_never_name_or_email()
    {
        var people = new[]
        {
            new Person { PersonId = 1, LegacyUserId = 41, DisplayName = "Same Name" },
            new Person { PersonId = 2, LegacyUserId = 42, DisplayName = "Same Name" }
        };
        var employments = new[]
        {
            new Employment { EmploymentId = 11, PersonId = 1, OrganizationId = 5,
                EmployeeNo = "E001", Email = "shared@example.test", LegacyUserId = 41 },
            new Employment { EmploymentId = 12, PersonId = 2, OrganizationId = 5,
                EmployeeNo = "E002", Email = "shared@example.test", LegacyUserId = 42 }
        };
        Assert.Equal(1, V180IdentityRules.ResolvePersonId(new(LegacyUserId: 41), people, employments));
        Assert.Equal(2, V180IdentityRules.ResolvePersonId(new(OrganizationId: 5, EmployeeNo: "E002"), people, employments));
        Assert.Throws<InvalidOperationException>(() =>
            V180IdentityRules.ResolvePersonId(new(), people, employments));
    }

    [Fact]
    public void Every_v180_rowversion_is_a_concurrency_token()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var types = new[] { typeof(Organization), typeof(Team), typeof(Center), typeof(TeamCenterAssignment),
            typeof(Person), typeof(Employment), typeof(EmploymentStatusPeriod), typeof(EmploymentRoleAssignment),
            typeof(TeamMembership), typeof(TeamLeaderAssignment), typeof(TeamLeaderDelegation) };
        foreach (var type in types)
        {
            var property = db.Model.FindEntityType(type)!.FindProperty("RowVersion");
            Assert.NotNull(property);
            Assert.True(property!.IsConcurrencyToken, type.Name);
        }
    }

    [Fact]
    public void V170_route_contract_remains_unchanged_and_v180_is_additive()
    {
        var v170 = Assert.Single(typeof(V170PeopleAdminController).GetCustomAttributes(
            typeof(RouteAttribute), false).Cast<RouteAttribute>());
        var v180 = Assert.Single(typeof(V180OrganizationPeopleAdminController).GetCustomAttributes(
            typeof(RouteAttribute), false).Cast<RouteAttribute>());
        Assert.Equal("api/v1/admin/people", v170.Template);
        Assert.Equal("api/v1/admin/v180", v180.Template);
        Assert.NotNull(typeof(V170PeopleAdminController).GetMethod("Query"));
        Assert.NotNull(typeof(V170PeopleAdminController).GetMethod("Get"));
    }

    private static EmploymentStatusPeriod Status(long id, DateOnly from, DateOnly? to) => new()
    { EmploymentStatusPeriodId = id, EmploymentId = 1, EmploymentStatus = "Active",
        EffectiveFrom = from, EffectiveTo = to };

    private static TeamMembership Membership(long id, int teamId, bool primary) => new()
    { TeamMembershipId = id, EmploymentId = 1, TeamId = teamId, IsPrimary = primary,
        EffectiveFrom = AsOf.AddDays(-1), EffectiveTo = null };
}
