namespace FieldVisit.Application;

public sealed record V170LocationSearchRequest(
    string? Query,
    string? City,
    string? District,
    int? ProjectId,
    int Page = 1,
    int PageSize = 20);

public sealed record V170LocationSearchSpec(
    string? Query,
    string? City,
    string? District,
    int? ProjectId,
    int Page,
    int PageSize);

public sealed record V170LocationSearchItemDto(
    int LocationId,
    string? LocationCode,
    string LocationName,
    string LocationType,
    string? City,
    string? District,
    string? Address,
    string? PlusCode,
    decimal? Latitude,
    decimal? Longitude);

public sealed record V170LocationSearchResult(
    IReadOnlyList<V170LocationSearchItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasNextPage);

public static class V170LocationSearchRules
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public const int MaxPage = 100000;

    public static V170LocationSearchSpec Normalize(
        V170LocationSearchRequest request)
    {
        if (request.ProjectId.HasValue && request.ProjectId.Value <= 0)
            throw new InvalidOperationException("ProjectId 必須大於 0。");

        var query = TrimToNull(request.Query);
        var city = TrimToNull(request.City);
        var district = TrimToNull(request.District);

        if (query is { Length: > 200 })
            throw new InvalidOperationException("搜尋關鍵字不可超過 200 個字元。");

        if (city is { Length: > 50 })
            throw new InvalidOperationException("縣市不可超過 50 個字元。");

        if (district is { Length: > 50 })
            throw new InvalidOperationException("鄉鎮市區不可超過 50 個字元。");

        var page = request.Page < 1
            ? 1
            : Math.Min(request.Page, MaxPage);

        var pageSize = request.PageSize <= 0
            ? DefaultPageSize
            : Math.Min(request.PageSize, MaxPageSize);

        return new V170LocationSearchSpec(
            query,
            city,
            district,
            request.ProjectId,
            page,
            pageSize);
    }

    public static void EnsurePickerRole(
        IReadOnlyList<string> roles)
    {
        var allowed =
            roles.Any(x =>
                x.Equals("visitor", StringComparison.OrdinalIgnoreCase)
                || x.Equals("leader", StringComparison.OrdinalIgnoreCase)
                || x.Equals("admin", StringComparison.OrdinalIgnoreCase));

        if (!allowed)
            throw new UnauthorizedAccessException(
                "目前角色無權使用地點選擇功能。");
    }

    private static string? TrimToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}
