using System.Globalization;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace FieldVisit.Api.Controllers;

internal sealed record ReportExportColumn(
    string Label,
    string TechnicalId,
    string Source,
    string Definition);

/// <summary>Builds the business, raw and field-map sheets from one authorized projection.</summary>
internal static class BilingualReportWorkbookWriter
{
    private sealed record Field(string Id, string Source, string Definition);

    private static readonly IReadOnlyDictionary<string, Field> Fields = BuildFields();

    public static byte[] Create(
        string title,
        object filters,
        IReadOnlyList<string> labels,
        IEnumerable<string[]> sourceRows,
        int maxRows)
    {
        var columns = labels.Select(label => Resolve(title, label)).ToArray();
        var rows = sourceRows.ToList();
        if (rows.Count > maxRows) throw new InvalidOperationException("REPORT_EXPORT_ROW_LIMIT");
        if (rows.Any(row => row.Length != columns.Length))
            throw new InvalidOperationException("REPORT_EXPORT_COLUMN_COUNT_MISMATCH");

        using var book = new XSSFWorkbook();
        var business = book.CreateSheet("業務查詢資料");
        var raw = book.CreateSheet("原始資料");
        var map = book.CreateSheet("欄位對照表");

        Write(business, 0, columns.Select(x => x.Label));
        Write(business, 1, columns.Select(x => x.TechnicalId));
        Write(raw, 0, columns.Select(x => x.TechnicalId));
        for (var index = 0; index < rows.Count; index++)
        {
            Write(business, index + 2, rows[index]);
            Write(raw, index + 1, rows[index]);
        }

        Write(map, 0, ["繁體中文標頭", "English technical identifier", "實際來源", "業務定義與格式"]);
        for (var index = 0; index < columns.Length; index++)
        {
            var column = columns[index];
            Write(map, index + 1, [column.Label, column.TechnicalId, column.Source, column.Definition]);
        }

        StyleHeaders(book, business, 2, columns.Length, rows.Count + 2);
        StyleHeaders(book, raw, 1, columns.Length, rows.Count + 1);
        StyleHeaders(book, map, 1, 4, columns.Length + 1);
        business.CreateFreezePane(0, 2);
        raw.CreateFreezePane(0, 1);
        map.CreateFreezePane(0, 1);
        if (columns.Length > 0 && rows.Count > 0)
        {
            business.SetAutoFilter(new CellRangeAddress(1, rows.Count + 1, 0, columns.Length - 1));
            raw.SetAutoFilter(new CellRangeAddress(0, rows.Count, 0, columns.Length - 1));
        }
        for (var column = 0; column < columns.Length; column++)
        {
            business.SetColumnWidth(column, Width(columns[column].Label));
            raw.SetColumnWidth(column, Width(columns[column].TechnicalId));
        }
        map.SetColumnWidth(0, 24 * 256);
        map.SetColumnWidth(1, 34 * 256);
        map.SetColumnWidth(2, 60 * 256);
        map.SetColumnWidth(3, 58 * 256);

        // Filters are intentionally metadata only; they never change the rows
        // supplied by the already authorized server-side query.
        book.GetProperties().CoreProperties.Title = title;
        // Do not persist user-supplied search terms in document metadata.
        book.GetProperties().CoreProperties.Subject = "Authorized report export";
        using var stream = new MemoryStream();
        book.Write(stream);
        return stream.ToArray();
    }

    private static ReportExportColumn Resolve(string title, string label)
    {
        if (!Fields.TryGetValue(title + "\n" + label, out var field))
            throw new InvalidOperationException("REPORT_EXPORT_COLUMN_MAP_MISSING");
        return new ReportExportColumn(label, field.Id, field.Source, field.Definition);
    }

    private static void Write(ISheet sheet, int rowIndex, IEnumerable<string> values)
    {
        var row = sheet.CreateRow(rowIndex);
        var column = 0;
        foreach (var value in values)
            // Explicit String cells preserve leading zeros and keep formula-like
            // user text inert in Excel and compatible spreadsheet readers.
            row.CreateCell(column++, CellType.String).SetCellValue(value ?? string.Empty);
    }

    private static void StyleHeaders(XSSFWorkbook book, ISheet sheet, int headerRows, int columns, int rowCount)
    {
        var style = book.CreateCellStyle();
        style.FillForegroundColor = IndexedColors.LightCornflowerBlue.Index;
        style.FillPattern = FillPattern.SolidForeground;
        style.BorderBottom = BorderStyle.Thin;
        style.WrapText = true;
        var font = book.CreateFont();
        font.IsBold = true;
        style.SetFont(font);
        for (var rowIndex = 0; rowIndex < headerRows; rowIndex++)
        {
            var row = sheet.GetRow(rowIndex);
            if (row is null) continue;
            for (var column = 0; column < columns; column++)
                if (row.GetCell(column) is { } cell) cell.CellStyle = style;
        }
        for (var rowIndex = headerRows; rowIndex < rowCount; rowIndex++)
            if (sheet.GetRow(rowIndex) is { } row) row.Height = 19;
    }

    private static int Width(string text) => Math.Min(42, Math.Max(15, text.Length + 4)) * 256;

    private static IReadOnlyDictionary<string, Field> BuildFields()
    {
        var map = new Dictionary<string, Field>(StringComparer.Ordinal);
        void Add(string report, string label, string id, string source, string definition) =>
            map.Add(report + "\n" + label, new Field(id, source, definition));

        void People(string report, params (string Label, string Id, string Source, string Meaning)[] fields)
        {
            foreach (var field in fields) Add(report, field.Label, field.Id, field.Source, field.Meaning);
        }

        People("人事資料",
            ("工號", "EmployeeNo", "V170PeopleRowDto.EmployeeNo; derived from Users.EmployeeNo, falling back to UserIdentityProfiles.UserCode", "組織內人員識別碼；以文字保存。"),
            ("姓名", "DisplayName", "V170PeopleRowDto.DisplayName ← Users.DisplayName", "人員顯示姓名。"),
            ("Email", "Email", "V170PeopleRowDto.Email ← Users.Email", "帳號電子郵件；個人資料欄位。"),
            ("人事狀態", "EmploymentStatus", "V170PeopleRowDto.EmploymentStatus; effective-dated EmploymentStatusPeriods / UserEmploymentPeriods", "查詢日有效的人事狀態。"),
            ("入職日", "HireDate", "V170PeopleRowDto.HireDate ← Employments.HireDate", "人事主檔入職日期。"),
            ("離職日", "TerminationDate", "V170PeopleRowDto.TerminationDate ← Employments.TerminationDate", "人事主檔離職日期。"),
            ("主要小組", "PrimaryTeamName", "V170PeopleRowDto.PrimaryTeamName; effective UserTeamAssignments joined to Teams", "目前主要管理小組名稱。"),
            ("主要派駐據點", "PrimaryDeploymentSiteName", "V170PeopleRowDto.PrimaryDeploymentSiteName; effective EmploymentDeploymentSiteAssignments joined to DeploymentSites", "目前主要派駐據點名稱。"),
            ("就業中心", "PrimaryCenterName", "V170PeopleRowDto.PrimaryCenterName; derived through DeploymentSites and Centers", "主要派駐據點所屬中心名稱；衍生欄位。"));

        People("角色與登入",
            ("工號", "EmployeeNo", "V170PeopleRowDto.EmployeeNo; derived from Users.EmployeeNo / UserIdentityProfiles.UserCode", "組織內人員識別碼；以文字保存。"),
            ("姓名", "DisplayName", "V170PeopleRowDto.DisplayName ← Users.DisplayName", "人員顯示姓名。"),
            ("角色", "Roles", "V170PeopleRowDto.Roles; effective UserRoleAssignments joined to Roles", "查詢日有效角色清單；多角色以頓號分隔。"),
            ("人事狀態", "EmploymentStatus", "V170PeopleRowDto.EmploymentStatus; effective employment status history", "查詢日有效的人事狀態。"),
            ("實際登入", "ActualAccess", "V170PeopleRowDto.ActualAccess; derived from account and HR login eligibility", "伺服器計算的目前登入資格，不是單一資料庫欄位。"),
            ("管理小組", "TeamAssignments", "V170PeopleRowDto.TeamAssignments; effective UserTeamAssignments joined to Teams", "目前有效小組與主要小組旗標；多值欄位。"));

        People("小組",
            ("小組代碼", "TeamCode", "V180TeamRow.TeamCode ← Teams.TeamCode", "組織內小組識別代碼。"),
            ("小組名稱", "TeamName", "V180TeamRow.TeamName ← Teams.TeamName", "小組顯示名稱。"),
            ("啟用", "IsActive", "V180TeamRow.IsActive ← Teams.IsActive", "小組啟用狀態。"),
            ("成員人數", "MemberCount", "V180TeamRow.MemberCount; derived count of effective UserTeamAssignments", "查詢日有效成員數；衍生計數。"));

        People("小組成員",
            ("工號", "EmployeeNo", "V170PeopleRowDto.EmployeeNo; derived from Users.EmployeeNo / UserIdentityProfiles.UserCode", "組織內人員識別碼；以文字保存。"),
            ("姓名", "DisplayName", "V170PeopleRowDto.DisplayName ← Users.DisplayName", "人員顯示姓名。"),
            ("角色", "Roles", "V170PeopleRowDto.Roles; effective role assignments joined to Roles", "查詢日有效角色清單；多角色以頓號分隔。"),
            ("人事狀態", "EmploymentStatus", "V170PeopleRowDto.EmploymentStatus; effective employment status history", "查詢日有效的人事狀態。"),
            ("主要小組", "PrimaryTeamName", "V170PeopleRowDto.PrimaryTeamName; effective team assignments joined to Teams", "目前主要管理小組名稱。"),
            ("其他小組", "OtherTeamNames", "Derived from V170PeopleRowDto.TeamAssignments excluding primary assignment", "目前其他有效小組名稱；衍生多值欄位。"),
            ("主要派駐據點", "PrimaryDeploymentSiteName", "V170PeopleRowDto.PrimaryDeploymentSiteName; effective employment-site assignment joined to DeploymentSites", "目前主要派駐據點名稱。"));

        People("地點主檔",
            ("地點代碼", "LocationCode", "ManagedLocationDto.LocationCode ← Locations.LocationCode", "地點主檔識別碼。"),
            ("名稱", "LocationName", "ManagedLocationDto.LocationName ← Locations.LocationName", "地點名稱。"),
            ("統一編號", "TaxId", "ManagedLocationDto.TaxId ← Locations.TaxId", "地點或機構統一編號；敏感主檔欄位。"),
            ("主檔備註", "MasterNote", "ManagedLocationDto.MasterNote ← Locations.MasterNote", "管理者維護備註；自由文字。"),
            ("小組", "TeamName", "ManagedLocationDto.TeamName; joined from Locations.TeamId to Teams.TeamName", "地點所屬小組名稱；衍生欄位。"),
            ("縣市", "City", "ManagedLocationDto.City ← Locations.City", "地點縣市。"),
            ("鄉鎮區", "District", "ManagedLocationDto.District ← Locations.District", "地點鄉鎮市區。"),
            ("地址", "Address", "ManagedLocationDto.Address ← Locations.Address", "地點地址；自由文字。"),
            ("Plus Code", "PlusCode", "ManagedLocationDto.PlusCode ← Locations.PlusCode", "Google Plus Code。"),
            ("類型", "LocationType", "ManagedLocationDto.LocationType ← Locations.LocationType", "地點類型。"),
            ("審核狀態", "ApprovalStatus", "ManagedLocationDto.ApprovalStatus ← Locations.ApprovalStatus", "地點審核狀態。"),
            ("解析狀態", "GeocodingStatus", "ManagedLocationDto.GeocodingStatus ← Locations.GeocodingStatus", "地理編碼狀態。"),
            ("啟用", "IsActive", "ManagedLocationDto.IsActive ← Locations.IsActive", "地點啟用狀態。"));

        People("專案",
            ("專案代碼", "ProjectCode", "V180ProjectRow.ProjectCode ← Projects.ProjectCode", "組織內專案識別代碼。"),
            ("專案名稱", "ProjectName", "V180ProjectRow.ProjectName ← Projects.ProjectName", "專案名稱。"),
            ("歸屬小組 ID", "TeamId", "V180ProjectRow.TeamId ← Projects.TeamId", "歸屬小組識別碼；無歸屬時空白。"),
            ("說明", "Description", "V180ProjectRow.Description ← Projects.Description", "專案說明文字。"),
            ("地點方式", "LocationMode", "V180ProjectRow.LocationMode ← Projects.LocationMode", "專案地點選擇方式。"),
            ("開始日", "StartDate", "V180ProjectRow.StartDate ← Projects.StartDate", "專案有效開始日，格式 yyyy-MM-dd。"),
            ("結束日", "EndDate", "V180ProjectRow.EndDate ← Projects.EndDate", "專案有效結束日，格式 yyyy-MM-dd。"),
            ("啟用", "IsActive", "V180ProjectRow.IsActive ← Projects.IsActive", "專案啟用狀態。"),
            ("固定地點數", "LocationCount", "V180ProjectRow.LocationCount; derived count of active ProjectLocations", "目前有效固定地點數；衍生計數。"));

        People("就業中心",
            ("Center Code", "CenterCode", "V180MasterDataRow.Key ← Centers.CenterCode", "就業中心代碼。"),
            ("就業中心名稱", "CenterName", "V180MasterDataRow.Detail ← Centers.CenterName", "就業中心顯示名稱。"),
            ("生效日", "EffectiveFrom", "V180MasterDataRow.EffectiveFrom ← Centers.EffectiveFrom", "資料有效起日，格式 yyyy-MM-dd。"),
            ("失效日", "EffectiveTo", "V180MasterDataRow.EffectiveTo ← Centers.EffectiveTo", "資料有效迄日，格式 yyyy-MM-dd；空白代表未設定。"),
            ("目前狀態", "EffectiveStatus", "Derived from Centers.IsActive and effective dates", "依啟用旗標與今天的有效期間計算。"));

        People("官方據點",
            ("Center Code", "CenterCode", "V180MasterDataRow.ParentKey ← Centers.CenterCode", "所屬就業中心代碼。"),
            ("就業中心", "CenterName", "Derived join from DeploymentSites.CenterId to Centers.CenterName", "所屬就業中心名稱；衍生欄位。"),
            ("Site Code", "SiteCode", "V180MasterDataRow.Key ← DeploymentSites.SiteCode", "官方據點代碼。"),
            ("據點名稱", "SiteName", "V180MasterDataRow.Detail ← DeploymentSites.SiteName", "官方據點名稱。"),
            ("Location Code", "LocationCode", "V180MasterDataRow.ReferenceKey; effective DeploymentSiteLocationAssignments joined to Locations", "目前符合據點有效區間的地點代碼；衍生關聯欄位。"),
            ("生效日", "EffectiveFrom", "V180MasterDataRow.EffectiveFrom ← DeploymentSites.EffectiveFrom", "資料有效起日，格式 yyyy-MM-dd。"),
            ("失效日", "EffectiveTo", "V180MasterDataRow.EffectiveTo ← DeploymentSites.EffectiveTo", "資料有效迄日，格式 yyyy-MM-dd；空白代表未設定。"),
            ("目前狀態", "EffectiveStatus", "Derived from DeploymentSites.IsActive and effective dates", "依啟用旗標與今天的有效期間計算。"));

        return map;
    }
}
