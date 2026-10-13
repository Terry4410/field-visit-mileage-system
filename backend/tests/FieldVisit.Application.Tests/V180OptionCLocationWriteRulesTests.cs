using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OptionCLocationWriteRulesTests
{
    private static CurrentUserDto Leader(int id = 7) =>
        new(id, $"E{id}", "Leader", null, 1, 10, "Team", ["leader"]);

    private static CurrentUserDto Admin(int id = 8) =>
        new(id, $"E{id}", "Admin", null, 1, 10, "Team", ["admin"]);

    private static Location Draft(int owner = 7) => new()
    {
        OrganizationId = 1, TeamId = 10, CreatedByUserId = owner,
        LocationType = "Customer", ApprovalStatus = "Pending", IsActive = false
    };

    [Fact]
    public void Published_and_active_locations_are_never_directly_mutable()
    {
        var approved = Draft(); approved.ApprovalStatus = "Approved";
        var active = Draft(); active.IsActive = true;
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCLocationWriteRules.EnsureDirectDraftUpdate(approved, Admin(), 10, "Customer", false));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCLocationWriteRules.EnsureDirectDraftUpdate(active, Leader(), 10, "Customer", false));
        Assert.Throws<InvalidOperationException>(() =>
            V180OptionCLocationWriteRules.EnsureUnpublishedDraft(approved));
    }

    [Fact]
    public void Leader_can_only_change_own_pending_customer_draft_with_same_team()
    {
        V180OptionCLocationWriteRules.EnsureDirectDraftUpdate(Draft(), Leader(), 10, "Customer", false);
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180OptionCLocationWriteRules.EnsureDirectDraftUpdate(Draft(9), Leader(), 10, "Customer", false));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180OptionCLocationWriteRules.EnsureDirectDraftUpdate(Draft(), Leader(), 11, "Customer", false));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180OptionCLocationWriteRules.EnsureDirectDraftUpdate(Draft(), Leader(), 10, "Official", false));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180OptionCLocationWriteRules.EnsureDirectDraftUpdate(Draft(), Leader(), 10, "Customer", true));
    }

    [Fact]
    public void Leader_create_requires_customer_draft_with_valid_team()
    {
        V180OptionCLocationWriteRules.EnsureLeaderDraftCreate(Leader(), 10, "Customer", false);
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180OptionCLocationWriteRules.EnsureLeaderDraftCreate(Leader(), null, "Customer", false));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180OptionCLocationWriteRules.EnsureLeaderDraftCreate(Leader(), 11, "Customer", false));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180OptionCLocationWriteRules.EnsureLeaderDraftCreate(Leader(), 10, "Official", false));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180OptionCLocationWriteRules.EnsureLeaderDraftCreate(Leader(), 10, "Customer", true));
    }
}
