using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace FieldVisit.Api.Controllers;

// The Visitor/Leader export reuses both existing scoped reads. In particular,
// no Admin query-export permission is delegated to either role.
[ApiController]
[Authorize(Roles = "visitor,leader")]
[Route("api/v1/scoped-location-export")]
public sealed class V180ScopedLocationExportController(
    V160FinalService locations, V170LocationService notes) : ControllerBase
{
    public sealed record ScopedRow(ManagedLocationDto Location, string? LatestNote,
        string? NoteAuthor, DateTime? NoteAt);

    [HttpGet("locations")]
    public Task<IReadOnlyList<ScopedRow>> List([FromQuery] string? q, CancellationToken ct)
        => ReadAsync(q, ct);

    private async Task<IReadOnlyList<ScopedRow>> ReadAsync(string? q, CancellationToken ct)
    {
        if (q?.Length > 200) throw new InvalidOperationException("搜尋文字不可超過 200 字。");
        var rows = await locations.ManagedLocationsAsync(true, ct);
        if (rows.Count > 10000) throw new InvalidOperationException("地點超過 10000 筆，請縮小查詢範圍。");
        var data = new List<ScopedRow>();
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            V170LocationMaintenanceDto? detail = null;
            try { detail = await notes.GetMaintenanceAsync(row.LocationId, row.TeamId, ct); }
            catch (KeyNotFoundException) { /* No scoped note for this visible row. */ }
            catch (UnauthorizedAccessException) { /* Location visible; note scope denied. */ }
            var latest = detail?.Notes.FirstOrDefault();
            var searchable = string.Join(" ", new[] {row.LocationCode,row.LocationName,row.Address,
                row.TaxId,row.MasterNote}.Concat(detail?.Notes.Select(n => n.Note) ?? []));
            if (!string.IsNullOrWhiteSpace(q)
                && !searchable.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            data.Add(new(row, latest?.Note, latest?.ChangedBy, latest?.ChangedAt));
        }
        return data;
    }

    [HttpGet("locations.xlsx")]
    public async Task<IActionResult> Download([FromQuery] string? q, CancellationToken ct)
    {
        var data = await ReadAsync(q, ct);
        using var book = new XSSFWorkbook();
        var sheet = book.CreateSheet("授權地點清單");
        string[] headers = ["代碼","名稱","地址","Plus Code","統一編號","小組","主檔備註",
            "最近可見拜訪備註","備註作者","備註時間","審核狀態","啟用狀態"];
        static void Write(ISheet sheet,int index,IReadOnlyList<string> values)
        {
            var line = sheet.CreateRow(index);
            for(var i=0;i<values.Count;i++)line.CreateCell(i,CellType.String).SetCellValue(values[i]);
        }
        Write(sheet,0,headers);
        for(var i=0;i<data.Count;i++)
        {
            var x=data[i];var row=x.Location;
            Write(sheet,i+1,[row.LocationCode,row.LocationName,row.Address ?? "",row.PlusCode ?? "",
                row.TaxId ?? "",row.TeamName ?? "",row.MasterNote ?? "",x.LatestNote ?? "",
                x.NoteAuthor ?? "",x.NoteAt?.ToString("yyyy-MM-dd HH:mm") ?? "",
                row.ApprovalStatus,row.IsActive ? "啟用" : "停用"]);
        }
        sheet.CreateFreezePane(0,1);
        using var stream = new MemoryStream();
        book.Write(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"FieldVisit_授權地點清單_{DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)):yyyyMMdd_HHmmss}.xlsx");
    }
}
