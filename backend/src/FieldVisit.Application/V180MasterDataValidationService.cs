namespace FieldVisit.Application;

public sealed class V180MasterDataValidationService
{
    public static void Period(DateOnly from, DateOnly? to)
    {
        if (to.HasValue && to.Value < from)
            throw new InvalidOperationException("INVALID_EFFECTIVE_PERIOD");
    }

    public static bool Overlaps(
        DateOnly from,
        DateOnly? to,
        DateOnly otherFrom,
        DateOnly? otherTo) =>
        from <= (otherTo ?? DateOnly.MaxValue)
        && otherFrom <= (to ?? DateOnly.MaxValue);

    public static bool Covers(
        DateOnly from,
        DateOnly? to,
        DateOnly requiredFrom,
        DateOnly? requiredTo) =>
        from <= requiredFrom
        && (!requiredTo.HasValue
            ? !to.HasValue
            : !to.HasValue || to.Value >= requiredTo.Value);

    public static void Status(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Trim() is not ("Active" or "Leave" or "Terminated" or "PreHire"))
            throw new InvalidOperationException("INVALID_EMPLOYMENT_STATUS");
    }

    public static void RowVersion(string? expected, byte[] actual)
    {
        if (string.IsNullOrWhiteSpace(expected))
            throw new InvalidOperationException("ROWVERSION_REQUIRED");

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(expected);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("ROWVERSION_INVALID");
        }

        if (!decoded.SequenceEqual(actual))
            throw new InvalidOperationException("ROWVERSION_CONFLICT");
    }
}
