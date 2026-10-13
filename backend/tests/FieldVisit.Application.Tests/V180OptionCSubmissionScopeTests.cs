using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OptionCSubmissionScopeTests
{
    private static CurrentUserDto Actor(string role = "leader") =>
        new(7, "L007", "Actor", null, 1, 10, "Team", [role]);
    private static Location Published() => new()
    {
        LocationId = 19, OrganizationId = 1, TeamId = 10,
        CreatedByUserId = 7, LocationType = "Customer",
        ApprovalStatus = "Approved", IsActive = true
    };
    [Fact]
    public void Live_leader_can_propose_only_own_published_customer() =>
        V180OptionCSubmissionScope.RequireOwnPublishedCustomer(
            Actor(), Published(), false, false, true);
    [Fact]
    public void Live_visitor_and_current_team_can_propose() =>
        V180OptionCSubmissionScope.RequireOwnPublishedCustomer(
            Actor("visitor"), Published(), true, true, false);
    [Fact]
    public void Token_without_live_role_or_team_is_denied()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180OptionCSubmissionScope.RequireOwnPublishedCustomer(
                Actor(), Published(), false, false, false));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180OptionCSubmissionScope.RequireOwnPublishedCustomer(
                Actor("visitor"), Published(), true, false, false));
    }
    [Theory]
    [InlineData("owner")]
    [InlineData("team")]
    [InlineData("org")]
    [InlineData("type")]
    [InlineData("pending")]
    [InlineData("inactive")]
    public void Cross_scope_or_unpublished_location_is_denied(string changed)
    {
        var row = Published();
        switch (changed)
        {
            case "owner": row.CreatedByUserId = 8; break;
            case "team": row.TeamId = 11; break;
            case "org": row.OrganizationId = 2; break;
            case "type": row.LocationType = "Official"; break;
            case "pending": row.ApprovalStatus = "Pending"; break;
            case "inactive": row.IsActive = false; break;
        }
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180OptionCSubmissionScope.RequireOwnPublishedCustomer(
                Actor(), row, false, false, true));
    }
}
