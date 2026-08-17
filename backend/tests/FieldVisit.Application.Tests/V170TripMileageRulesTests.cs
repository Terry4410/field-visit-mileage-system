using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170TripMileageRulesTests
{
    [Fact]
    public void Submission_rejects_less_than_two_stops()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => V170TripMileageRules.EnsureReadyForSubmission(1, null));

        Assert.Equal(V170TripMileageRules.MinimumStopsMessage, ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Submission_rejects_non_positive_claimed_mileage(double km)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => V170TripMileageRules.EnsureReadyForSubmission(2, (decimal)km));

        Assert.Equal(V170TripMileageRules.ClaimedMileageMessage, ex.Message);
    }

    [Fact]
    public void Submission_rejects_missing_claimed_mileage()
    {
        Assert.Throws<InvalidOperationException>(
            () => V170TripMileageRules.EnsureReadyForSubmission(2, null));
    }

    [Fact]
    public void Submission_allows_two_stops_with_positive_claimed_mileage()
    {
        V170TripMileageRules.EnsureReadyForSubmission(2, 12.3m);
    }

    [Fact]
    public void Approval_rejects_historical_single_stop_trip()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => V170TripMileageRules.EnsureReadyForApproval(1));

        Assert.Equal(V170TripMileageRules.ApprovalMinimumStopsMessage, ex.Message);
    }

    [Fact]
    public void Approval_allows_two_or_more_stops()
    {
        V170TripMileageRules.EnsureReadyForApproval(2);
        V170TripMileageRules.EnsureReadyForApproval(3);
    }
}
