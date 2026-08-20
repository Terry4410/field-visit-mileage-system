using System.Globalization;
using System.Text;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace FieldVisit.Api.Controllers;

internal static class ImportFileCompatibility
{
    private static readonly string[] ProjectHeaders =
    [
        "ProjectCode",
        "ProjectName",
        "TeamCode",
        "LocationMode",
        "StartDate",
        "EndDate",
        "Status",
        "Description"
    ];

    private static readonly string[] ProjectLocationHeaders =
    [
        "ProjectCode",
        "LocationCode",
        "Status"
    ];

    private static readonly string[] InternalAuthorizationHeaders =
    [
        "UserCode",
        "EmployeeNo",
        "DisplayName",
        "Email",
        "EmploymentStatus",
        "AdminEnabled",
        "Roles",
        "TeamCodes",
        "PrimaryTeamCode",
        "ChangeEffectiveFrom",
        "IdentityProvider",
        "EntraTenantId",
        "EntraObjectId"
    ];

    private static readonly string[] ExternalSupervisorHeaders =
    [
        "UserCode",
        "DisplayName",
        "Email",
        "ExternalOrganization",
        "ExternalTitle",
        "AuthorizationFrom",
        "AuthorizationTo",
        "AdminEnabled",
        "ScopeType",
        "ScopeTeamCodes",
        "CanExportExcel",
        "CanExportPdf",
        "IdentityProvider",
        "EntraTenantId",
        "EntraObjectId",
        "ChangeEffectiveFrom"
    ];

    public static bool IsSupported(string fileName)
    {
        var extension =
            Path.GetExtension(fileName)
                .ToLowerInvariant();

        return extension is
            ".xlsx"
            or ".xls"
            or ".csv";
    }

    public static byte[] NormalizeToXlsx(
        string fileName,
        byte[] content,
        string importKind)
    {
        if (content.Length == 0)
        {
            throw new InvalidOperationException(
                "上傳檔案為空。");
        }

        var extension =
            Path.GetExtension(fileName)
                .ToLowerInvariant();

        return extension switch
        {
            // Critical compatibility rule:
            // existing XLSX files are NOT converted or rewritten.
            ".xlsx" => content,

            ".xls" =>
                ConvertLegacyExcelToXlsx(
                    content),

            ".csv" =>
                ConvertCsvToXlsx(
                    content,
                    importKind),

            _ =>
                throw new InvalidOperationException(
                    "只支援 .xlsx、.xls、.csv 檔案。")
        };
    }

    private static byte[] ConvertLegacyExcelToXlsx(
        byte[] content)
    {
        using var input =
            new MemoryStream(
                content,
                writable: false);

        using var source =
            WorkbookFactory.Create(input);

        using var target =
            new XSSFWorkbook();

        var formatter =
            new DataFormatter();

        var evaluator =
            source
                .GetCreationHelper()
                .CreateFormulaEvaluator();

        for (var sheetIndex = 0;
             sheetIndex < source.NumberOfSheets;
             sheetIndex++)
        {
            var sourceSheet =
                source.GetSheetAt(sheetIndex);

            var targetSheet =
                target.CreateSheet(
                    sourceSheet.SheetName);

            for (var rowIndex = 0;
                 rowIndex <= sourceSheet.LastRowNum;
                 rowIndex++)
            {
                var sourceRow =
                    sourceSheet.GetRow(rowIndex);

                if (sourceRow is null)
                {
                    continue;
                }

                var targetRow =
                    targetSheet.CreateRow(
                        rowIndex);

                var lastCell =
                    sourceRow.LastCellNum;

                if (lastCell <= 0)
                {
                    continue;
                }

                for (var cellIndex = 0;
                     cellIndex < lastCell;
                     cellIndex++)
                {
                    var sourceCell =
                        sourceRow.GetCell(
                            cellIndex,
                            MissingCellPolicy.RETURN_BLANK_AS_NULL);

                    if (sourceCell is null)
                    {
                        continue;
                    }

                    var value =
                        FormatCell(
                            sourceCell,
                            formatter,
                            evaluator);

                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    targetRow
                        .CreateCell(
                            cellIndex,
                            CellType.String)
                        .SetCellValue(value);
                }
            }
        }

        using var output =
            new MemoryStream();

        target.Write(output);

        return output.ToArray();
    }

    private static string FormatCell(
        ICell cell,
        DataFormatter formatter,
        IFormulaEvaluator evaluator)
    {
        // Legacy .xls may store dates as numeric Excel cells
        // with display formats such as M/d/yy.
        // Normalize real date cells to ISO so the existing
        // import business rules can parse them consistently.
        if (cell.CellType == CellType.Numeric
            && DateUtil.IsCellDateFormatted(cell))
        {
            var dateValue =
                DateUtil.GetJavaDate(
                    cell.NumericCellValue);

            return dateValue.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);
        }

        try
        {
            return formatter.FormatCellValue(
                cell,
                evaluator);
        }
        catch
        {
            return formatter.FormatCellValue(
                cell);
        }
    }

    private static byte[] ConvertCsvToXlsx(
        byte[] content,
        string importKind)
    {
        var text =
            DecodeCsv(content);

        var rows =
            ParseCsv(text);

        if (rows.Count == 0
            || rows[0].Length == 0
            || rows[0].All(
                string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "CSV 沒有標題列。");
        }

        rows[0] =
            rows[0]
                .Select(
                    (value, index) =>
                        index == 0
                            ? value
                                .TrimStart('\uFEFF')
                                .Trim()
                            : value.Trim())
                .ToArray();

        var headers = rows[0];

        return importKind
            .Trim()
            .ToLowerInvariant()
            switch
            {
                "locations" =>
                    CreateXlsx(
                    [
                        (
                            "Locations",
                            rows
                        )
                    ]),

                "projects" =>
                    ConvertProjectCsv(
                        headers,
                        rows),

                "people" =>
                    ConvertPeopleCsv(
                        headers,
                        rows),

                _ =>
                    throw new InvalidOperationException(
                        $"不支援的 CSV 匯入類型：{importKind}。")
            };
    }

    private static byte[] ConvertProjectCsv(
        string[] headers,
        List<string[]> rows)
    {
        if (HasHeader(
                headers,
                "ProjectName",
                "專案名稱"))
        {
            return CreateXlsx(
            [
                (
                    "Projects",
                    rows
                )
            ]);
        }

        if (HasHeader(
                headers,
                "ProjectCode",
                "專案代碼")
            && HasHeader(
                headers,
                "LocationCode",
                "地點代碼"))
        {
            return CreateXlsx(
            [
                (
                    "Projects",
                    new List<string[]>
                    {
                        ProjectHeaders
                    }
                ),
                (
                    "ProjectLocations",
                    rows
                )
            ]);
        }

        throw new InvalidOperationException(
            "無法判斷 CSV 是專案主檔或專案地點。請使用系統下載的欄位名稱。");
    }

    private static byte[] ConvertPeopleCsv(
        string[] headers,
        List<string[]> rows)
    {
        if (HasHeader(
                headers,
                "EmployeeNo")
            || HasHeader(
                headers,
                "Roles")
            || HasHeader(
                headers,
                "TeamCodes"))
        {
            return CreateXlsx(
            [
                (
                    "InternalAuthorization",
                    rows
                ),
                (
                    "ExternalSupervisors",
                    new List<string[]>
                    {
                        ExternalSupervisorHeaders
                    }
                )
            ]);
        }

        if (HasHeader(
                headers,
                "ExternalOrganization")
            || HasHeader(
                headers,
                "ScopeType")
            || HasHeader(
                headers,
                "CanExportExcel"))
        {
            return CreateXlsx(
            [
                (
                    "InternalAuthorization",
                    new List<string[]>
                    {
                        InternalAuthorizationHeaders
                    }
                ),
                (
                    "ExternalSupervisors",
                    rows
                )
            ]);
        }

        throw new InvalidOperationException(
            "無法判斷 CSV 是內部人員授權或外部督導資料。請使用系統下載的欄位名稱。");
    }

    private static bool HasHeader(
        IEnumerable<string> headers,
        params string[] names)
    {
        return headers.Any(
            header =>
                names.Any(
                    name =>
                        string.Equals(
                            header.Trim(),
                            name,
                            StringComparison.OrdinalIgnoreCase)));
    }

    private static byte[] CreateXlsx(
        IReadOnlyList<(
            string SheetName,
            IReadOnlyList<string[]> Rows)>
            sheets)
    {
        using var workbook =
            new XSSFWorkbook();

        foreach (var definition in sheets)
        {
            var sheet =
                workbook.CreateSheet(
                    definition.SheetName);

            for (var rowIndex = 0;
                 rowIndex < definition.Rows.Count;
                 rowIndex++)
            {
                var sourceRow =
                    definition.Rows[rowIndex];

                var row =
                    sheet.CreateRow(rowIndex);

                for (var cellIndex = 0;
                     cellIndex < sourceRow.Length;
                     cellIndex++)
                {
                    var value =
                        sourceRow[cellIndex]
                        ?? "";

                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    row.CreateCell(
                            cellIndex,
                            CellType.String)
                        .SetCellValue(value);
                }
            }
        }

        using var output =
            new MemoryStream();

        workbook.Write(output);

        return output.ToArray();
    }

    private static string DecodeCsv(
        byte[] content)
    {
        if (content.Length >= 3
            && content[0] == 0xEF
            && content[1] == 0xBB
            && content[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(
                content,
                3,
                content.Length - 3);
        }

        try
        {
            var strictUtf8 =
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true);

            return strictUtf8.GetString(
                content);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(
                CodePagesEncodingProvider.Instance);

            return Encoding
                .GetEncoding(950)
                .GetString(content);
        }
    }

    private static List<string[]>
        ParseCsv(string text)
    {
        var result =
            new List<string[]>();

        var currentRow =
            new List<string>();

        var currentField =
            new StringBuilder();

        var quoted = false;

        for (var i = 0;
             i < text.Length;
             i++)
        {
            var ch = text[i];

            if (ch == '"')
            {
                if (quoted
                    && i + 1 < text.Length
                    && text[i + 1] == '"')
                {
                    currentField.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (ch == ','
                && !quoted)
            {
                currentRow.Add(
                    currentField.ToString());

                currentField.Clear();
                continue;
            }

            if ((ch == '\r'
                 || ch == '\n')
                && !quoted)
            {
                if (ch == '\r'
                    && i + 1 < text.Length
                    && text[i + 1] == '\n')
                {
                    i++;
                }

                currentRow.Add(
                    currentField.ToString());

                currentField.Clear();

                result.Add(
                    currentRow.ToArray());

                currentRow =
                    new List<string>();

                continue;
            }

            currentField.Append(ch);
        }

        if (quoted)
        {
            throw new InvalidOperationException(
                "CSV 引號格式不完整。");
        }

        if (currentField.Length > 0
            || currentRow.Count > 0)
        {
            currentRow.Add(
                currentField.ToString());

            result.Add(
                currentRow.ToArray());
        }

        while (result.Count > 0
               && result[^1].All(
                   string.IsNullOrWhiteSpace))
        {
            result.RemoveAt(
                result.Count - 1);
        }

        return result;
    }
}
