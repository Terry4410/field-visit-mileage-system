using System.Reflection;
using System.Text;
using FieldVisit.Api.Controllers;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class MultiFormatImportCompatibilityTests
{
    private static readonly MethodInfo NormalizeMethod =
        typeof(V160FinalController)
            .Assembly
            .GetType(
                "FieldVisit.Api.Controllers.ImportFileCompatibility",
                throwOnError: true)!
            .GetMethod(
                "NormalizeToXlsx",
                BindingFlags.Public |
                BindingFlags.Static)!
        ?? throw new InvalidOperationException(
            "NormalizeToXlsx not found.");

    private static byte[] Normalize(
        string fileName,
        byte[] content,
        string importKind)
    {
        return (byte[])NormalizeMethod.Invoke(
            null,
            new object[]
            {
                fileName,
                content,
                importKind
            })!;
    }

    private static XSSFWorkbook OpenXlsx(
        byte[] content)
    {
        return new XSSFWorkbook(
            new MemoryStream(content));
    }

    [Fact]
    public void Xlsx_Is_Byte_For_Byte_Passthrough()
    {
        var input = new byte[] { 1, 2, 3, 4, 5 };

        var result =
            Normalize(
                "test.xlsx",
                input,
                "locations");

        Assert.Same(input, result);
    }

    [Fact]
    public void Csv_Utf8_Preserves_Chinese_And_Blank_Cell()
    {
        var csv =
            """
LocationCode,TeamCode,LocationName,City,District,Address,PlusCode,Status
,TEAM-002,CSV中文測試,彰化縣,彰化市,彰化縣彰化市測試路1號,,Active
""";

        var result =
            Normalize(
                "locations.csv",
                Encoding.UTF8.GetBytes(csv),
                "locations");

        using var workbook =
            OpenXlsx(result);

        var row =
            workbook
                .GetSheet("Locations")
                .GetRow(1);

        Assert.Null(
            row.GetCell(
                0,
                MissingCellPolicy.RETURN_BLANK_AS_NULL));

        Assert.Equal(
            "CSV中文測試",
            row.GetCell(2).StringCellValue);
    }

    [Fact]
    public void Csv_Quoted_Comma_Is_Preserved()
    {
        var csv =
            """
LocationCode,TeamCode,LocationName,City,District,Address,PlusCode,Status
,TEAM-002,"測試,地點",彰化縣,彰化市,彰化縣彰化市測試路2號,,Active
""";

        var result =
            Normalize(
                "locations.csv",
                Encoding.UTF8.GetBytes(csv),
                "locations");

        using var workbook =
            OpenXlsx(result);

        Assert.Equal(
            "測試,地點",
            workbook
                .GetSheet("Locations")
                .GetRow(1)
                .GetCell(2)
                .StringCellValue);
    }

    [Fact]
    public void Csv_Big5_Preserves_Chinese()
    {
        Encoding.RegisterProvider(
            CodePagesEncodingProvider.Instance);

        var csv =
            """
LocationCode,TeamCode,LocationName,City,District,Address,PlusCode,Status
,TEAM-002,Big5中文測試,彰化縣,彰化市,彰化縣彰化市測試路3號,,Active
""";

        var bytes =
            Encoding
                .GetEncoding(950)
                .GetBytes(csv);

        var result =
            Normalize(
                "locations.csv",
                bytes,
                "locations");

        using var workbook =
            OpenXlsx(result);

        Assert.Equal(
            "Big5中文測試",
            workbook
                .GetSheet("Locations")
                .GetRow(1)
                .GetCell(2)
                .StringCellValue);
    }

    [Fact]
    public void Xls_Preserves_Missing_Blank_Cell()
    {
        using var source =
            new HSSFWorkbook();

        var sheet =
            source.CreateSheet("Locations");

        var header =
            sheet.CreateRow(0);

        header.CreateCell(0)
            .SetCellValue("LocationCode");

        header.CreateCell(1)
            .SetCellValue("TeamCode");

        header.CreateCell(2)
            .SetCellValue("LocationName");

        header.CreateCell(5)
            .SetCellValue("Address");

        var row =
            sheet.CreateRow(1);

        row.CreateCell(1)
            .SetCellValue("TEAM-002");

        row.CreateCell(2)
            .SetCellValue("XLS空白測試");

        row.CreateCell(5)
            .SetCellValue("彰化縣彰化市測試路4號");

        using var ms =
            new MemoryStream();

        source.Write(ms);

        var result =
            Normalize(
                "locations.xls",
                ms.ToArray(),
                "locations");

        using var workbook =
            OpenXlsx(result);

        var convertedRow =
            workbook
                .GetSheet("Locations")
                .GetRow(1);

        Assert.Null(
            convertedRow.GetCell(
                0,
                MissingCellPolicy.RETURN_BLANK_AS_NULL));

        Assert.Equal(
            "XLS空白測試",
            convertedRow.GetCell(2).StringCellValue);
    }

    [Fact]
    public void Legacy_Xls_Date_Is_Normalized_To_Iso()
    {
        using var source =
            new HSSFWorkbook();

        var sheet =
            source.CreateSheet("Projects");

        var header =
            sheet.CreateRow(0);

        header.CreateCell(0)
            .SetCellValue("ProjectCode");

        header.CreateCell(1)
            .SetCellValue("ProjectName");

        header.CreateCell(3)
            .SetCellValue("LocationMode");

        header.CreateCell(4)
            .SetCellValue("StartDate");

        header.CreateCell(6)
            .SetCellValue("Status");

        var row =
            sheet.CreateRow(1);

        row.CreateCell(0)
            .SetCellValue("P-DATE-001");

        row.CreateCell(1)
            .SetCellValue("日期測試");

        row.CreateCell(3)
            .SetCellValue("List");

        var dateCell =
            row.CreateCell(4);

        dateCell.SetCellValue(
            new DateTime(2026, 8, 20));

        var style =
            source.CreateCellStyle();

        style.DataFormat =
            source
                .CreateDataFormat()
                .GetFormat("m/d/yy");

        dateCell.CellStyle = style;

        row.CreateCell(6)
            .SetCellValue("Active");

        using var ms =
            new MemoryStream();

        source.Write(ms);

        var result =
            Normalize(
                "projects.xls",
                ms.ToArray(),
                "projects");

        using var workbook =
            OpenXlsx(result);

        Assert.Equal(
            "2026-08-20",
            workbook
                .GetSheet("Projects")
                .GetRow(1)
                .GetCell(4)
                .StringCellValue);
    }

    [Fact]
    public void ProjectLocation_Csv_Creates_Required_Sheets()
    {
        var csv =
            """
ProjectCode,LocationCode,Status
CARE-TEST,LOC-TEST,Active
""";

        var result =
            Normalize(
                "project-locations.csv",
                Encoding.UTF8.GetBytes(csv),
                "projects");

        using var workbook =
            OpenXlsx(result);

        Assert.NotNull(
            workbook.GetSheet("Projects"));

        Assert.NotNull(
            workbook.GetSheet("ProjectLocations"));
    }

    [Fact]
    public void Internal_People_Csv_Creates_Both_Required_Sheets()
    {
        var csv =
            """
UserCode,EmployeeNo,DisplayName,Email,EmploymentStatus,AdminEnabled,Roles,TeamCodes,PrimaryTeamCode,ChangeEffectiveFrom,IdentityProvider,EntraTenantId,EntraObjectId
user-test,E001,測試人員,test@example.com,Active,Y,visitor,TEAM-002,TEAM-002,2026-08-20,Local,,
""";

        var result =
            Normalize(
                "internal.csv",
                Encoding.UTF8.GetBytes(csv),
                "people");

        using var workbook =
            OpenXlsx(result);

        Assert.NotNull(
            workbook.GetSheet("InternalAuthorization"));

        Assert.NotNull(
            workbook.GetSheet("ExternalSupervisors"));
    }

    [Fact]
    public void External_People_Csv_Creates_Both_Required_Sheets()
    {
        var csv =
            """
UserCode,DisplayName,Email,ExternalOrganization,ExternalTitle,AuthorizationFrom,AuthorizationTo,AdminEnabled,ScopeType,ScopeTeamCodes,CanExportExcel,CanExportPdf,IdentityProvider,EntraTenantId,EntraObjectId,ChangeEffectiveFrom
,外部測試督導,test@example.com,測試機構,督導,2026-08-20,2026-12-31,Y,Team,TEAM-002,Y,Y,Local,,,2026-08-20
""";

        var result =
            Normalize(
                "external.csv",
                Encoding.UTF8.GetBytes(csv),
                "people");

        using var workbook =
            OpenXlsx(result);

        Assert.NotNull(
            workbook.GetSheet("InternalAuthorization"));

        Assert.NotNull(
            workbook.GetSheet("ExternalSupervisors"));
    }
}
