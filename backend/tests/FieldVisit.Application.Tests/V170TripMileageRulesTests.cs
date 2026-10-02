using FieldVisit.Application;

namespace FieldVisit.Application.Tests;

public sealed class V170TripMileageRulesTests
{
    [Fact]
    public void Submission_requires_at_least_two_stops()
    {
        var ex=Assert.Throws<InvalidOperationException>(()=>V170TripMileageRules.EnsureReadyForSubmission(1,null));
        Assert.Contains("至少需要 2 個公務地點",ex.Message);
    }

    [Fact]
    public void Submission_allows_missing_manual_fallback_mileage()
        => V170TripMileageRules.EnsureReadyForSubmission(2,null);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Submission_rejects_nonpositive_manual_fallback_when_supplied(decimal value)
    {
        var ex=Assert.Throws<InvalidOperationException>(()=>V170TripMileageRules.EnsureReadyForSubmission(2,value));
        Assert.Contains("人工備援里程",ex.Message);
    }

    [Fact]
    public void Submission_allows_positive_manual_fallback()
        => V170TripMileageRules.EnsureReadyForSubmission(2,12.3m);

    [Fact]
    public void Approval_requires_two_stops()
        => Assert.Throws<InvalidOperationException>(()=>V170TripMileageRules.EnsureReadyForApproval(1));
}
