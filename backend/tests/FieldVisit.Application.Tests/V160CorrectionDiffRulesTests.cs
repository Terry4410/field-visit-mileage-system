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
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Empty_or_invalid_change_count_cannot_be_approved(int count)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => V160CorrectionDiffRules.RequirePersistedChangesForApproval(count));
        Assert.Contains("差異明細不存在", error.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Persisted_correction_differences_can_proceed_to_approval(int count)
    {
        V160CorrectionDiffRules.RequirePersistedChangesForApproval(count);
    }

}
