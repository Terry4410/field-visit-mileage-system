namespace FieldVisit.Application;

public sealed record ManagedLocationGovernanceRequest(
    string? TaxId,
    string? MasterNote,
    int? DuplicateOfLocationId,
    string? DuplicateReason,
    string RowVersion);

public sealed record ManagedLocationGovernanceDto(
    int LocationId,
    string? TaxId,
    string? MasterNote,
    int? DuplicateOfLocationId,
    string? DuplicateReason,
    string RowVersion);

public interface IV180ManagedLocationGovernanceRepository
{
    Task<PagedResult<ManagedLocationDto>> SearchManagedLocationsAsync(CurrentUserDto user, ManagedLocationQueryRequest request, CancellationToken ct);
    Task<ManagedLocationDto> UpdateManagedLocationAsync(CurrentUserDto user, int locationId, SaveManagedLocationRequest request, CancellationToken ct);
    Task<ManagedLocationGovernanceDto> UpdateGovernanceAsync(CurrentUserDto user, int locationId, ManagedLocationGovernanceRequest request, CancellationToken ct);
    Task DeactivateManagedLocationAsync(CurrentUserDto user, int locationId, string rowVersion, CancellationToken ct);
}

public static class V180LocationGovernanceRules
{
    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static (string? TaxId, string? MasterNote, int? DuplicateOfLocationId, string? DuplicateReason) Normalize(
        int locationId,
        ManagedLocationGovernanceRequest request)
    {
        var taxId = NormalizeOptional(request.TaxId);
        var masterNote = NormalizeOptional(request.MasterNote);
        var reason = NormalizeOptional(request.DuplicateReason);

        if (request.DuplicateOfLocationId == locationId)
            throw new InvalidOperationException("LOCATION_DUPLICATE_SELF：重複參照不可指向自己。");
        if (request.DuplicateOfLocationId.HasValue && reason is null)
            throw new InvalidOperationException("LOCATION_DUPLICATE_REASON_REQUIRED：設定重複參照時必須填寫原因。");
        if (!request.DuplicateOfLocationId.HasValue)
            reason = null;

        return (taxId, masterNote, request.DuplicateOfLocationId, reason);
    }

    public static byte[] RequireRowVersion(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("ROWVERSION_REQUIRED：必須提供 RowVersion。");
        try
        {
            var bytes = Convert.FromBase64String(token);
            if (bytes.Length != 8)
                throw new InvalidOperationException("ROWVERSION_INVALID：RowVersion 格式不正確。");
            return bytes;
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("ROWVERSION_INVALID：RowVersion 必須是有效 Base64。");
        }
    }
}
