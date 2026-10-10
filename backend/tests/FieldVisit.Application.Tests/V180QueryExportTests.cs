using System.Reflection;
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
    public void Workbook_has_filter_metadata_and_preserves_leading_zeros_as_text()
    {
        var controller = new V180QueryExportController(null!, null!, null!, null!, null!, null!);
        var method = typeof(V180QueryExportController).GetMethod("Workbook", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = (FileContentResult)method.Invoke(controller,
            ["人事資料", new { Keyword = "test" }, new[] { "工號", "姓名" },
             new[] { new[] { "00123", "=HYPERLINK(\"http://evil\")" } }.AsEnumerable(), "全組織"])!;
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.ContentType);
        using var stream = new MemoryStream(result.FileContents);
        using var book = new XSSFWorkbook(stream);
        Assert.Equal("工號", book.GetSheet("查詢結果").GetRow(0).GetCell(0).StringCellValue);
        Assert.Equal("00123", book.GetSheet("查詢結果").GetRow(1).GetCell(0).StringCellValue);
        var safeCell = book.GetSheet("查詢結果").GetRow(1).GetCell(1);
        Assert.Equal(CellType.String, safeCell.CellType);
        Assert.Equal("=HYPERLINK(\"http://evil\")", safeCell.StringCellValue);
        Assert.Contains("test", book.GetSheet("匯出資訊").GetRow(2).GetCell(1).StringCellValue);
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
    public void Filename_is_distinct_and_sanitized_for_excel_download()
    {
        var controller = new V180QueryExportController(null!,null!,null!,null!,null!,null!);
        var method = typeof(V180QueryExportController)
            .GetMethod("Workbook", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = (FileContentResult)method.Invoke(controller,
            ["人事完整履歷/2026", new { Keyword = "test" }, new[]{"欄位"},
             new[]{ new[]{"=1+1"} }.AsEnumerable(), "全組織"])!;
        Assert.StartsWith("FieldVisit_人事完整履歷_2026_全組織_",result.FileDownloadName);
        Assert.EndsWith(".xlsx",result.FileDownloadName);
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
    public void Single_sheet_workbook_includes_final_row_count_metadata()
    {
        var controller = new V180QueryExportController(null!, null!, null!, null!, null!, null!);
        var method = typeof(V180QueryExportController)
            .GetMethod("Workbook", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var output = (FileContentResult)method.Invoke(controller,
            ["人事資料", new { Keyword = "test" }, new[] { "工號" },
             new[] { new[] { "001" }, new[] { "002" } }.AsEnumerable(), "全組織"])!;
        using var data = new MemoryStream(output.FileContents);
        using var book = new XSSFWorkbook(data);
        Assert.Equal("符合查詢筆數", book.GetSheet("匯出資訊").GetRow(5).GetCell(0).StringCellValue);
        Assert.Equal("2", book.GetSheet("匯出資訊").GetRow(5).GetCell(1).StringCellValue);
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
    public void Five_sheet_workbook_roundtrips_without_lookup_and_preserves_source_records()
    {
        var controller = new V180QueryExportController(null!, null!, null!, null!, null!, null!);
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
        var output = (FileContentResult)method.Invoke(controller,
            ["人事完整履歷", new { Scope = "全組織" }, list, "全組織"])!;
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
