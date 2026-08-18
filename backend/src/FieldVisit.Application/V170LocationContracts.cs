namespace FieldVisit.Application;

public sealed record V170LocationSearchRequest(
    string? Query,
    string? City,
    string? District,
    int? ProjectId,
    int Page = 1,
    int PageSize = 20,
    int? TeamId = null);

public sealed record V170LocationSearchSpec(
    string? Query,
    string? City,
    string? District,
    int? ProjectId,
    int Page,
    int PageSize,
    int? TeamId = null);

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

public sealed record V170LocationFavoriteDto(
    int LocationId,
    string? LocationCode,
    string LocationName,
    string LocationType,
    string? City,
    string? District,
    string? Address,
    string? PlusCode,
    decimal? Latitude,
    decimal? Longitude,
    int SortOrder,
    DateTime CreatedAt);

public sealed record V170LocationRecentDto(
    int LocationId,
    string? LocationCode,
    string LocationName,
    string LocationType,
    string? City,
    string? District,
    string? Address,
    string? PlusCode,
    decimal? Latitude,
    decimal? Longitude,
    DateOnly LastVisitedOn);

public sealed record V170LocationNearbyRequest(
    decimal Latitude,
    decimal Longitude,
    int? ProjectId,
    int Limit = 20,
    int? TeamId = null);

public sealed record V170LocationNearbySpec(
    decimal Latitude,
    decimal Longitude,
    int? ProjectId,
    int Limit,
    int? TeamId = null);

public sealed record V170LocationNearbyDto(
    int LocationId,
    string? LocationCode,
    string LocationName,
    string LocationType,
    string? City,
    string? District,
    string? Address,
    string? PlusCode,
    decimal Latitude,
    decimal Longitude,
    decimal DistanceKm);

public sealed record V170LocationFavoriteOrderRequest(
    IReadOnlyList<int> LocationIds);

public static class V170LocationSearchRules
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public const int MaxPage = 100000;

    public static V170LocationSearchSpec Normalize(
        V170LocationSearchRequest request)
    {
        if (request.ProjectId.HasValue
            && request.ProjectId.Value <= 0)
            throw new InvalidOperationException(
                "ProjectId 必須大於 0。");

        var query = TrimToNull(request.Query);
        var city = TrimToNull(request.City);
        var district = TrimToNull(request.District);

        if (query is { Length: > 200 })
            throw new InvalidOperationException(
                "搜尋關鍵字不可超過 200 個字元。");

        if (city is { Length: > 50 })
            throw new InvalidOperationException(
                "縣市不可超過 50 個字元。");

        if (district is { Length: > 50 })
            throw new InvalidOperationException(
                "鄉鎮市區不可超過 50 個字元。");

        var page = request.Page < 1
            ? 1
            : Math.Min(request.Page, MaxPage);

        var pageSize = request.PageSize <= 0
            ? DefaultPageSize
            : Math.Min(request.PageSize, MaxPageSize);

        if (request.TeamId.HasValue
            && request.TeamId.Value <= 0)
            throw new InvalidOperationException(
                "TeamId 必須大於 0。");

        return new V170LocationSearchSpec(
            query,
            city,
            district,
            request.ProjectId,
            page,
            pageSize,
            request.TeamId);
    }

    public static void EnsurePickerRole(
        IReadOnlyList<string> roles)
    {
        var allowed =
            roles.Any(x =>
                x.Equals(
                    "visitor",
                    StringComparison.OrdinalIgnoreCase)
                || x.Equals(
                    "leader",
                    StringComparison.OrdinalIgnoreCase)
                || x.Equals(
                    "admin",
                    StringComparison.OrdinalIgnoreCase));

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

public static class V170LocationPickerRules
{
    public const int DefaultRecentLimit = 20;
    public const int MaxRecentLimit = 50;
    public const int MaxFavoriteOrderCount = 200;

    public static int EnsureLocationId(
        int locationId)
    {
        if (locationId <= 0)
            throw new InvalidOperationException(
                "LocationId 必須大於 0。");

        return locationId;
    }

    public static IReadOnlyList<int> NormalizeFavoriteOrder(
        V170LocationFavoriteOrderRequest request)
    {
        if (request.LocationIds.Count
            > MaxFavoriteOrderCount)
        {
            throw new InvalidOperationException(
                $"常用地點排序一次最多 {MaxFavoriteOrderCount} 筆。");
        }

        if (request.LocationIds.Any(x => x <= 0))
            throw new InvalidOperationException(
                "LocationId 必須大於 0。");

        if (request.LocationIds.Distinct().Count()
            != request.LocationIds.Count)
        {
            throw new InvalidOperationException(
                "常用地點排序不可包含重複的 LocationId。");
        }

        return request.LocationIds.ToArray();
    }

    public const int DefaultNearbyLimit = 20;
    public const int MaxNearbyLimit = 50;

    public static int NormalizeRecentLimit(
        int limit)
    {
        if (limit <= 0)
            return DefaultRecentLimit;

        return Math.Min(
            limit,
            MaxRecentLimit);
    }

    public static V170LocationNearbySpec NormalizeNearby(
        V170LocationNearbyRequest request)
    {
        if (request.Latitude < -90m
            || request.Latitude > 90m)
        {
            throw new InvalidOperationException(
                "Latitude 必須介於 -90 到 90。");
        }

        if (request.Longitude < -180m
            || request.Longitude > 180m)
        {
            throw new InvalidOperationException(
                "Longitude 必須介於 -180 到 180。");
        }

        if (request.ProjectId.HasValue
            && request.ProjectId.Value <= 0)
        {
            throw new InvalidOperationException(
                "ProjectId 必須大於 0。");
        }

        var limit =
            request.Limit <= 0
                ? DefaultNearbyLimit
                : Math.Min(
                    request.Limit,
                    MaxNearbyLimit);

        if (request.TeamId.HasValue
            && request.TeamId.Value <= 0)
            throw new InvalidOperationException(
                "TeamId 必須大於 0。");

        return new V170LocationNearbySpec(
            request.Latitude,
            request.Longitude,
            request.ProjectId,
            limit,
            request.TeamId);
    }

    public static decimal CalculateDistanceKm(
        decimal fromLatitude,
        decimal fromLongitude,
        decimal toLatitude,
        decimal toLongitude)
    {
        const double earthRadiusKm =
            6371.0088d;

        static double ToRadians(
            decimal degrees) =>
            (double)degrees
            * Math.PI
            / 180d;

        var lat1 =
            ToRadians(fromLatitude);

        var lat2 =
            ToRadians(toLatitude);

        var deltaLat =
            ToRadians(
                toLatitude
                - fromLatitude);

        var deltaLon =
            ToRadians(
                toLongitude
                - fromLongitude);

        var sinLat =
            Math.Sin(deltaLat / 2d);

        var sinLon =
            Math.Sin(deltaLon / 2d);

        var a =
            sinLat * sinLat
            + Math.Cos(lat1)
            * Math.Cos(lat2)
            * sinLon
            * sinLon;

        a = Math.Clamp(
            a,
            0d,
            1d);

        var c =
            2d
            * Math.Atan2(
                Math.Sqrt(a),
                Math.Sqrt(1d - a));

        return Convert.ToDecimal(
            earthRadiusKm * c);
    }
}
