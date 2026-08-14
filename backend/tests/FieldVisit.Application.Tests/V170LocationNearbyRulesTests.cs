using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170LocationNearbyRulesTests
{
    [Fact]
    public void NormalizeNearby_UsesDefaultLimit()
    {
        var result =
            V170LocationPickerRules.NormalizeNearby(
                new V170LocationNearbyRequest(
                    25.033m,
                    121.5654m,
                    null,
                    0));

        Assert.Equal(
            V170LocationPickerRules.DefaultNearbyLimit,
            result.Limit);
    }

    [Fact]
    public void NormalizeNearby_ClampsMaximumLimit()
    {
        var result =
            V170LocationPickerRules.NormalizeNearby(
                new V170LocationNearbyRequest(
                    25.033m,
                    121.5654m,
                    null,
                    999));

        Assert.Equal(
            V170LocationPickerRules.MaxNearbyLimit,
            result.Limit);
    }

    [Fact]
    public void NormalizeNearby_RejectsInvalidLatitude()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V170LocationPickerRules.NormalizeNearby(
                new V170LocationNearbyRequest(
                    91m,
                    121m,
                    null,
                    20)));
    }

    [Fact]
    public void NormalizeNearby_RejectsInvalidLongitude()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V170LocationPickerRules.NormalizeNearby(
                new V170LocationNearbyRequest(
                    25m,
                    181m,
                    null,
                    20)));
    }

    [Fact]
    public void NormalizeNearby_RejectsInvalidProjectId()
    {
        Assert.Throws<InvalidOperationException>(() =>
            V170LocationPickerRules.NormalizeNearby(
                new V170LocationNearbyRequest(
                    25m,
                    121m,
                    0,
                    20)));
    }

    [Fact]
    public void CalculateDistanceKm_SamePoint_IsZero()
    {
        var distance =
            V170LocationPickerRules
                .CalculateDistanceKm(
                    25.033m,
                    121.5654m,
                    25.033m,
                    121.5654m);

        Assert.Equal(
            0m,
            distance);
    }

    [Fact]
    public void CalculateDistanceKm_OneDegreeAtEquator_IsAbout111Km()
    {
        var distance =
            V170LocationPickerRules
                .CalculateDistanceKm(
                    0m,
                    0m,
                    0m,
                    1m);

        Assert.InRange(
            distance,
            111m,
            112m);
    }
}
