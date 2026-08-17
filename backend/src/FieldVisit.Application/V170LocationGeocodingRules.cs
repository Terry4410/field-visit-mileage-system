namespace FieldVisit.Application;

public static class V170LocationGeocodingRules
{
    public static bool GeocodingInputChanged(
        string? currentAddress,
        string? currentPlusCode,
        string? nextAddress,
        string? nextPlusCode)
    {
        var oldAddress = Normalize(currentAddress);
        var newAddress = Normalize(nextAddress);

        var oldPlusCode = Normalize(currentPlusCode);
        var newPlusCode = Normalize(nextPlusCode);

        return !string.Equals(
                   oldAddress,
                   newAddress,
                   StringComparison.Ordinal)
               ||
               !string.Equals(
                   oldPlusCode,
                   newPlusCode,
                   StringComparison.OrdinalIgnoreCase);
    }

    public static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
