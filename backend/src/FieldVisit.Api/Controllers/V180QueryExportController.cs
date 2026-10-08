using System.Globalization;
using System.Text.Json;
using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace FieldVisit.Api.Controllers;

// All exports re-run the server-authorized query. Browser data or page count is not trusted.
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/admin/query-exports")]
public sealed class V180QueryExportController(
    V170PeopleAdminService people,
    IV160FinalRepository repository,
    ICurrentUserService current,
    V160FinalService locations,
    MasterService master) : ControllerBase
{
    private const int MaxRows = 10000;
    private const int BatchSize = 100;
    private static readonly string Mime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet("personnel.xlsx")]
    public async Task<IActionResult> Personnel([FromQuery] V170PeopleQueryRequest request, CancellationToken ct)
    {
        request = request with { UserType = "Internal" };
        var rows = await GetAllAsync(page => people.QueryAsync(request with { Page = page, PageSize = BatchSize }, ct), ct);
        return Workbook("人事資料", request,
            ["工號", "姓名", "Email", "人事狀態", "入職日", "離職日", "主要小組", "主要派駐據點", "就業中心"],
            rows.Select(x => Cells(x.EmployeeNo ?? x.UserCode, x.DisplayName, x.Email, x.EmploymentStatus,
                x.HireDate, x.TerminationDate, x.PrimaryTeamName, x.PrimaryDeploymentSiteName, x.PrimaryCenterName)));
    }

    [HttpGet("roles.xlsx")]
    public async Task<IActionResult> Roles([FromQuery] V170PeopleQueryRequest request, CancellationToken ct)
    {
        request = request with { UserType = "Internal" };
        var rows = await GetAllAsync(page => people.QueryAsync(request with { Page = page, PageSize = BatchSize }, ct), ct);
        return Workbook("角色與登入", request,
            ["工號", "姓名", "角色", "人事狀態", "實際登入", "管理小組"],
            rows.Select(x => Cells(x.EmployeeNo ?? x.UserCode, x.DisplayName,
                string.Join("、", x.Roles), x.EmploymentStatus, x.ActualAccess ? "可登入" : "不可登入",
                string.Join("、", x.TeamAssignments.Select(t => t.TeamName + (t.IsPrimary ? " ★" : ""))))));
    }

    [HttpGet("teams.xlsx")]
    public async Task<IActionResult> Teams([FromQuery] V180SearchRequest request, CancellationToken ct)
    {
        var user = current.GetRequired();
        var rows = await GetAllAsync(page => repository.SearchTeamsAsync(user, request with { Page = page, PageSize = BatchSize }, ct), ct);
        return Workbook("小組", request, ["小組代碼", "小組名稱", "啟用", "成員人數"],
            rows.Select(x => Cells(x.TeamCode, x.TeamName, x.IsActive ? "是" : "否", x.MemberCount)));
    }

    [HttpGet("members.xlsx")]
    public async Task<IActionResult> Members([FromQuery] V170PeopleQueryRequest request, CancellationToken ct)
    {
        if (!request.TeamId.HasValue || request.TeamId <= 0)
            return BadRequest("請先選擇小組，才能匯出該組成員。");
        request = request with { UserType = "Internal" };
        var rows = await GetAllAsync(page => people.QueryAsync(request with { Page = page, PageSize = BatchSize }, ct), ct);
        return Workbook("小組成員", request,
            ["工號", "姓名", "角色", "人事狀態", "主要小組", "其他小組", "主要派駐據點"],
            rows.Select(x => Cells(x.EmployeeNo ?? x.UserCode, x.DisplayName, string.Join("、", x.Roles),
                x.EmploymentStatus, x.PrimaryTeamName,
                string.Join("、", x.TeamAssignments.Where(t => !t.IsPrimary).Select(t => t.TeamName)),
                x.PrimaryDeploymentSiteName)));
    }

    [HttpGet("locations.xlsx")]
    public async Task<IActionResult> Locations([FromQuery] ManagedLocationQueryRequest request, CancellationToken ct)
    {
        var rows = await GetAllAsync(page => locations.SearchManagedLocationsAsync(
            request with { Page = page, PageSize = BatchSize }, ct), ct);
        return Workbook("地點主檔", request,
            ["地點代碼", "名稱", "統一編號", "主檔備註", "小組", "縣市", "鄉鎮區", "地址", "Plus Code", "類型", "審核狀態", "解析狀態", "啟用"],
            rows.Select(x => Cells(x.LocationCode, x.LocationName, x.TaxId, x.MasterNote, x.TeamName,
                x.City, x.District, x.Address, x.PlusCode, x.LocationType, x.ApprovalStatus,
                x.GeocodingStatus, x.IsActive ? "是" : "否")));
    }

    [HttpGet("projects.xlsx")]
    public async Task<IActionResult> Projects([FromQuery] V180SearchRequest request, CancellationToken ct)
    {
        var user = current.GetRequired();
        var rows = await GetAllAsync(page => repository.SearchProjectsAsync(user, request with { Page = page, PageSize = BatchSize }, ct), ct);
        return Workbook("專案", request,
            ["專案代碼", "專案名稱", "歸屬小組 ID", "說明", "地點方式", "開始日", "結束日", "啟用", "固定地點數"],
            rows.Select(x => Cells(x.ProjectCode, x.ProjectName, x.TeamId, x.Description,
                x.LocationMode, x.StartDate, x.EndDate, x.IsActive ? "是" : "否", x.LocationCount)));
    }

    [HttpGet("visit-types.xlsx")]
    public async Task<IActionResult> VisitTypes(CancellationToken ct)
    {
        var rows = await master.VisitTypesAsync(ct);
        if (rows.Count > MaxRows) throw new InvalidOperationException("結果超過 10000 筆，請聯絡管理員。");
        return Workbook("拜訪型式", new { Scope = "全部已授權資料" },
            ["代碼", "名稱", "說明", "排序", "啟用"],
            rows.OrderBy(x => x.SortOrder).Select(x => Cells(x.VisitTypeCode, x.VisitTypeName,
                x.Description, x.SortOrder, x.IsActive ? "是" : "否")));
    }

    private static async Task<List<T>> GetAllAsync<T>(
        Func<int, Task<PagedResult<T>>> pageQuery, CancellationToken ct)
    {
        var result = new List<T>();
        var first = await pageQuery(1);
        if (first.TotalCount > MaxRows)
            throw new InvalidOperationException($"查詢結果 {first.TotalCount} 筆，超過匯出上限 {MaxRows} 筆，請縮小查詢條件。");
        result.AddRange(first.Items);
        for (int page = 2; result.Count < first.TotalCount; page++)
        {
            ct.ThrowIfCancellationRequested();
            if (page > (MaxRows + BatchSize - 1) / BatchSize + 1)
                throw new InvalidOperationException("匯出分頁異常，已停止下載。");
            var next = await pageQuery(page);
            if (next.Items.Count == 0) throw new InvalidOperationException("匯出過程資料已變動，請重新查詢。");
            result.AddRange(next.Items);
            if (result.Count > MaxRows) throw new InvalidOperationException("匯出筆數超過上限。");
        }
        return result;
    }

    private static string[] Cells(params object?[] values) =>
        values.Select(x => x switch {
            null => "",
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => Convert.ToString(x, CultureInfo.InvariantCulture) ?? ""
        }).ToArray();

    private IActionResult Workbook(string title, object filters, string[] headers, IEnumerable<string[]> records)
    {
        using var book = new XSSFWorkbook();
        var sheet = book.CreateSheet("查詢結果");
        var meta = book.CreateSheet("匯出資訊");
        WriteRow(meta, 0, ["功能", title]);
        WriteRow(meta, 1, ["匯出時間（台北）", DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss")]);
        WriteRow(meta, 2, ["查詢條件", JsonSerializer.Serialize(filters)]);
        WriteRow(meta, 3, ["資料範圍", "僅目前登入管理員經後端授權查詢可見的資料"]);
        WriteRow(meta, 4, ["最大筆數", MaxRows.ToString(CultureInfo.InvariantCulture)]);
        WriteRow(sheet, 0, headers);
        var rowNumber = 1;
        foreach (var values in records) WriteRow(sheet, rowNumber++, values);
        sheet.CreateFreezePane(0, 1);
        using var stream = new MemoryStream();
        book.Write(stream);
        var filename = $"fieldvisit_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return File(stream.ToArray(), Mime, filename);
    }

    private static void WriteRow(ISheet sheet, int rowNumber, string[] values)
    {
        var row = sheet.CreateRow(rowNumber);
        for (var col = 0; col < values.Length; col++)
        {
            // Set as text explicitly; preserve leading zeros and do not evaluate spreadsheet formulas.
            row.CreateCell(col, CellType.String).SetCellValue(values[col]);
        }
    }
}
