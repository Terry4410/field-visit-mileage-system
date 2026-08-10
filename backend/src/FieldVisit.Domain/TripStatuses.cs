namespace FieldVisit.Domain;

public static class TripStatuses
{
    public const string Draft = "Draft";
    public const string Submitted = "Submitted";
    public const string RoutePending = "RoutePending";
    public const string RouteCalculated = "RouteCalculated";
    public const string PendingApproval = "PendingApproval";
    public const string Approved = "Approved";
    public const string Returned = "Returned";
    public const string Cancelled = "Cancelled";

    public static string Display(string status) => status switch
    {
        Draft => "草稿",
        Submitted => "已送出",
        RoutePending => "待計算",
        RouteCalculated => "里程已計算",
        PendingApproval => "待核准",
        Approved => "已核准",
        Returned => "已退回",
        Cancelled => "已取消",
        _ => status
    };
}
