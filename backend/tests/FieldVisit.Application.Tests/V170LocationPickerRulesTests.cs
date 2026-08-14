using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170LocationPickerRulesTests
{
    [Fact]
    public void EnsureLocationId_RejectsNonPositive()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V170LocationPickerRules
                .EnsureLocationId(0));
    }

    [Fact]
    public void NormalizeFavoriteOrder_PreservesOrder()
    {
        var result =
            V170LocationPickerRules
                .NormalizeFavoriteOrder(
                    new V170LocationFavoriteOrderRequest(
                        new[] { 30, 10, 20 }));

        Assert.Equal(
            new[] { 30, 10, 20 },
            result);
    }

    [Fact]
    public void NormalizeFavoriteOrder_RejectsDuplicates()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V170LocationPickerRules
                .NormalizeFavoriteOrder(
                    new V170LocationFavoriteOrderRequest(
                        new[] { 10, 10 })));
    }

    [Fact]
    public void NormalizeFavoriteOrder_RejectsInvalidId()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V170LocationPickerRules
                .NormalizeFavoriteOrder(
                    new V170LocationFavoriteOrderRequest(
                        new[] { 10, 0 })));
    }

    [Fact]
    public void NormalizeRecentLimit_UsesDefault()
    {
        Assert.Equal(
            V170LocationPickerRules.DefaultRecentLimit,
            V170LocationPickerRules
                .NormalizeRecentLimit(0));
    }

    [Fact]
    public void NormalizeRecentLimit_ClampsMaximum()
    {
        Assert.Equal(
            V170LocationPickerRules.MaxRecentLimit,
            V170LocationPickerRules
                .NormalizeRecentLimit(999));
    }
}
