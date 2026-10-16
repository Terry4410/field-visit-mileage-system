namespace FieldVisit.Infrastructure;

/// <summary>
/// Option C fail-closed publication boundary. A published location is the
/// authoritative address/identity for historical and active trips. High-risk
/// changes must stage a separate reviewed version and swap only after valid
/// geocoding, duplicate review and audit; direct overwrite is prohibited.
/// </summary>
public static class V180OptionCPublicationSafety
{
    public static void RequireNoPublishedIdentityOrGeocodeChange(
        V180B3LocationFields before, V180B3LocationFields proposed)
    {
        if (!string.Equals(before.LocationName, proposed.LocationName, StringComparison.Ordinal)
            || !string.Equals(before.City, proposed.City, StringComparison.Ordinal)
            || !string.Equals(before.District, proposed.District, StringComparison.Ordinal)
            || !string.Equals(before.Address, proposed.Address, StringComparison.Ordinal)
            || !string.Equals(before.PlusCode, proposed.PlusCode, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "OPTIONC_PUBLISHED_IDENTITY_CHANGE_REQUIRES_VERSIONED_SWAP");
    }
}
