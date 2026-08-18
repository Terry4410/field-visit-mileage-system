using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V160CorrectionFinancialRulesTests
{
    [Fact]
    public void SameVisitDate_PreservesFrozenSnapshotRate()
    {
        Assert.True(
            V160CorrectionFinancialRules.ShouldPreserveSnapshotRate(
                new DateOnly(2026, 8, 18),
                new DateOnly(2026, 8, 18)));

        Assert.Equal(
            3.00m,
            V160CorrectionFinancialRules.RequireSnapshotRate(3.00m));
    }

    [Fact]
    public void ChangedVisitDate_ReevaluatesRate()
    {
        Assert.False(
            V160CorrectionFinancialRules.ShouldPreserveSnapshotRate(
                new DateOnly(2026, 8, 18),
                new DateOnly(2026, 8, 19)));
    }

    [Fact]
    public void CorrectedMileage_UsesFrozenRateForAmount()
    {
        Assert.Equal(
            57.00m,
            V160CorrectionFinancialRules.CalculateSubsidy(19.0m, 3.00m));
    }

    [Fact]
    public void MissingSnapshotRate_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(
            () => V160CorrectionFinancialRules.RequireSnapshotRate(null));
    }
}
