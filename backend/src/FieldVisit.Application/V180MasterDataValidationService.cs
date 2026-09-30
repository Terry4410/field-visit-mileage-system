namespace FieldVisit.Application;

public sealed class V180MasterDataValidationService
{
    public static void Period(DateOnly from, DateOnly? to) { if (to.HasValue && to < from) throw new InvalidOperationException("INVALID_EFFECTIVE_PERIOD"); }
    public static bool Overlaps(DateOnly from, DateOnly? to, DateOnly otherFrom, DateOnly? otherTo) => from <= (otherTo ?? DateOnly.MaxValue) && otherFrom <= (to ?? DateOnly.MaxValue);
    public static void Status(string value) { if (value.Trim() is not ("Active" or "Leave" or "Terminated" or "PreHire")) throw new InvalidOperationException("INVALID_EMPLOYMENT_STATUS"); }
    public static void RowVersion(string? expected, byte[] actual) { if (!string.IsNullOrWhiteSpace(expected) && !Convert.FromBase64String(expected).SequenceEqual(actual)) throw new InvalidOperationException("ROWVERSION_CONFLICT"); }
}
