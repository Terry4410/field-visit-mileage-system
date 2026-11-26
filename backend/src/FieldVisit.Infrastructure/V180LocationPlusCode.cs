namespace FieldVisit.Infrastructure;

/// <summary>Decode full, unpadded Open Location Codes of 10–15 digits
/// to their cell center. Short or padded codes are intentionally rejected:
/// a reference position or coarse cell is not adequate geocode evidence.
/// See https://github.com/google/open-location-code/blob/main/Documentation/Specification/olc_definition.adoc
/// </summary>
public static class V180LocationPlusCode
{
    public static (decimal Latitude, decimal Longitude) Decode(string code)
    {
        const string alphabet = "23456789CFGHJMPQRVWX";
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length < 11 || normalized.Length > 16 || normalized[8] != '+')
            throw Invalid();
        var digits = normalized.Remove(8, 1);
        var values = digits.Select(c => alphabet.IndexOf(c)).ToArray();
        if (values.Any(x => x < 0) || values[0] >= 9 || values[1] >= 18)
            throw Invalid();
        decimal lat = -90m, lon = -180m, latSize = 20m, lonSize = 20m;
        for (var i = 0; i < 10; i += 2)
        {
            lat += values[i] * latSize;
            lon += values[i + 1] * lonSize;
            if (i < 8) { latSize /= 20m; lonSize /= 20m; }
        }
        for (var i = 10; i < values.Length; i++)
        {
            latSize /= 5m;
            lonSize /= 4m;
            lat += (values[i] / 4) * latSize;
            lon += (values[i] % 4) * lonSize;
        }
        return (decimal.Round(lat + latSize / 2, 8), decimal.Round(lon + lonSize / 2, 8));
    }

    private static InvalidOperationException Invalid() =>
        new("請輸入有效完整 Plus Code（加號前八碼、後兩至七碼）。");
}
