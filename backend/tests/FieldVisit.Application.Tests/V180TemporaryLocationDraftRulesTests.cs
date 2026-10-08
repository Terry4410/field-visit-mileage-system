using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180TemporaryLocationDraftRulesTests
{
    private static CurrentUserDto Visitor(int userId = 100, int organizationId = 1) =>
        new(
            userId,
            "pilotv01",
            "Pilot Visitor",
            "pilotv01@example.com",
            organizationId,
            10,
            "北區第一組",
            new[] { "visitor" },
            new[] { new TeamScopeDto(10, "北區第一組", true) });

    [Fact]
    public void Current_trip_pending_temporary_location_can_be_reused()
    {
        var location = new Location
        {
            LocationId = 501,
            OrganizationId = 1,
            TeamId = 10,
            LocationName = "Pending temporary",
            IsTemporary = true,
            ApprovalStatus = "Pending",
            IsActive = false,
            CreatedByUserId = 100
        };

        Assert.True(
            V180TemporaryLocationDraftRules.CanReusePendingTemporaryLocation(
                location,
                "Temporary",
                Visitor(),
                10,
                new HashSet<int> { 501 }));
    }

    [Fact]
    public void Pending_temporary_location_from_another_trip_is_not_reusable()
    {
        var location = new Location
        {
            LocationId = 502,
            OrganizationId = 1,
            TeamId = 10,
            LocationName = "Other trip temporary",
            IsTemporary = true,
            ApprovalStatus = "Pending",
            IsActive = false,
            CreatedByUserId = 100
        };

        Assert.False(
            V180TemporaryLocationDraftRules.CanReusePendingTemporaryLocation(
                location,
                "Temporary",
                Visitor(),
                10,
                new HashSet<int> { 501 }));
    }

    [Fact]
    public void Pending_temporary_location_created_by_another_user_is_not_reusable()
    {
        var location = new Location
        {
            LocationId = 501,
            OrganizationId = 1,
            TeamId = 10,
            LocationName = "Other visitor temporary",
            IsTemporary = true,
            ApprovalStatus = "Pending",
            IsActive = false,
            CreatedByUserId = 999
        };

        Assert.False(
            V180TemporaryLocationDraftRules.CanReusePendingTemporaryLocation(
                location,
                "Temporary",
                Visitor(),
                10,
                new HashSet<int> { 501 }));
    }

    [Fact]
    public void Pending_temporary_reuse_requires_temporary_source_same_team_and_inactive_state()
    {
        var originalIds = new HashSet<int> { 501 };
        var location = new Location
        {
            LocationId = 501,
            OrganizationId = 1,
            TeamId = 10,
            LocationName = "Pending temporary",
            IsTemporary = true,
            ApprovalStatus = "Pending",
            IsActive = false,
            CreatedByUserId = 100
        };

        Assert.False(
            V180TemporaryLocationDraftRules.CanReusePendingTemporaryLocation(
                location,
                "Master",
                Visitor(),
                10,
                originalIds));

        Assert.False(
            V180TemporaryLocationDraftRules.CanReusePendingTemporaryLocation(
                location,
                "Temporary",
                Visitor(),
                11,
                originalIds));

        location.IsActive = true;
        Assert.False(
            V180TemporaryLocationDraftRules.CanReusePendingTemporaryLocation(
                location,
                "Temporary",
                Visitor(),
                10,
                originalIds));
    }

    [Fact]
    public void Persisted_temporary_stop_keeps_temporary_source_type()
    {
        var stop = new VisitTripStop
        {
            LocationId = 501,
            Location = new Location
            {
                LocationId = 501,
                IsTemporary = true,
                ApprovalStatus = "Pending"
            }
        };

        Assert.Equal(
            "Temporary",
            V180TemporaryLocationDraftRules.ResolveStopSourceType(stop));
    }

    [Fact]
    public void Removed_pending_temporary_locations_are_identified_for_abandonment()
    {
        var requested = new[]
        {
            new TripStopInput(
                502,
                null,
                null,
                "Temporary",
                "Kept temp",
                "Address",
                null,
                null)
        };

        var removed =
            V180TemporaryLocationDraftRules.RemovedPendingTemporaryLocationIds(
                new HashSet<int> { 501, 502 },
                requested);

        Assert.Equal(new[] { 501 }, removed);
    }
}
