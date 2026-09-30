using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180MasterDataRulesTests
{
    [Fact]
    public void NormalizeEmploymentStatus_UsesCanonicalValue()
    {
        Assert.Equal("Active", V180MasterDataRules.NormalizeEmploymentStatus(" active "));
    }

    [Fact]
    public void NormalizeEmploymentStatus_RejectsUnknownValue()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V180MasterDataRules.NormalizeEmploymentStatus("Working"));
    }

    [Fact]
    public void ValidatePeriod_RejectsReverseRange()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V180MasterDataRules.ValidatePeriod(
                new DateOnly(2026, 10, 2),
                new DateOnly(2026, 10, 1),
                "Test"));
    }

    [Theory]
    [InlineData("2026-09-01", null, "2026-09-30", "2026-10-01", true)]
    [InlineData("2026-09-01", "2026-09-30", "2026-09-30", null, true)]
    [InlineData("2026-09-01", "2026-09-29", "2026-09-30", null, false)]
    public void Overlaps_UsesInclusivePeriods(
        string leftFrom,
        string? leftTo,
        string rightFrom,
        string? rightTo,
        bool expected)
    {
        var result = V180MasterDataRules.Overlaps(
            DateOnly.Parse(leftFrom),
            leftTo is null ? null : DateOnly.Parse(leftTo),
            DateOnly.Parse(rightFrom),
            rightTo is null ? null : DateOnly.Parse(rightTo));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Covers_OpenEndedOuter_CoversOpenEndedInner()
    {
        Assert.True(V180MasterDataRules.Covers(
            new DateOnly(2026, 1, 1),
            null,
            new DateOnly(2026, 9, 1),
            null));
    }

    [Fact]
    public void Covers_FiniteOuter_DoesNotCoverOpenEndedInner()
    {
        Assert.False(V180MasterDataRules.Covers(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            new DateOnly(2026, 9, 1),
            null));
    }

    [Theory]
    [InlineData("Y", true)]
    [InlineData("No", false)]
    [InlineData("是", true)]
    [InlineData("0", false)]
    public void ParseBoolean_AcceptsBusinessFriendlyValues(string raw, bool expected)
    {
        Assert.Equal(expected, V180MasterDataRules.ParseBoolean(raw, "Flag"));
    }
}
