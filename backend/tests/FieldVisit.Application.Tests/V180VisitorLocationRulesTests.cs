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
}
