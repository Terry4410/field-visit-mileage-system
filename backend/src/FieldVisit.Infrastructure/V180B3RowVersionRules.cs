namespace FieldVisit.Infrastructure;

/// <summary>
/// SQL Server ROWVERSION is exactly 8 bytes and serializes to exactly 12
/// canonical Base64 characters. Reject unexpected lengths BEFORE decoding
/// untrusted client input, avoiding large allocations and lenient aliases.
/// This only checks the token format, not authorization or row ownership.
/// </summary>
public static class V180B3RowVersionRules
{
    public static byte[] Parse(string? supplied)
    {
        if(supplied is null || supplied.Length!=12)
            throw new InvalidOperationException("ROWVERSION_CONFLICT");
        Span<byte> decoded=stackalloc byte[8];
        if(!Convert.TryFromBase64String(supplied,decoded,out var count)
            ||count!=8
            ||!string.Equals(Convert.ToBase64String(decoded),supplied,
                StringComparison.Ordinal))
            throw new InvalidOperationException("ROWVERSION_CONFLICT");
        return decoded.ToArray();
    }
}
