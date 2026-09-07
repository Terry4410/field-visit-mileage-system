namespace FieldVisit.Application;

public sealed record V180SearchRequest(
    string? Keyword = null, string? Status = null, int? TeamId = null,
    DateOnly? StartDate = null, DateOnly? EndDate = null,
    bool? IsActive = null, string? Role = null, int Page = 1, int PageSize = 50);

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
