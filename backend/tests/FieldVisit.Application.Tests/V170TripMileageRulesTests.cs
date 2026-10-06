using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170TripMileageRulesTests
{
    [Fact]
    public void Minimum_visit_stop_count_is_one()
        => Assert.Equal(1, V170TripMileageRules.MinimumVisitStopCount);

    [Fact]
    public void Submission_requires_at_least_one_visit_stop()
    {
        var ex=Assert.Throws<InvalidOperationException>(()=>V170TripMileageRules.EnsureReadyForSubmission(0,null));
        Assert.Contains("至少需要 1 個拜訪地點",ex.Message);
    }

    [Fact]
    public void Submission_allows_one_visit_stop_without_manual_fallback_mileage()
        => V170TripMileageRules.EnsureReadyForSubmission(1,null);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Submission_rejects_nonpositive_manual_fallback_when_supplied(decimal value)
    {
        var ex=Assert.Throws<InvalidOperationException>(()=>V170TripMileageRules.EnsureReadyForSubmission(1,value));
        Assert.Contains("人工備援里程",ex.Message);
    }

    [Fact]
    public void Submission_allows_positive_manual_fallback()
        => V170TripMileageRules.EnsureReadyForSubmission(1,12.3m);

    [Fact]
    public void Approval_allows_one_visit_stop()
        => V170TripMileageRules.EnsureReadyForApproval(1);

    [Fact]
    public void Approval_rejects_zero_visit_stops()
        => Assert.Throws<InvalidOperationException>(()=>V170TripMileageRules.EnsureReadyForApproval(0));
}
