using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V160CorrectionDiffRulesTests
{
    [Theory]
    [InlineData("10.00", "10")]
    [InlineData("9.70", "9.7")]
    [InlineData("18.80", "18.8")]
    public void DecimalScale_DoesNotCreateDifference(
        string left,
        string right)
    {
        decimal? a = decimal.Parse(left);
        decimal? b = decimal.Parse(right);

        Assert.True(
            V160CorrectionDiffRules.AreEquivalent(
                a,
                b));
    }

    [Fact]
    public void DifferentDecimal_IsDetected()
    {
        decimal? a = 18.8m;
        decimal? b = 15m;

        Assert.False(
            V160CorrectionDiffRules.AreEquivalent(
                a,
                b));
    }

    [Theory]
    [InlineData("10.00", "10")]
    [InlineData("9.70", "9.7")]
    [InlineData("37.50", "37.5")]
    public void DecimalDisplay_RemovesScaleOnlyZeros(
        string input,
        string expected)
    {
        var value = decimal.Parse(input);

        Assert.Equal(
            expected,
            V160CorrectionDiffRules.ToDisplayValue(
                value));
    }

    [Fact]
    public void StringDifference_IsStillDetected()
    {
        Assert.False(
            V160CorrectionDiffRules.AreEquivalent(
                "原備註",
                "新備註"));
    }
}
