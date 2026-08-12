using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FieldVisit.Application;
using Microsoft.Extensions.Configuration;
using SkiaSharp;

namespace FieldVisit.Infrastructure;

public sealed class ReportDocumentService(IConfiguration configuration) : IReportDocumentService
{
    public Task<ReportExportContext> CreateExcelAsync(IReadOnlyList<TripQueryRowDto> rows, TripQueryRequest request, CurrentUserDto user, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            AddWorksheet(workbookPart, sheets, 1, "查詢條件", BuildConditionRows(request, user, rows.Count));
            AddWorksheet(workbookPart, sheets, 2, "行程彙總", BuildSummaryRows(rows));
            AddWorksheet(workbookPart, sheets, 3, "拜訪地點明細", BuildStopRows(rows));
            workbookPart.Workbook.Save();
        }
        var name = $"外訪行程查詢_{BusinessTime.Today:yyyyMMdd}.xlsx";
        return Task.FromResult(new ReportExportContext(name, stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    }

    public Task<ReportExportContext> CreatePdfAsync(IReadOnlyList<TripQueryRowDto> rows, TripQueryRequest request, CurrentUserDto user, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        using var document = SKDocument.CreatePdf(stream);
        var typeface = ResolveTypeface();
        var titlePaint = new SKPaint { Typeface = typeface, TextSize = 18, IsAntialias = true, Color = SKColors.Black };
        var textPaint = new SKPaint { Typeface = typeface, TextSize = 7.2f, IsAntialias = true, Color = SKColors.Black };
        var headerPaint = new SKPaint { Typeface = typeface, TextSize = 7.2f, IsAntialias = true, FakeBoldText = true, Color = SKColors.Black };
        var linePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 0.6f, Color = SKColors.Gray };
        const float width = 841.89f, height = 595.28f; // A4 landscape points
        const float left = 28, right = 28, top = 28, bottom = 28;
        var columns = new[] { 68f, 52f, 55f, 45f, 60f, 55f, 150f, 38f, 38f, 38f, 42f, 50f, 50f };
        var headers = new[] { "TripNo", "日期", "外訪員", "小組", "專案", "拜訪形式", "拜訪路線", "自算", "系統", "核定", "費率", "補助金額", "狀態" };
        var rowHeight = 22f;
        var pageNo = 0;
        var index = 0;
        do
        {
            pageNo++;
            using var canvas = document.BeginPage(width, height);
            var y = top;
            canvas.DrawText("外訪行程與里程管理系統－行程查詢報表", left, y + 16, titlePaint);
            y += 30;
            var filter = $"期間：{request.StartDate:yyyy/MM/dd} ～ {request.EndDate:yyyy/MM/dd}　匯出人：{user.DisplayName}　匯出時間：{BusinessTime.Now:yyyy/MM/dd HH:mm}　總筆數：{rows.Count}";
            canvas.DrawText(filter, left, y + 10, textPaint);
            y += 22;
            DrawTableRow(canvas, left, y, columns, headers, rowHeight, headerPaint, linePaint);
            y += rowHeight;
            var maxRows = Math.Max(1, (int)((height - bottom - y - 32) / rowHeight));
            for (var i = 0; i < maxRows && index < rows.Count; i++, index++)
            {
                var r = rows[index];
                var cells = new[]
                {
                    r.TripNo, r.VisitDate.ToString("yyyy/MM/dd"), r.VisitorName, r.TeamName ?? "—", Truncate(r.ProjectNames, 12),
                    Truncate(r.VisitTypeNames, 12), Truncate(r.Route, 30), Km(r.ClaimedDistanceKm), Km(r.SystemDistanceKm),
                    Km(r.ApprovedDistanceKm), Money(r.RatePerKmSnapshot), Money(r.SubsidyAmount), r.StatusName
                };
                DrawTableRow(canvas, left, y, columns, cells, rowHeight, textPaint, linePaint);
                y += rowHeight;
            }
            var sumKm = rows.Sum(x => x.ApprovedDistanceKm ?? 0m);
            var sumAmount = rows.Sum(x => x.SubsidyAmount ?? 0m);
            canvas.DrawText($"核定總里程：{sumKm:0.##} km　補助總金額：${sumAmount:0.00}", left, height - bottom - 12, headerPaint);
            canvas.DrawText($"第 {pageNo} 頁", width - right - 45, height - bottom - 12, textPaint);
            document.EndPage();
        } while (index < rows.Count || pageNo == 0);
        document.Close();
        var name = $"外訪行程查詢_{BusinessTime.Today:yyyyMMdd}.pdf";
        return Task.FromResult(new ReportExportContext(name, stream.ToArray(), "application/pdf"));
    }

    private SKTypeface ResolveTypeface()
    {
        var configured = configuration["Report:PdfFontPath"];
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            var fromFile = SKTypeface.FromFile(configured);
            if (fromFile is not null) return fromFile;
        }
        foreach (var candidate in new[]
        {
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJKtc-Regular.otf",
            "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJKtc-Regular.otf",
            @"C:\Windows\Fonts\msjh.ttc"
        })
        {
            if (!File.Exists(candidate)) continue;
            var fromCommonPath = SKTypeface.FromFile(candidate);
            if (fromCommonPath is not null) return fromCommonPath;
        }
        foreach (var family in new[] { "Microsoft JhengHei", "Noto Sans CJK TC", "Noto Sans CJK", "Noto Sans TC", "PingFang TC", "Arial Unicode MS", "Arial" })
        {
            var font = SKTypeface.FromFamilyName(family);
            if (font is not null) return font;
        }
        return SKTypeface.Default;
    }

    private static void DrawTableRow(SKCanvas canvas, float x, float y, float[] widths, string[] cells, float height, SKPaint textPaint, SKPaint linePaint)
    {
        var cursor = x;
        for (var i = 0; i < widths.Length; i++)
        {
            var rect = new SKRect(cursor, y, cursor + widths[i], y + height);
            canvas.DrawRect(rect, linePaint);
            canvas.Save();
            canvas.ClipRect(new SKRect(rect.Left + 2, rect.Top + 2, rect.Right - 2, rect.Bottom - 2));
            canvas.DrawText(cells.ElementAtOrDefault(i) ?? "", rect.Left + 3, rect.Top + 14, textPaint);
            canvas.Restore();
            cursor += widths[i];
        }
    }

    private static IEnumerable<IReadOnlyList<object?>> BuildConditionRows(TripQueryRequest request, CurrentUserDto user, int count)
    {
        yield return new object?[] { "條件", "內容" };
        yield return new object?[] { "開始日期", request.StartDate?.ToString("yyyy-MM-dd") };
        yield return new object?[] { "結束日期", request.EndDate?.ToString("yyyy-MM-dd") };
        yield return new object?[] { "小組ID", request.TeamId };
        yield return new object?[] { "外訪員ID", request.VisitorId };
        yield return new object?[] { "地點關鍵字", request.LocationKeyword };
        yield return new object?[] { "專案ID", request.ProjectId };
        yield return new object?[] { "拜訪形式ID", request.VisitTypeId };
        yield return new object?[] { "狀態", request.Status };
        yield return new object?[] { "匯出人", $"{user.EmployeeNo} {user.DisplayName}" };
        yield return new object?[] { "匯出時間（Asia/Taipei）", BusinessTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
        yield return new object?[] { "資料筆數", count };
    }

    private static IEnumerable<IReadOnlyList<object?>> BuildSummaryRows(IReadOnlyList<TripQueryRowDto> rows)
    {
        yield return new object?[] { "日期", "TripNo", "外訪員", "員編", "小組", "專案", "拜訪形式", "拜訪順序", "自算里程", "系統里程", "核定里程", "每公里補助", "補助金額", "狀態", "Snapshot版本", "更正狀態", "備註" };
        foreach (var r in rows) yield return new object?[] { r.VisitDate.ToString("yyyy-MM-dd"), r.TripNo, r.VisitorName, r.EmployeeNo, r.TeamName, r.ProjectNames, r.VisitTypeNames, r.Route, r.ClaimedDistanceKm, r.SystemDistanceKm, r.ApprovedDistanceKm, r.RatePerKmSnapshot, r.SubsidyAmount, r.StatusName, r.SnapshotVersion, r.CorrectionStatus, r.Notes };
    }

    private static IEnumerable<IReadOnlyList<object?>> BuildStopRows(IReadOnlyList<TripQueryRowDto> rows)
    {
        yield return new object?[] { "日期", "TripNo", "外訪員", "小組", "順序", "地點代碼", "地點名稱", "地址", "專案代碼", "專案名稱", "拜訪形式代碼", "拜訪形式", "行程目的", "備註" };
        foreach (var r in rows)
            foreach (var s in r.Stops.OrderBy(x => x.StopSequence))
                yield return new object?[] { r.VisitDate.ToString("yyyy-MM-dd"), r.TripNo, r.VisitorName, r.TeamName, s.StopSequence, s.LocationCode, s.LocationName, s.Address, s.ProjectCode, s.ProjectName, s.VisitTypeCode, s.VisitTypeName, s.VisitPurpose, s.Notes };
    }

    private static void AddWorksheet(WorkbookPart workbookPart, Sheets sheets, uint sheetId, string name, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        foreach (var values in rows)
        {
            var row = new Row();
            foreach (var value in values) row.Append(ToCell(value));
            sheetData.Append(row);
        }
        worksheetPart.Worksheet = new Worksheet(sheetData);
        worksheetPart.Worksheet.Save();
        sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId, Name = name });
    }

    private static Cell ToCell(object? value)
    {
        if (value is null) return new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text("")) };
        if (value is byte or short or int or long or float or double or decimal)
            return new Cell { DataType = CellValues.Number, CellValue = new CellValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)) };
        if (value is bool b) return new Cell { DataType = CellValues.Boolean, CellValue = new CellValue(b ? "1" : "0") };
        return new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value.ToString() ?? "")) };
    }

    private static string Km(decimal? value) => value.HasValue ? value.Value.ToString("0.##") : "N/A";
    private static string Money(decimal? value) => value.HasValue ? $"${value.Value:0.00}" : "N/A";
    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..Math.Max(0, max - 1)] + "…";
}
