using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class V180HistoryRateReportController(IV160FinalRepository repository, ICurrentUserService current,
    MasterService master, IConfiguration configuration) : ControllerBase
{
    private const int MaxRows = 10000;
    private const int BatchSize = 100;

    // Same scope and filters as the history list. The existing G1ReportExportGuard
    // checks live identity and role/team scope before and after generating files.
    [HttpGet("corrections/export.xlsx")]
    public Task<IActionResult> CorrectionExcel([FromQuery] V180SearchRequest request, CancellationToken ct)
        => Corrections("xlsx", request, ct);
    [HttpGet("corrections/export.pdf")]
    public Task<IActionResult> CorrectionPdf([FromQuery] V180SearchRequest request, CancellationToken ct)
        => Corrections("pdf", request, ct);

    private async Task<IActionResult> Corrections(string format, V180SearchRequest request, CancellationToken ct)
    {
        var user = current.GetRequired();
        request = V180QueryRules.Normalize(request);
        var first = await repository.SearchCorrectionsAsync(user, request with { Page = 1, PageSize = BatchSize }, ct);
        if (first.TotalCount > MaxRows) throw new InvalidOperationException("超過匯出上限 10000 筆，請縮小查詢範圍。");
        var rows = first.Items.ToList();
        for (var page = 2; rows.Count < first.TotalCount; page++)
        {
            ct.ThrowIfCancellationRequested();
            var next = await repository.SearchCorrectionsAsync(user, request with { Page = page, PageSize = BatchSize }, ct);
            if (next.TotalCount != first.TotalCount || next.Items.Count == 0)
                throw new InvalidOperationException("查詢期間資料已變更，請重新查詢。");
            rows.AddRange(next.Items);
        }
        if (rows.Count != first.TotalCount || rows.Select(x => x.CorrectionRequestId).Distinct().Count() != first.TotalCount)
            throw new InvalidOperationException("查詢期間資料已變更，請重新查詢。");
        var data = rows.Select(x => new object?[] {
            DateTime.SpecifyKind(x.RequestedAt, DateTimeKind.Utc).AddHours(8).ToString("yyyy-MM-dd"),
            x.TripNo, x.VisitorName, x.Reason, CorrectionStatus(x.Status),
            $"V{x.BaseSnapshotVersion}", x.ResultSnapshotVersion.HasValue ? $"V{x.ResultSnapshotVersion}" : "—"
        }).ToArray();
        var filters = $"申請日期：{Date(request.StartDate)}～{Date(request.EndDate)}　狀態：{CorrectionStatus(request.Status)}　關鍵字：{request.Keyword ?? "—"}　行程：{request.TripNo ?? "全部"}";
        var file = await new ReportDocumentService(configuration).CreateTabularAsync(format, "更正歷史",
            ["申請日", "行程編號", "外訪員", "更正原因", "狀態", "原核准版本", "結果版本"],
            [86f,120f,100f,249f,95f,70f,65.89f], data, filters, user, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("admin/query-exports/mileage-rates/search"), Authorize(Roles = "admin")]
    public async Task<IActionResult> RateSearch([FromQuery] MileageRateQueryRequest request, CancellationToken ct)
    {
        request = V180MileageRateQueryRules.Normalize(request);
        var rows = await ReadRates(request, ct);
        return Ok(new PagedResult<MileageRateDto>(rows.Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize).ToArray(), request.Page, request.PageSize, rows.Count));
    }

    [HttpGet("admin/query-exports/mileage-rates/export.xlsx"), Authorize(Roles = "admin")]
    public Task<IActionResult> RateExcel([FromQuery] MileageRateQueryRequest request, CancellationToken ct)
        => Rates("xlsx", request, ct);
    [HttpGet("admin/query-exports/mileage-rates/export.pdf"), Authorize(Roles = "admin")]
    public Task<IActionResult> RatePdf([FromQuery] MileageRateQueryRequest request, CancellationToken ct)
        => Rates("pdf", request, ct);

    private async Task<IReadOnlyList<MileageRateDto>> ReadRates(MileageRateQueryRequest request, CancellationToken ct)
        => V180MileageRateQueryRules.Filter(await master.RatesAsync(ct), request);

    private async Task<IActionResult> Rates(string format, MileageRateQueryRequest request, CancellationToken ct)
    {
        var user = current.GetRequired();
        request = V180MileageRateQueryRules.Normalize(request);
        var rows = await ReadRates(request, ct);
        if (rows.Count > MaxRows) throw new InvalidOperationException("超過匯出上限 10000 筆，請縮小查詢範圍。");
        var data = rows.Select(x => new object?[] { Vehicle(x.VehicleType), Date(x.EffectiveFrom),
            x.EffectiveTo.HasValue ? Date(x.EffectiveTo) : "無期限", x.RatePerKm, x.RuleName,
            x.IsActive ? "啟用" : "停用", x.OrganizationId.HasValue ? "組織費率" : "系統共用" }).ToArray();
        var filters = $"交通工具：{Vehicle(request.VehicleType)}　狀態：{(request.Status == "Active" ? "啟用" : request.Status == "Inactive" ? "停用" : "全部")}　適用期間：{Date(request.StartDate)}～{Date(request.EndDate)}　關鍵字：{request.Keyword ?? "—"}";
        var file = await new ReportDocumentService(configuration).CreateTabularAsync(format, "補助費率",
            ["交通工具", "生效日期", "失效日期", "每公里補助", "規則名稱／備註", "狀態", "範圍"],
            [90f,105f,105f,105f,230f,75f,75.89f], data, filters, user, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }
    private static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd") ?? "不限";
    private static string Vehicle(string? value) => value switch { "Car" => "汽車", "Motorcycle" => "機車", _ => "全部" };
    private static string CorrectionStatus(string? value) => value switch {
        "PendingLeaderReview" => "待小組長審核", "PendingAdminClose" => "待管理者結案",
        "Closed" => "已結案", "Rejected" => "已退回", _ => "全部狀態"
    };
}
