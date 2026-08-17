using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170LocationGeocodingRulesTests
{
    [Fact]
    public void NameOnlyEdit_DoesNotRequireGeocoding()
    {
        var changed =
            V170LocationGeocodingRules.GeocodingInputChanged(
                "540南投縣南投市三和里中興路660號",
                null,
                "540南投縣南投市三和里中興路660號",
                null);

        Assert.False(changed);
    }

    [Fact]
    public void AddressEdit_RequiresGeocoding()
    {
        var changed =
            V170LocationGeocodingRules.GeocodingInputChanged(
                "舊地址",
                null,
                "新地址",
                null);

        Assert.True(changed);
    }

    [Fact]
    public void PlusCodeEdit_RequiresGeocoding()
    {
        var changed =
            V170LocationGeocodingRules.GeocodingInputChanged(
                null,
                "7Q23+AA",
                null,
                "7Q23+BB");

        Assert.True(changed);
    }

    [Fact]
    public void PlusCodeCaseOnlyDifference_DoesNotRequireGeocoding()
    {
        var changed =
            V170LocationGeocodingRules.GeocodingInputChanged(
                null,
                "7q23+aa",
                null,
                "7Q23+AA");

        Assert.False(changed);
    }

    [Fact]
    public void LeadingAndTrailingWhitespace_DoesNotRequireGeocoding()
    {
        var changed =
            V170LocationGeocodingRules.GeocodingInputChanged(
                " 地址 ",
                " CODE ",
                "地址",
                "code");

        Assert.False(changed);
    }
}
