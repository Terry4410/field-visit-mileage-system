using System.Reflection;
using System.IO.Compression;
using FieldVisit.Api.Controllers;
using FieldVisit.Application;
using Microsoft.AspNetCore.Mvc;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180QueryExportTests
{
    [Fact]
    public async Task Workbook_has_filter_metadata_and_preserves_leading_zeros_as_text()
    {
        var controller = BuildController();
        var method = typeof(V180QueryExportController).GetMethod("Workbook", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = await InvokeWorkbook(method, controller,
            ["人事資料", new { Keyword = "test" }, new[] { "工號", "姓名", "Email" },
             new[] { new[] { "00123", "=HYPERLINK(\"http://evil\")", "synthetic@example.invalid" } }.AsEnumerable(), "全組織"]);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.ContentType);
        using var stream = new MemoryStream(result.FileContents);
        using var book = new XSSFWorkbook(stream);
        Assert.Equal(3, book.NumberOfSheets);
        Assert.Equal("業務查詢資料", book.GetSheetAt(0).SheetName);
        Assert.Equal("原始資料", book.GetSheetAt(1).SheetName);
        Assert.Equal("欄位對照表", book.GetSheetAt(2).SheetName);
        Assert.Equal("工號", book.GetSheet("業務查詢資料").GetRow(0).GetCell(0).StringCellValue);
        Assert.Equal("EmployeeNo", book.GetSheet("業務查詢資料").GetRow(1).GetCell(0).StringCellValue);
        Assert.Equal("00123", book.GetSheet("業務查詢資料").GetRow(2).GetCell(0).StringCellValue);
        var safeCell = book.GetSheet("業務查詢資料").GetRow(2).GetCell(1);
        Assert.Equal(CellType.String, safeCell.CellType);
        Assert.Equal("=HYPERLINK(\"http://evil\")", safeCell.StringCellValue);
        Assert.Equal("=HYPERLINK(\"http://evil\")", book.GetSheet("原始資料").GetRow(1).GetCell(1).StringCellValue);
        Assert.Contains("Users.Email", book.GetSheet("欄位對照表").GetRow(3).GetCell(2).StringCellValue);
    }

    [Fact]
    public async Task All_paged_results_are_included_not_just_first_screen()
    {
        var pages = new List<int>();
        Func<int, Task<PagedResult<string>>> loader = page =>
        {
            pages.Add(page);
            return Task.FromResult(page switch
            {
                1 => new PagedResult<string>(["A", "B"], 1, 2, 3),
                2 => new PagedResult<string>(["C"], 2, 2, 3),
                _ => throw new Exception("Unexpected page")
            });
        };
        var result = await InvokeAll(loader);
        Assert.Equal(new[] { 1, 2 }, pages);
        Assert.Equal(new[] { "A", "B", "C" }, result);
    }

    [Fact]
    public async Task Export_blocks_results_exceeding_configured_limit()
    {
        Func<int, Task<PagedResult<string>>> loader = page =>
            Task.FromResult(new PagedResult<string>(["A"], page, 100, 10001));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAll(loader));
        Assert.Contains("超過匯出上限", ex.Message);
    }


    [Fact]
    public void Personnel_column_picker_is_server_whitelisted_and_rejects_duplicates()
    {
        var method = typeof(V180QueryExportController)
            .GetMethod("ParsePersonnelColumns", BindingFlags.Static | BindingFlags.NonPublic)!;
        var selected = ((System.Collections.IEnumerable)method.Invoke(null, ["employeeNo,name,hireDate"])!).Cast<object>().ToList();
        Assert.Equal(3,selected.Count);
        foreach(var invalid in new[]{"employeeNo,secret","employeeNo,employeeNo"})
        {
            var error = Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,[invalid]));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    [Fact]
    public async Task Filename_is_distinct_and_sanitized_for_excel_download()
    {
        var controller = BuildController();
        var method = typeof(V180QueryExportController)
            .GetMethod("Workbook", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = await InvokeWorkbook(method, controller,
            ["小組", new { Keyword = "test" }, new[]{"小組代碼"},
             new[]{ new[]{"=1+1"} }.AsEnumerable(), "全組織"]);
        Assert.StartsWith("FieldVisit_小組_全組織_",result.FileDownloadName);
        Assert.EndsWith(".xlsx",result.FileDownloadName);
    }

    [Fact]
    public async Task Visit_type_export_denies_unclassified_scope_with_controlled_403()
    {
        var controller = BuildController();
        var result = await controller.VisitTypes(CancellationToken.None);
        var denied = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, denied.StatusCode);
        var body = System.Text.Json.JsonSerializer.Serialize(denied.Value);
        Assert.Contains("REPORT_EXPORT_VISIT_TYPES_SCOPE_HOLD", body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    [Fact]
    public void Personnel_and_official_exports_require_admin_role()
    {
        var auth = typeof(V180QueryExportController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().ToList();
        Assert.Contains(auth,attribute=>attribute.Roles=="admin");
        var endpoints = typeof(V180QueryExportController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(m=>m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpGetAttribute),true)
                .Cast<Microsoft.AspNetCore.Mvc.HttpGetAttribute>().Select(a=>a.Template)).ToList();
        Assert.Contains("personnel-full.xlsx",endpoints);
        Assert.Contains("centers.xlsx",endpoints);
        Assert.Contains("deployment-sites.xlsx",endpoints);
        Assert.Contains("locations-official.xlsx",endpoints);
    }

    [Fact]
    public async Task Single_sheet_workbook_includes_final_row_count_metadata()
    {
        var controller = BuildController();
        var method = typeof(V180QueryExportController)
            .GetMethod("Workbook", BindingFlags.Instance | BindingFlags.NonPublic)!;
        SetLegacy(controller);
        var output = await InvokeWorkbook(method, controller,
            ["人事資料", new { Keyword = "test" }, new[] { "工號" },
             new[] { new[] { "001" }, new[] { "002" } }.AsEnumerable(), "全組織"])!;
        using var data = new MemoryStream(output.FileContents);
        using var book = new XSSFWorkbook(data);
        Assert.Equal("符合查詢筆數", book.GetSheet("匯出資訊").GetRow(5).GetCell(0).StringCellValue);
        Assert.Equal("2", book.GetSheet("匯出資訊").GetRow(5).GetCell(1).StringCellValue);
    }

    [Fact]
    public async Task Bilingual_workbook_freezes_headers_and_adds_sortable_filters_to_authorized_rows()
    {
        var controller = BuildController();
        var method = typeof(V180QueryExportController).GetMethod("Workbook", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var output = await InvokeWorkbook(method, controller,
            ["小組", new { Keyword = "北" }, new[] { "小組代碼", "小組名稱", "成員人數" },
             new[] { new[] { "001", "北區", "3" } }.AsEnumerable(), "全組織"])!;
        using var stream = new MemoryStream(output.FileContents);
        using var book = new XSSFWorkbook(stream);
        var business = book.GetSheet("業務查詢資料");
        Assert.Equal(3, business.LastRowNum + 1);
        Assert.Equal("MemberCount", business.GetRow(1).GetCell(2).StringCellValue);
        Assert.Equal("001", business.GetRow(2).GetCell(0).StringCellValue);
        Assert.NotNull(business.GetRow(1).GetCell(0).CellStyle);
        using var packageStream = new MemoryStream();
        book.Write(packageStream);
        using var archive = new ZipArchive(new MemoryStream(packageStream.ToArray()), ZipArchiveMode.Read);
        using var worksheet = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        Assert.Contains("autoFilter", worksheet.ReadToEnd());
        Assert.Contains("Teams.TeamCode", book.GetSheet("欄位對照表").GetRow(1).GetCell(2).StringCellValue);
        Assert.Equal("001", book.GetSheet("原始資料").GetRow(1).GetCell(0).StringCellValue);
    }

    [Fact]
    public async Task Bilingual_writer_fails_closed_when_a_report_column_has_no_verified_mapping()
    {
        var method = typeof(V180QueryExportController).GetMethod("Workbook", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var controller = BuildController();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeWorkbook(method, controller,
            ["小組", new { }, new[] { "小組代碼", "未核定欄位" }, Array.Empty<string[]>().AsEnumerable(), "全組織"]));
        Assert.Contains("REPORT_EXPORT_COLUMN_MAP_MISSING", error.Message);
    }

    [Fact]
    public async Task Synthetic_bilingual_excel_samples_are_written_when_ci_artifact_directory_is_configured()
    {
        var directory = Environment.GetEnvironmentVariable("PACKAGE_A_SAMPLE_OUTPUT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var controller = BuildController();
        var method = typeof(V180QueryExportController).GetMethod("Workbook", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var samples = new[]
        {
            ("人事資料", new[] { "工號", "姓名", "Email" }, new[] { "SYNTH-001", "合成範例員工", "synthetic@example.invalid" }, "personnel-summary-synthetic.xlsx"),
            ("小組", new[] { "小組代碼", "小組名稱", "啟用", "成員人數" }, new[] { "SYNTH-T01", "合成北區小組", "是", "3" }, "teams-synthetic.xlsx")
        };
        foreach (var sample in samples)
        {
            var output = await InvokeWorkbook(method, controller,
                [sample.Item1, new { Synthetic = true }, sample.Item2, new[] { sample.Item3 }, "合成資料"]);
            await File.WriteAllBytesAsync(Path.Combine(directory, sample.Item4), output.FileContents);
        }
    }

    private static void SetLegacy(V180QueryExportController controller)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?legacy=true");
        controller.ControllerContext = new ControllerContext { HttpContext = context };
    }

    private static async Task<FileContentResult> InvokeWorkbook(MethodInfo method, V180QueryExportController controller, object?[] arguments)
    {
        var task = (Task<IActionResult>)method.Invoke(controller, arguments)!;
        return (FileContentResult)await task;
    }

    private static V180QueryExportController BuildController()
    {
        var repository = DispatchProxy.Create<IV160FinalRepository, ExportAuditProxy>();
        var controller = new V180QueryExportController(null!, repository, new ExportCurrentUser(), null!, null!, new ExportAuthorization());
        controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
        return controller;
    }

    public class ExportAuditProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IV160FinalRepository.AuditReportExportAsync)
                ? Task.CompletedTask
                : throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class ExportCurrentUser : ICurrentUserService
    {
        public CurrentUserDto GetRequired() => new(1, "T001", "Test", null, 1, null, null, ["admin"]);
    }

    private sealed class ExportAuthorization : IReportExportAuthorizationService
    {
        public Task<CurrentUserDto> AuthorizeAsync(IReadOnlyCollection<string> allowedRoles, string? format, CancellationToken ct) =>
            Task.FromResult(new CurrentUserDto(1, "T001", "Test", null, 1, null, null, ["admin"]));
    }

    [Fact]
    public void Personnel_history_period_boundaries_do_not_multiply_overlaps()
    {
        var nested = typeof(V180QueryExportController)
            .GetNestedType("PersonnelHistoryEvent", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(nested);
        var entries = (System.Collections.IList)Activator.CreateInstance(listType)!;
        object Create(string kind, DateOnly start, DateOnly? end) =>
            Activator.CreateInstance(nested, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, ["001", kind, "A", "Test", start, end, "42", null, null, 123L], null)!;
        entries.Add(Create("角色", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)));
        entries.Add(Create("小組", new DateOnly(2026, 2, 1), null));
        entries.Add(Create("派駐據點", new DateOnly(2026, 6, 1), null)); // Future assignment must not create an interval.
        var boundsMethod = typeof(V180QueryExportController)
            .GetMethod("BuildPeriodBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
        var bounds = (List<DateOnly>)boundsMethod.Invoke(null, [entries, new DateOnly(2026, 4, 30)])!;
        Assert.Equal(
            [new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1),
             new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1)],
            bounds);
    }

    [Fact]
    public void Period_boundaries_cover_inclusive_overlap_and_one_day_changes()
    {
        var nested = typeof(V180QueryExportController)
            .GetNestedType("PersonnelHistoryEvent", BindingFlags.NonPublic)!;
        var entries = (System.Collections.IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(nested))!;
        object Create(DateOnly start, DateOnly? end, long id) =>
            Activator.CreateInstance(nested,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, ["001", "角色", "visitor", "外訪員", start, end, "42", null, null, id], null)!;
        entries.Add(Create(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), 1));
        entries.Add(Create(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3), 2));
        entries.Add(Create(new DateOnly(2026, 1, 3), null, 3));
        entries.Add(Create(new DateOnly(2027, 1, 1), null, 4));
        var method = typeof(V180QueryExportController)
            .GetMethod("BuildPeriodBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
        var bounds = (List<DateOnly>)method.Invoke(null, [entries, new DateOnly(2026, 1, 5)])!;
        Assert.Equal(
            [new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2),
             new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 4),
             new DateOnly(2026, 1, 6)],
            bounds);
    }

    [Fact]
    public void History_audit_validation_rejects_duplicate_source_ids_and_backward_dates()
    {
        var eventType = typeof(V180QueryExportController)
            .GetNestedType("PersonnelHistoryEvent", BindingFlags.NonPublic)!;
        var list = (System.Collections.IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(eventType))!;
        object Create(string kind, long sourceId, DateOnly from, DateOnly? to) =>
            Activator.CreateInstance(eventType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, ["001", kind, "visitor", "外訪員", from, to, "42", null, null, sourceId], null)!;
        var valid = Create("角色", 10, new DateOnly(2026, 1, 1), null);
        list.Add(valid);
        var validator = typeof(V180QueryExportController)
            .GetMethod("ValidateHistorySourceIntegrity", BindingFlags.Static | BindingFlags.NonPublic)!;
        validator.Invoke(null, [list]);
        list.Add(Create("角色", 10, new DateOnly(2026, 2, 1), null));
        var duplicate = Assert.Throws<TargetInvocationException>(() => validator.Invoke(null, [list]));
        Assert.Contains("來源重複", duplicate.InnerException!.Message);
        list.RemoveAt(1);
        list.Add(Create("小組", 11, new DateOnly(2026, 4, 5), new DateOnly(2026, 4, 4)));
        var invalid = Assert.Throws<TargetInvocationException>(() => validator.Invoke(null, [list]));
        Assert.Contains("有效期間異常", invalid.InnerException!.Message);
    }

    [Fact]
    public async Task Five_sheet_workbook_roundtrips_without_lookup_and_preserves_source_records()
    {
        var controller = BuildController();
        var sheetType = typeof(V180QueryExportController)
            .GetNestedType("ExportSheet", BindingFlags.NonPublic)!;
        var list = (System.Collections.IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(sheetType))!;
        void Add(string title, string[] headers, params string[][] data)
        {
            var sheet = Activator.CreateInstance(sheetType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [title, headers, data.AsEnumerable()], null)!;
            list.Add(sheet);
        }
        Add("人員完整總覽", ["工號", "姓名", "目前角色"], ["00123", "測試甲", "外訪員、小組長"]);
        Add("人員歷史期間快照", ["工號", "期間起日", "期間迄日", "角色", "小組"],
            ["00123", "2026-01-01", "2026-01-31", "外訪員", "北區"],
            ["00123", "2026-02-01", "", "外訪員、小組長", "北區、南區"]);
        Add("人員異動事件明細", ["工號", "類型", "來源ID"],
            ["00123", "角色", "11"], ["00123", "小組", "21"], ["00123", "小組", "22"]);
        Add("完整稽核資料", ["來源類型", "來源ID", "工號"],
            ["角色", "11", "00123"], ["小組", "21", "00123"], ["小組", "22", "00123"]);
        var method = typeof(V180QueryExportController)
            .GetMethod("WorkbookSheets", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<IActionResult>)method.Invoke(controller,
            ["人事完整履歷", new { Scope = "全組織" }, list, "全組織"])!;
        var output = (FileContentResult)await task;
        using var stream = new MemoryStream(output.FileContents);
        using var workbook = new XSSFWorkbook(stream);
        Assert.Equal(5, workbook.NumberOfSheets);
        Assert.Equal("00123", workbook.GetSheet("人員完整總覽").GetRow(1).GetCell(0).StringCellValue);
        Assert.Equal(3, workbook.GetSheet("人員歷史期間快照").LastRowNum + 1);
        var events = workbook.GetSheet("人員異動事件明細");
        var audit = workbook.GetSheet("完整稽核資料");
        Assert.Equal(3, events.LastRowNum);
        Assert.Equal(events.LastRowNum, audit.LastRowNum);
        Assert.Equal("22", audit.GetRow(3).GetCell(1).StringCellValue);
        var info = workbook.GetSheet("報表資訊");
        Assert.Contains("人員歷史期間快照", Enumerable.Range(4, 4)
            .Select(i => info.GetRow(i).GetCell(0).StringCellValue));
    }

    private static Task<List<string>> InvokeAll(Func<int, Task<PagedResult<string>>> loader)
    {
        var method = typeof(V180QueryExportController)
            .GetMethod("GetAllAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(string));
        return (Task<List<string>>)method.Invoke(null, [loader, CancellationToken.None])!;
    }
}
