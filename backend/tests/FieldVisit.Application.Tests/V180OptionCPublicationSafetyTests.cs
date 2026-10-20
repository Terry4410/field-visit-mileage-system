using FieldVisit.Infrastructure;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OptionCPublicationSafetyTests
{
    private static readonly V180B3LocationFields Current =
        new("Original", "Taipei", "Xinyi", "Road 1", null, "12345678", "Old note");

    [Theory]
    [InlineData("name")]
    [InlineData("city")]
    [InlineData("district")]
    [InlineData("address")]
    [InlineData("plus")]
    public void Published_location_identity_and_geocoding_changes_require_versioned_swap(
        string changed)
    {
        var next = changed switch
        {
            "name" => Current with { LocationName = "Other" },
            "city" => Current with { City = "New Taipei" },
            "district" => Current with { District = "Other" },
            "address" => Current with { Address = "Road 2" },
            _ => Current with { PlusCode = "QWERTY" }
        };
        var error = Assert.Throws<InvalidOperationException>(() =>
            V180OptionCPublicationSafety.RequireNoPublishedIdentityOrGeocodeChange(
                Current, next));
        Assert.Equal("OPTIONC_PUBLISHED_IDENTITY_CHANGE_REQUIRES_VERSIONED_SWAP",
            error.Message);
    }

    [Fact]
    public void Metadata_only_change_does_not_require_geocoding_swap()
    {
        V180OptionCPublicationSafety.RequireNoPublishedIdentityOrGeocodeChange(
            Current, Current with { TaxId = "87654321", MasterNote = "New note" });
    }
}
