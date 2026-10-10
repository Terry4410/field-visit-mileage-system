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

    private static Task<List<string>> InvokeAll(Func<int, Task<PagedResult<string>>> loader)
    {
        var method = typeof(V180QueryExportController)
            .GetMethod("GetAllAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(string));
        return (Task<List<string>>)method.Invoke(null, [loader, CancellationToken.None])!;
    }
}
