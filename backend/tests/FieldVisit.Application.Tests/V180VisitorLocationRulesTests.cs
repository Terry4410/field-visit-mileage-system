using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180VisitorLocationRulesTests
{
    private static CurrentUserDto Visitor(int id = 7) =>
        new(id, $"E{id}", "Visitor", null, 1, 10, "Team", ["visitor"]);

    private static Location Draft(int owner = 7) => new()
    {
        OrganizationId = 1, TeamId = 10, CreatedByUserId = owner,
        LocationType = "Customer", ApprovalStatus = "Pending", IsActive = false
    };

    [Fact]
    public void Create_requires_customer_in_effective_team_and_inactive_state()
    {
        V180VisitorLocationRules.EnsureCreate(Visitor(), 10, "Customer", false, [10]);
        Assert.Throws<UnauthorizedAccessException>(() => V180VisitorLocationRules.EnsureCreate(Visitor(), 11, "Customer", false, [10]));
        Assert.Throws<UnauthorizedAccessException>(() => V180VisitorLocationRules.EnsureCreate(Visitor(), 10, "Official", false, [10]));
        Assert.Throws<UnauthorizedAccessException>(() => V180VisitorLocationRules.EnsureCreate(Visitor(), 10, "Customer", true, [10]));
    }

    [Fact]
    public void Update_requires_owner_pending_inactive_same_team()
    {
        V180VisitorLocationRules.EnsureUpdate(Visitor(), Draft(), 10, "Customer", false, [10]);
        Assert.Throws<UnauthorizedAccessException>(() => V180VisitorLocationRules.EnsureUpdate(Visitor(), Draft(8), 10, "Customer", false, [10]));
        var approved = Draft(); approved.ApprovalStatus = "Approved";
        Assert.Throws<UnauthorizedAccessException>(() => V180VisitorLocationRules.EnsureUpdate(Visitor(), approved, 10, "Customer", false, [10]));
        Assert.Throws<UnauthorizedAccessException>(() => V180VisitorLocationRules.EnsureUpdate(Visitor(), Draft(), 11, "Customer", false, [10]));
    }
    [Fact]
    public void Visitor_update_requires_exact_nonempty_rowversion()
    {
        byte[] current = [0, 0, 0, 0, 0, 0, 0, 5];
        V180VisitorLocationRules.EnsureRequiredRowVersion(current, Convert.ToBase64String(current));
        foreach (string? token in new string?[]
        {
            null, "", "invalid-base64!", Convert.ToBase64String([0, 0, 0, 0, 0, 0, 0, 4])
        })
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                V180VisitorLocationRules.EnsureRequiredRowVersion(current, token));
            Assert.Contains("ROWVERSION_CONFLICT", error.Message);
        }
    }

    [Theory]
    [InlineData(false, "Active", 1, 1)]
    [InlineData(true, "Leave", 1, 1)]
    [InlineData(true, "Terminated", 1, 1)]
    [InlineData(true, "Active", 0, 1)]
    [InlineData(true, "Active", 1, null)]
    public void Visitor_denies_missing_current_role_hr_team_or_organization(
        bool currentRole, string status, int teamCount, int? organizationId)
    {
        int[] teams = teamCount == 0 ? [] : [10];
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180VisitorLocationRules.EnsureCurrentAssignment(currentRole, status, teams, organizationId));
    }

    [Fact]
    public void Visitor_accepts_only_current_active_grants_and_own_draft()
    {
        V180VisitorLocationRules.EnsureCurrentAssignment(true, "Active", [10], 1);
        V180VisitorLocationRules.EnsureUpdate(Visitor(), Draft(), 10, "Customer", false, [10]);
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180VisitorLocationRules.EnsureUpdate(Visitor(), Draft(8), 10, "Customer", false, [10]));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180VisitorLocationRules.EnsureCreate(Visitor(), 20, "Customer", false, [10]));
        var crossOrganization = Draft(); crossOrganization.OrganizationId = 2;
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180VisitorLocationRules.EnsureUpdate(Visitor(), crossOrganization, 10, "Customer", false, [10]));
    }

}
