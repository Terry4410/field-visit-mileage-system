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
            AddWorkbookStyles(workbookPart);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            AddWorksheet(workbookPart, sheets, 1, "查詢條件", BuildConditionRows(request, user, rows.Count));
            AddWorksheet(workbookPart, sheets, 2, "行程彙總", BuildSummaryRows(rows));
            AddWorksheet(workbookPart, sheets, 3, "拜訪地點明細", BuildStopRows(rows));
            workbookPart.Workbook.Save();
        }
        var name = $"外訪行程查詢_{BusinessTime.Today:yyyyMMdd}.xlsx";
        return Task.FromResult(new ReportExportContext(name, stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    }


    // Mandatory QUERY-05 report: preserve a real TABLE, not individual cards.
    // Allow line wrapping and multipage rows instead of shrinking text to 7.2pt.
    public const float PdfBodyFontPt = 10.5f;
    private const float PdfLineHeight = 16f;
    private const float PdfTableCellPadding = 5f;
    private const float PdfHeaderHeight = 29f;

    private sealed record PdfTableFragment(
        int TripIndex,
        int Offset,
        int LineCount,
        IReadOnlyList<IReadOnlyList<string>> ColumnLines);

    public Task<ReportExportContext> CreatePdfAsync(IReadOnlyList<TripQueryRowDto> rows, TripQueryRequest request, CurrentUserDto user, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        using var document = SKDocument.CreatePdf(stream);
        var typeface = ResolveTypeface();
        using var titlePaint = new SKPaint { Typeface = typeface, TextSize = 18f, IsAntialias = true, Color = SKColors.Black };
        using var bodyPaint = new SKPaint { Typeface = typeface, TextSize = PdfBodyFontPt, IsAntialias = true, Color = new SKColor(30, 44, 61) };
        using var headingPaint = new SKPaint { Typeface = typeface, TextSize = PdfBodyFontPt, IsAntialias = true, FakeBoldText = true, Color = SKColors.White };
        using var boldPaint = new SKPaint { Typeface = typeface, TextSize = PdfBodyFontPt, IsAntialias = true, FakeBoldText = true, Color = new SKColor(30, 44, 61) };
        using var gridPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 0.5f, Color = new SKColor(190, 201, 214) };
        using var headerFill = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(38, 69, 100) };
        using var bandFill = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(246, 249, 252) };

        const float width = 841.89f, height = 595.28f; // A4 landscape, print-friendly
        const float left = 28f, right = 28f, top = 28f, bottom = 28f;
        const float footerReserve = 35f;
        // Eight grouped columns retain all 14 original data fields without
        // a microscopic font. Values within a cell may wrap over several lines.
        var columns = new[] { 90f, 132f, 92f, 104f, 132f, 108f, 75f, 52.89f };
        var headers = new[]
        {
            "日期／時間", "行程編號", "外訪員／小組", "專案／拜訪形式",
            "拜訪路線", "里程（km）", "費率／補助", "狀態"
        };
        var usableWidth = width - left - right;
        if (Math.Abs(columns.Sum() - usableWidth) > 0.1f)
            throw new InvalidOperationException("PDF_TABLE_WIDTH_INVALID");

        var generated = BusinessTime.Now;
        var filter = $"期間：{request.StartDate:yyyy/MM/dd} ～ {request.EndDate:yyyy/MM/dd}　匯出人：{user.DisplayName}　匯出時間：{generated:yyyy/MM/dd HH:mm}　總筆數：{rows.Count}";
        var filterLines = WrapPdfBand(filter, bodyPaint, usableWidth);
        var tableHeaderY = top + 30f + filterLines.Count * 15f + 16f;
        var bodyTop = tableHeaderY + PdfHeaderHeight;
        var footerTop = height - bottom - footerReserve;
        var availableHeight = footerTop - bodyTop;
        if (availableHeight < 4 * PdfLineHeight)
            throw new InvalidOperationException("PDF_TABLE_PAGE_AREA_INVALID");

        // Plan page breaks from wrapped cells. Every wrapped line must be drawn,
        // including exceptionally long routes spanning several pages.
        var pages = new List<List<PdfTableFragment>> { new() };
        var usedHeight = 0f;
        for (var tripIndex = 0; tripIndex < rows.Count; tripIndex++)
        {
            ct.ThrowIfCancellationRequested();
            var cells = PdfTableCells(rows[tripIndex]);
            var wrapped = columns.Select((columnWidth, index) =>
                WrapPdfBand(cells[index], bodyPaint, columnWidth - PdfTableCellPadding * 2)).ToArray();
            var totalLines = wrapped.Max(lines => lines.Count);
            var offset = 0;
            while (offset < totalLines)
            {
                var roomLines = (int)Math.Floor(
                    (availableHeight - usedHeight - PdfTableCellPadding * 2) / PdfLineHeight);
                var fullPageLines = (int)Math.Floor(
                    (availableHeight - PdfTableCellPadding * 2) / PdfLineHeight);
                var remainingLines = totalLines - offset;

                // Keep ordinary table rows intact; split only rows taller than
                // a fresh page. Don't leave one orphan line at the page bottom.
                if (usedHeight > 0 &&
                    (roomLines < 2 || (remainingLines > roomLines && remainingLines <= fullPageLines)))
                {
                    pages.Add(new());
                    usedHeight = 0f;
                    continue;
                }
                if (roomLines < 1)
                {
                    if (usedHeight == 0f)
                        throw new InvalidOperationException("PDF_TABLE_ROW_CANNOT_FIT");
                    pages.Add(new());
                    usedHeight = 0f;
                    continue;
                }
                var lineCount = Math.Min(remainingLines, roomLines);
                pages[^1].Add(new PdfTableFragment(tripIndex, offset, lineCount, wrapped));
                usedHeight += PdfTableCellPadding * 2 + lineCount * PdfLineHeight;
                offset += lineCount;
                if (offset < totalLines)
                {
                    pages.Add(new());
                    usedHeight = 0f;
                }
            }
        }

        var sumKm = rows.Sum(x => x.ApprovedDistanceKm ?? 0m);
        var sumAmount = rows.Sum(x => x.SubsidyAmount ?? 0m);
        for (var pageNumber = 0; pageNumber < pages.Count; pageNumber++)
        {
            using var canvas = document.BeginPage(width, height);
            canvas.DrawText("外訪行程與里程管理系統－行程查詢報表", left, top + 19f, titlePaint);
            for (var i = 0; i < filterLines.Count; i++)
                canvas.DrawText(filterLines[i], left, top + 36f + i * 15f + bodyPaint.TextSize, bodyPaint);

            // Repeat the visible table header on every printed page.
            var cursorX = left;
            for (var col = 0; col < columns.Length; col++)
            {
                var rect = new SKRect(cursorX, tableHeaderY, cursorX + columns[col], tableHeaderY + PdfHeaderHeight);
                canvas.DrawRect(rect, headerFill);
                canvas.DrawRect(rect, gridPaint);
                canvas.DrawText(headers[col], cursorX + PdfTableCellPadding,
                    tableHeaderY + 8f + headingPaint.TextSize, headingPaint);
                cursorX += columns[col];
            }

            var y = bodyTop;
            foreach (var fragment in pages[pageNumber])
            {
                var rowHeight = PdfTableCellPadding * 2 + fragment.LineCount * PdfLineHeight;
                cursorX = left;
                for (var col = 0; col < columns.Length; col++)
                {
                    var rect = new SKRect(cursorX, y, cursorX + columns[col], y + rowHeight);
                    if (fragment.TripIndex % 2 == 1) canvas.DrawRect(rect, bandFill);
                    canvas.DrawRect(rect, gridPaint);
                    var cellLines = fragment.ColumnLines[col];
                    for (var j = 0; j < fragment.LineCount; j++)
                    {
                        var sourceLine = fragment.Offset + j;
                        string? text = sourceLine < cellLines.Count ? cellLines[sourceLine] : null;
                        if (fragment.Offset > 0 && j == 0 && col == 0 && string.IsNullOrEmpty(text))
                            text = "續頁";
                        if (fragment.Offset > 0 && j == 0 && col == 1 && string.IsNullOrEmpty(text))
                            text = rows[fragment.TripIndex].TripNo;
                        if (string.IsNullOrEmpty(text)) continue;
                        var paint = col == 1 ? boldPaint : bodyPaint;
                        canvas.DrawText(text, cursorX + PdfTableCellPadding,
                            y + PdfTableCellPadding + paint.TextSize + j * PdfLineHeight, paint);
                    }
                    cursorX += columns[col];
                }
                y += rowHeight;
            }
            if (rows.Count == 0)
                canvas.DrawText("查無符合條件的行程。", left + PdfTableCellPadding, bodyTop + 22f, bodyPaint);

            canvas.DrawText($"核定總里程：{sumKm:0.##} km　補助總金額：NT$ {sumAmount:0.00}",
                left, height - bottom - 10f, boldPaint);
            canvas.DrawText($"第 {pageNumber + 1} / {pages.Count} 頁",
                width - right - 105f, height - bottom - 10f, bodyPaint);
            document.EndPage();
        }

        document.Close();
        var name = $"外訪行程查詢_{BusinessTime.Today:yyyyMMdd}.pdf";
        return Task.FromResult(new ReportExportContext(name, stream.ToArray(), "application/pdf"));
    }

    private static string[] PdfTableCells(TripQueryRowDto trip) =>
    [
        $"{trip.VisitDate:yyyy/MM/dd}\n{TimeRange(trip.StartTime, trip.EndTime)}",
        trip.TripNo,
        $"{trip.VisitorName}\n{trip.TeamName ?? "—"}",
        $"{trip.ProjectNames}\n{trip.VisitTypeNames}",
        trip.Route,
        $"自算：{Km(trip.ClaimedDistanceKm)}\n系統：{Km(trip.SystemDistanceKm)}\n核定：{Km(trip.ApprovedDistanceKm)}",
        $"費率：{Money(trip.RatePerKmSnapshot)}\n補助：{Money(trip.SubsidyAmount)}",
        ReportStatusName(trip.Status, trip.StatusName)
    ];

    // Preserve whole words and TripNos when they fit in their column.
    // The user accepts wrapping; overlong tokens break at Unicode text elements.
    private static IReadOnlyList<string> WrapPdfBand(string value, SKPaint paint, float maxWidth)
    {
        var result = new List<string>();
        foreach (var paragraph in (value ?? "").Replace("\r", "").Split('\n'))
        {
            var current = "";
            foreach (System.Text.RegularExpressions.Match match in
                System.Text.RegularExpressions.Regex.Matches(paragraph, @"\S+\s*"))
            {
                var token = match.Value;
                if (paint.MeasureText((current + token).TrimEnd()) <= maxWidth)
                {
                    current += token;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(current))
                {
                    result.Add(current.TrimEnd());
                    current = "";
                }
                if (paint.MeasureText(token.Trim()) <= maxWidth)
                {
                    current = token.TrimStart();
                    continue;
                }
                var units = System.Globalization.StringInfo.GetTextElementEnumerator(token.Trim());
                while (units.MoveNext())
                {
                    var unit = (string)units.Current!;
                    if (current.Length > 0 && paint.MeasureText(current + unit) > maxWidth)
                    {
                        result.Add(current.TrimEnd());
                        current = "";
                    }
                    current += unit;
                }
            }
            if (!string.IsNullOrWhiteSpace(current)) result.Add(current.TrimEnd());
            else if (paragraph.Length == 0) result.Add("");
        }
        return result.Count > 0 ? result : new[] { "" };
    }

    // Cache the 19 MB font once. Hosting platforms may mount the API ZIP
    // without exposing fonts/ under AppContext.BaseDirectory, despite the ZIP
    // containing the file. Embedding in the published DLL avoids that path risk.
    // The font is supplied from the existing source-verified packaging artifact.
    private static readonly Lazy<SKTypeface?> EmbeddedCjkTypeface = new(() =>
    {
        using var resource = typeof(ReportDocumentService).Assembly
            .GetManifestResourceStream("FieldVisit.Pdf.NotoSansCJK-Regular.ttc");
        if (resource is null) return null;
        using var bytes = new MemoryStream();
        resource.CopyTo(bytes);
        using var data = SKData.CreateCopy(bytes.ToArray());
        // Exact bundled NotoSansCJK-Regular.ttc has TC (Traditional Chinese)
        // as face index 3; the default index 0 uses Japanese glyph variants.
        return SKTypeface.FromData(data, 3);
    });

    // A generic Arial/DejaVu fallback silently yields missing-glyph squares.
    // Never deliver such a PDF and label it successful.
    private static bool SupportsReportChinese(SKTypeface? font) =>
        font is not null &&
        font.ContainsGlyphs("外訪行程查詢報表小組專案里程核定費率補助日期時間狀態");

    private SKTypeface ResolveTypeface()
    {
        var configured = configuration["Report:PdfFontPath"];
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            var chosen = SKTypeface.FromFile(configured);
            if (SupportsReportChinese(chosen)) return chosen;
        }

        // This is the primary source for production binaries. It is part of
        // FieldVisit.Infrastructure.dll and survives API Run-From-Package.
        if (EmbeddedCjkTypeface.Value is { } embedded &&
            SupportsReportChinese(embedded))
            return embedded;

        foreach (var candidate in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "fonts", "NotoSansCJK-Regular.ttc"),
            "/home/site/wwwroot/fonts/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJKtc-Regular.otf",
            "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
            @"C:\Windows\Fonts\msjh.ttc"
        })
        {
            if (!File.Exists(candidate)) continue;
            var fromFile = SKTypeface.FromFile(candidate);
            if (SupportsReportChinese(fromFile)) return fromFile;
        }
        foreach (var family in new[] {
            "Microsoft JhengHei", "Noto Sans CJK TC", "Noto Sans CJK",
            "Noto Sans TC", "PingFang TC", "Arial Unicode MS" })
        {
            var font = SKTypeface.FromFamilyName(family);
            if (SupportsReportChinese(font)) return font;
        }
        throw new InvalidOperationException("PDF_CJK_FONT_MISSING_OR_UNSUPPORTED");
    }

    private static float MeasureWrappedRowHeight(float[] widths, string[] cells, SKPaint textPaint, float minimumHeight)
    {
        var lineHeight = textPaint.TextSize + 2.8f;
        var maxLines = 1;

        for (var i = 0; i < widths.Length; i++)
        {
            var lines = WrapText(cells.ElementAtOrDefault(i) ?? "", textPaint, Math.Max(8f, widths[i] - 6f));
            maxLines = Math.Max(maxLines, lines.Count);
        }

        return Math.Max(minimumHeight, maxLines * lineHeight + 7f);
    }

    private static void DrawWrappedTableRow(SKCanvas canvas, float x, float y, float[] widths, string[] cells, float height, SKPaint textPaint, SKPaint linePaint)
    {
        var cursor = x;
        var lineHeight = textPaint.TextSize + 2.8f;

        for (var i = 0; i < widths.Length; i++)
        {
            var rect = new SKRect(cursor, y, cursor + widths[i], y + height);
            canvas.DrawRect(rect, linePaint);
            canvas.Save();
            canvas.ClipRect(new SKRect(rect.Left + 2, rect.Top + 2, rect.Right - 2, rect.Bottom - 2));

            var lines = WrapText(cells.ElementAtOrDefault(i) ?? "", textPaint, Math.Max(8f, widths[i] - 6f));
            var baseline = rect.Top + 3f + textPaint.TextSize;

            foreach (var line in lines)
            {
                if (baseline > rect.Bottom - 2f)
                    break;

                canvas.DrawText(line, rect.Left + 3f, baseline, textPaint);
                baseline += lineHeight;
            }

            canvas.Restore();
            cursor += widths[i];
        }
    }

    private static IReadOnlyList<string> WrapText(string text, SKPaint paint, float maxWidth)
    {
        if (string.IsNullOrEmpty(text))
            return new[] { "" };

        var result = new List<string>();
        var paragraphs = text.Replace("\r", "").Split('\n');

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length == 0)
            {
                result.Add("");
                continue;
            }

            var current = "";
            foreach (var ch in paragraph)
            {
                var candidate = current + ch;
                if (current.Length > 0 && paint.MeasureText(candidate) > maxWidth)
                {
                    result.Add(current.TrimEnd());
                    current = ch.ToString().TrimStart();
                }
                else
                {
                    current = candidate;
                }
            }

            if (current.Length > 0)
                result.Add(current.TrimEnd());
        }

        return result.Count == 0 ? new[] { "" } : result;
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
        yield return new object?[] { "狀態", ReportStatusName(request.Status, request.Status) };
        yield return new object?[] { "匯出人", $"{user.EmployeeNo} {user.DisplayName}" };
        yield return new object?[] { "匯出時間（Asia/Taipei）", BusinessTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
        yield return new object?[] { "資料筆數", count };
    }

    private static IEnumerable<IReadOnlyList<object?>> BuildSummaryRows(IReadOnlyList<TripQueryRowDto> rows)
    {
        yield return new object?[] { "日期", "起始時", "起始分", "結束時", "結束分", "TripNo", "外訪員", "員編", "小組", "專案", "拜訪形式", "拜訪順序", "自算里程", "系統里程", "核定里程", "每公里補助", "補助金額", "狀態", "Snapshot版本", "更正狀態", "備註" };
        foreach (var r in rows) yield return new object?[] { r.VisitDate.ToString("yyyy-MM-dd"), r.StartTime?.Hour, r.StartTime?.Minute, r.EndTime?.Hour, r.EndTime?.Minute, r.TripNo, r.VisitorName, r.EmployeeNo, r.TeamName, r.ProjectNames, r.VisitTypeNames, r.Route, r.ClaimedDistanceKm, r.SystemDistanceKm, r.ApprovedDistanceKm, r.RatePerKmSnapshot, r.SubsidyAmount, ReportStatusName(r.Status, r.StatusName), r.SnapshotVersion, r.CorrectionStatus, r.Notes };
    }

    private static IEnumerable<IReadOnlyList<object?>> BuildStopRows(IReadOnlyList<TripQueryRowDto> rows)
    {
        yield return new object?[] { "日期", "起始時", "起始分", "結束時", "結束分", "TripNo", "外訪員", "小組", "順序", "地點代碼", "地點名稱", "地址", "專案代碼", "專案名稱", "拜訪形式代碼", "拜訪形式", "行程目的", "備註" };
        foreach (var r in rows)
            foreach (var s in r.Stops.OrderBy(x => x.StopSequence))
                yield return new object?[] { r.VisitDate.ToString("yyyy-MM-dd"), r.StartTime?.Hour, r.StartTime?.Minute, r.EndTime?.Hour, r.EndTime?.Minute, r.TripNo, r.VisitorName, r.TeamName, s.StopSequence, s.LocationCode, s.LocationName, s.Address, s.ProjectCode, s.ProjectName, s.VisitTypeCode, s.VisitTypeName, s.VisitPurpose, s.Notes };
    }

    private static void AddWorksheet(WorkbookPart workbookPart, Sheets sheets, uint sheetId, string name, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        var materialized = rows.ToList();

        var twoDigitColumns = new HashSet<int>();
        if (materialized.Count > 0)
        {
            var header = materialized[0];
            for (var i = 0; i < header.Count; i++)
            {
                var label = header[i]?.ToString();
                if (label is "起始時" or "起始分" or "結束時" or "結束分")
                    twoDigitColumns.Add(i);
            }
        }

        for (var rowIndex = 0; rowIndex < materialized.Count; rowIndex++)
        {
            var values = materialized[rowIndex];
            var row = new Row();

            for (var columnIndex = 0; columnIndex < values.Count; columnIndex++)
            {
                var useTwoDigitStyle =
                    rowIndex > 0
                    && twoDigitColumns.Contains(columnIndex);

                row.Append(
                    ToCell(
                        values[columnIndex],
                        useTwoDigitStyle ? 1U : null));
            }

            sheetData.Append(row);
        }

        worksheetPart.Worksheet = new Worksheet(sheetData);
        worksheetPart.Worksheet.Save();
        sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId, Name = name });
    }

    private static void AddWorkbookStyles(WorkbookPart workbookPart)
    {
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();

        var numberingFormats = new NumberingFormats(
            new NumberingFormat
            {
                NumberFormatId = 164U,
                FormatCode = "00"
            })
        {
            Count = 1U
        };

        var fonts = new Fonts(new Font()) { Count = 1U };

        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
        {
            Count = 2U
        };

        var borders = new Borders(new Border()) { Count = 1U };

        var cellStyleFormats = new CellStyleFormats(
            new CellFormat())
        {
            Count = 1U
        };

        var cellFormats = new CellFormats(
            new CellFormat(),
            new CellFormat
            {
                NumberFormatId = 164U,
                ApplyNumberFormat = true
            })
        {
            Count = 2U
        };

        stylesPart.Stylesheet = new Stylesheet(
            numberingFormats,
            fonts,
            fills,
            borders,
            cellStyleFormats,
            cellFormats);

        stylesPart.Stylesheet.Save();
    }

    private static Cell ToCell(object? value, uint? styleIndex = null)
    {
        Cell cell;

        if (value is null)
        {
            cell = new Cell
            {
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(""))
            };
        }
        else if (value is byte or short or int or long or float or double or decimal)
        {
            cell = new Cell
            {
                DataType = CellValues.Number,
                CellValue = new CellValue(
                    Convert.ToString(
                        value,
                        System.Globalization.CultureInfo.InvariantCulture))
            };
        }
        else if (value is bool b)
        {
            cell = new Cell
            {
                DataType = CellValues.Boolean,
                CellValue = new CellValue(b ? "1" : "0")
            };
        }
        else
        {
            cell = new Cell
            {
                DataType = CellValues.InlineString,
                InlineString = new InlineString(
                    new Text(value.ToString() ?? ""))
            };
        }

        if (styleIndex.HasValue)
            cell.StyleIndex = styleIndex.Value;

        return cell;
    }

    private static string TimeRange(TimeOnly? start, TimeOnly? end)
    {
        var s = start.HasValue
            ? $"{start.Value.Hour:00}:{start.Value.Minute:00}"
            : null;

        var e = end.HasValue
            ? $"{end.Value.Hour:00}:{end.Value.Minute:00}"
            : null;

        if (s is not null && e is not null) return $"{s}～{e}";
        if (s is not null) return $"{s}～—";
        if (e is not null) return $"—～{e}";
        return "—";
    }

    internal static string? ReportStatusName(string? status, string? statusName) =>
        string.Equals(status, "Draft", StringComparison.OrdinalIgnoreCase)
            ? "預排行程"
            : statusName;

    private static string Km(decimal? value) => value.HasValue ? value.Value.ToString("0.##") : "N/A";
    private static string Money(decimal? value) => value.HasValue ? $"${value.Value:0.00}" : "N/A";
    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..Math.Max(0, max - 1)] + "…";
}
