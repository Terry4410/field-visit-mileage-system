namespace FieldVisit.Application;

public sealed record V180SearchRequest(
    string? Keyword = null, string? Status = null, int? TeamId = null,
    DateOnly? StartDate = null, DateOnly? EndDate = null,
    bool? IsActive = null, string? Role = null, int Page = 1, int PageSize = 50,
    long? VisitTripId = null, string? TripNo = null);

public sealed record V180TeamRow(int TeamId, int OrganizationId, string TeamCode,
    string TeamName, bool IsActive, int MemberCount);
public sealed record V180ProjectRow(int ProjectId, int? TeamId, string ProjectCode,
    string ProjectName, string? Description, string LocationMode, DateOnly? StartDate,
    DateOnly? EndDate, bool IsActive, int LocationCount);
public sealed record V180OrderItem(int VisitTypeId, int SortOrder);
public sealed record V180MoveVisitTypeRequest(string Direction, IReadOnlyList<V180OrderItem> ExpectedOrder);

public static class V180QueryRules
{
    public static V180SearchRequest Normalize(V180SearchRequest request)
    {
        if (request.StartDate.HasValue && request.EndDate.HasValue && request.EndDate < request.StartDate)
            throw new InvalidOperationException("查詢結束日期不可早於開始日期。");
        var keyword = request.Keyword?.Trim();
        if (keyword?.Length > 200) throw new InvalidOperationException("搜尋關鍵字不可超過 200 字。");
        return request with {
            Keyword = string.IsNullOrEmpty(keyword) ? null : keyword,
            Status = string.IsNullOrWhiteSpace(request.Status) ? null : request.Status.Trim(),
            Role = string.IsNullOrWhiteSpace(request.Role) ? null : request.Role.Trim().ToLowerInvariant(),
            Page = Math.Clamp(request.Page, 1, int.MaxValue / 100),
            PageSize = Math.Clamp(request.PageSize, 1, 100)
        };
    }

    public static IReadOnlyList<V180OrderItem> Move(
        IReadOnlyList<V180OrderItem> current, int id, V180MoveVisitTypeRequest request)
    {
        if (request.ExpectedOrder is null || !current.SequenceEqual(request.ExpectedOrder))
            throw new InvalidOperationException("ORDER_CONFLICT：排序已變更，請重新整理後再操作。");
        if (request.Direction is not ("up" or "down"))
            throw new InvalidOperationException("移動方向只允許 up 或 down。");
        var ordered = current.ToList();
        var index = ordered.FindIndex(x => x.VisitTypeId == id);
        if (index < 0) throw new KeyNotFoundException("找不到拜訪形式。");
        var next = index + (request.Direction == "up" ? -1 : 1);
        if (next < 0 || next >= ordered.Count) return ordered;
        (ordered[index], ordered[next]) = (ordered[next], ordered[index]);
        return ordered.Select((x, i) => x with { SortOrder = checked((i + 1) * 10) }).ToList();
    }
}

public sealed record MileageRateQueryRequest(string? Keyword = null, string? Status = null,
    string? VehicleType = null, DateOnly? StartDate = null, DateOnly? EndDate = null,
    int Page = 1, int PageSize = 50);

public static class V180MileageRateQueryRules
{
    public static MileageRateQueryRequest Normalize(MileageRateQueryRequest request)
    {
        var search = V180QueryRules.Normalize(new(Keyword: request.Keyword, Status: request.Status,
            StartDate: request.StartDate, EndDate: request.EndDate, Page: request.Page, PageSize: request.PageSize));
        var vehicle = string.IsNullOrWhiteSpace(request.VehicleType) ? null : request.VehicleType.Trim();
        if (vehicle is not (null or "Car" or "Motorcycle")) throw new InvalidOperationException("交通工具不正確。");
        if (search.Status is not (null or "Active" or "Inactive")) throw new InvalidOperationException("費率狀態不正確。");
        return request with { Keyword = search.Keyword, Status = search.Status, VehicleType = vehicle, Page = search.Page, PageSize = search.PageSize };
    }

    // Source rows must already be obtained through the existing authorized master service.
    public static IReadOnlyList<MileageRateDto> Filter(IEnumerable<MileageRateDto> rows, MileageRateQueryRequest input)
    {
        var r = Normalize(input);
        return rows.Where(x =>
            (r.VehicleType is null || x.VehicleType == r.VehicleType) &&
            (r.Status is null || x.IsActive == (r.Status == "Active")) &&
            (r.Keyword is null || x.RuleName.Contains(r.Keyword, StringComparison.OrdinalIgnoreCase)) &&
            (!r.StartDate.HasValue || !x.EffectiveTo.HasValue || x.EffectiveTo >= r.StartDate) &&
            (!r.EndDate.HasValue || x.EffectiveFrom <= r.EndDate))
            .OrderByDescending(x => x.EffectiveFrom).ThenBy(x => x.VehicleType).ThenByDescending(x => x.MileageRateRuleId).ToArray();
    }
}
