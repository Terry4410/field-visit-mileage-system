using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170TemporaryLocationCleanupRulesTests
{
    [Fact]
    public void ReturnsOnlyPendingTemporaryLocations()
    {
        var trip = new VisitTrip
        {
            Stops =
            [
                new VisitTripStop
                {
                    Location = new Location
                    {
                        LocationId = 101,
                        IsTemporary = true,
                        ApprovalStatus = "Pending"
                    }
                },
                new VisitTripStop
                {
                    Location = new Location
                    {
                        LocationId = 102,
                        IsTemporary = true,
                        ApprovalStatus = "Approved"
                    }
                },
                new VisitTripStop
                {
                    Location = new Location
                    {
                        LocationId = 103,
                        IsTemporary = false,
                        ApprovalStatus = "Pending"
                    }
                }
            ]
        };

        var result =
            V170TemporaryLocationCleanupRules
                .GetPendingTemporaryLocationIds(trip);

        Assert.Single(result);
        Assert.Equal(101, result[0]);
    }

    [Fact]
    public void RemovesDuplicateLocationIds()
    {
        var location = new Location
        {
            LocationId = 201,
            IsTemporary = true,
            ApprovalStatus = "Pending"
        };

        var trip = new VisitTrip
        {
            Stops =
            [
                new VisitTripStop { Location = location },
                new VisitTripStop { Location = location }
            ]
        };

        var result =
            V170TemporaryLocationCleanupRules
                .GetPendingTemporaryLocationIds(trip);

        Assert.Single(result);
        Assert.Equal(201, result[0]);
    }

    [Fact]
    public void EmptyTripReturnsEmptyList()
    {
        var trip = new VisitTrip();

        var result =
            V170TemporaryLocationCleanupRules
                .GetPendingTemporaryLocationIds(trip);

        Assert.Empty(result);
    }
}
