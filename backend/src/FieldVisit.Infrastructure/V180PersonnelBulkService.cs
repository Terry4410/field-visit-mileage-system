using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180PersonnelBulkService(AppDbContext db) : IV180PersonnelBulkService
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly string[] PersonnelHeaders =
        ["UserCode", "EmployeeNo", "DisplayName", "Email", "HireDate", "TerminationDate"];
    private static readonly string[] StatusHeaders =
        ["UserCode", "Status", "EffectiveFrom", "EffectiveTo"];

    public Task<ReportExportContext> CreateTemplateAsync(CurrentUserDto admin, CancellationToken ct)
    {
        RequireOrg(admin);
        var bytes = CreateWorkbook(
            ("Personnel", new[]
            {
                PersonnelHeaders,
                new[] { "visitor01", "E100", "王大明", "user@example.com", "2026-01-01", "" }
            }),
            ("EmploymentStatus", new[]
            {
                StatusHeaders,
                new[] { "visitor01", "Leave", "2026-10-01", "2026-10-31" }
            }),
            ("Instructions", new[]
            {
                new[] { "項目", "說明" },
                new[] { "UserCode", "穩定識別鍵，只用來找到既有內部人員；請勿自行變更。" },
                new[] { "Personnel", "EmployeeNo、姓名、Email、入職日、離職日皆可維護，與單筆人事畫面一致。" },
                new[] { "EmploymentStatus", "同一人可建立多個不重疊的 Active / Leave / Terminated / PreHire 有效期間。" },
                new[] { "責任邊界", "此 Excel 不修改角色或小組歸屬；角色在人員與權限、小組在小組與成員維護。" },
                new[] { "安全", "上傳先預覽；確認時會重新驗證，整批以單一 transaction 套用。" }
            }));
        return Task.FromResult(new ReportExportContext("人事主檔批次維護.xlsx", bytes, ExcelContentType));
    }

    public async Task<V180SimpleBulkPreviewDto> PreviewAsync(CurrentUserDto admin, byte[] content, CancellationToken ct)
    {
        var orgId = RequireOrg(admin);
        var rows = ReadWorkbook(content);
        var items = new List<V180SimpleBulkPreviewItem>();
        var seenPersonnel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stagedStatus = new Dictionary<string, List<(DateOnly From, DateOnly? To)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var userCode = Required(row, "UserCode");
            try
            {
                var target = await ResolveAsync(orgId, userCode, ct);
                if (row.Sheet == "Personnel")
                {
                    if (!seenPersonnel.Add(userCode)) throw new InvalidOperationException("DUPLICATE_PERSONNEL_ROW");
                    var employeeNo = Required(row, "EmployeeNo");
                    var displayName = Required(row, "DisplayName");
                    var email = Optional(row, "Email");
                    ValidateEmail(email);
                    var hire = Date(row, "HireDate", false);
                    var termination = Date(row, "TerminationDate", false);
                    if (hire.HasValue && termination.HasValue && termination.Value < hire.Value)
                        throw new InvalidOperationException("TERMINATION_BEFORE_HIRE");
                    if (await db.Employments.AsNoTracking().AnyAsync(x =>
                            x.OrganizationId == orgId && x.EmploymentId != target.EmploymentId && x.EmployeeNo == employeeNo, ct))
                        throw new InvalidOperationException("DUPLICATE_EMPLOYEE_NO");
                    if (email is not null && await db.Users.AsNoTracking().AnyAsync(x =>
                            x.OrganizationId == orgId && x.UserId != target.UserId && x.Email == email, ct))
                        throw new InvalidOperationException("DUPLICATE_EMAIL");

                    var noChange = target.EmployeeNo == employeeNo
                        && target.DisplayName == displayName
                        && string.Equals(target.Email ?? "", email ?? "", StringComparison.OrdinalIgnoreCase)
                        && target.HireDate == hire
                        && target.TerminationDate == termination;
                    items.Add(Valid(row, $"{userCode}|{employeeNo}", noChange ? "NoChange" : "Update"));
                }
                else
                {
                    var status = NormalizeStatus(Required(row, "Status"));
                    var from = Date(row, "EffectiveFrom", true)!.Value;
                    var to = Date(row, "EffectiveTo", false);
                    Period(from, to);
                    var existing = await db.EmploymentStatusPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
                        x.EmploymentId == target.EmploymentId && x.EffectiveFrom == from, ct);
                    var existingId = existing?.EmploymentStatusPeriodId ?? 0;
                    var others = await db.EmploymentStatusPeriods.AsNoTracking().Where(x =>
                        x.EmploymentId == target.EmploymentId && x.EmploymentStatusPeriodId != existingId).ToListAsync(ct);
                    if (others.Any(x => Overlaps(from, to, x.EffectiveFrom, x.EffectiveTo)))
                        throw new InvalidOperationException("OVERLAPPING_EMPLOYMENT_STATUS");
                    if (!stagedStatus.TryGetValue(userCode, out var staged)) stagedStatus[userCode] = staged = [];
                    if (staged.Any(x => Overlaps(from, to, x.From, x.To)))
                        throw new InvalidOperationException("OVERLAPPING_EMPLOYMENT_STATUS_IN_WORKBOOK");
                    staged.Add((from, to));
                    var noChange = existing is not null
                        && string.Equals(existing.EmploymentStatus, status, StringComparison.OrdinalIgnoreCase)
                        && existing.EffectiveTo == to;
                    items.Add(Valid(row, $"{userCode}|{from:yyyy-MM-dd}", existing is null ? "Create" : noChange ? "NoChange" : "Update"));
                }
            }
            catch (Exception ex)
            {
                items.Add(Error(row, userCode, ex.Message));
            }
        }
        return Preview(items);
    }

    public async Task<V180SimpleBulkConfirmResultDto> ConfirmAsync(CurrentUserDto admin, byte[] content, CancellationToken ct)
    {
        var preview = await PreviewAsync(admin, content, ct);
        if (preview.ErrorCount > 0) throw new InvalidOperationException("PERSONNEL_BULK_HAS_ERRORS");
        var orgId = RequireOrg(admin);
        var rows = ReadWorkbook(content);
        var result = new V180SimpleBulkConfirmResultDto(0, 0, 0, []);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var applied = 0;var noChange = 0;
            foreach (var row in rows)
            {
                var userCode = Required(row, "UserCode");
                var target = await ResolveAsync(orgId, userCode, ct, tracking: true);
                if (row.Sheet == "Personnel")
                {
                    var employeeNo = Required(row, "EmployeeNo");
                    var displayName = Required(row, "DisplayName");
                    var email = Optional(row, "Email");ValidateEmail(email);
                    var hire = Date(row, "HireDate", false);var termination = Date(row, "TerminationDate", false);
                    if (hire.HasValue && termination.HasValue && termination.Value < hire.Value)
                        throw new InvalidOperationException($"TERMINATION_BEFORE_HIRE:{userCode}");
                    if (await db.Employments.AsNoTracking().AnyAsync(x => x.OrganizationId == orgId && x.EmploymentId != target.EmploymentId && x.EmployeeNo == employeeNo, ct))
                        throw new InvalidOperationException($"DUPLICATE_EMPLOYEE_NO:{userCode}");
                    if (email is not null && await db.Users.AsNoTracking().AnyAsync(x => x.OrganizationId == orgId && x.UserId != target.UserId && x.Email == email, ct))
                        throw new InvalidOperationException($"DUPLICATE_EMAIL:{userCode}");
                    var noChangeRow = target.EmployeeNo == employeeNo && target.DisplayName == displayName
                        && string.Equals(target.Email ?? "", email ?? "", StringComparison.OrdinalIgnoreCase)
                        && target.HireDate == hire && target.TerminationDate == termination;
                    if (noChangeRow){noChange++;continue;}
                    var user = await db.Users.SingleAsync(x => x.UserId == target.UserId && x.OrganizationId == orgId, ct);
                    var employment = await db.Employments.SingleAsync(x => x.EmploymentId == target.EmploymentId && x.OrganizationId == orgId, ct);
                    var oldValues = new { user.EmployeeNo, user.DisplayName, user.Email, employment.HireDate, employment.TerminationDate };
                    user.EmployeeNo = employeeNo;user.DisplayName = displayName;user.Email = email;user.UpdatedAt = DateTime.UtcNow;
                    employment.EmployeeNo = employeeNo;employment.Email = email;employment.HireDate = hire;employment.TerminationDate = termination;
                    db.AuditLogs.Add(new AuditLog{UserId=admin.UserId,EntityType="Employment",EntityId=employment.EmploymentId.ToString(),Action="PersonnelBulkUpdate",OldValues=JsonSerializer.Serialize(oldValues),NewValues=JsonSerializer.Serialize(new{employeeNo,displayName,email,hire,termination}),CreatedAt=DateTime.UtcNow});
                    applied++;
                }
                else
                {
                    var status = NormalizeStatus(Required(row, "Status"));var from=Date(row,"EffectiveFrom",true)!.Value;var to=Date(row,"EffectiveTo",false);Period(from,to);
                    var existing = await db.EmploymentStatusPeriods.SingleOrDefaultAsync(x => x.EmploymentId == target.EmploymentId && x.EffectiveFrom == from, ct);
                    var existingId = existing?.EmploymentStatusPeriodId ?? 0;
                    if (await db.EmploymentStatusPeriods.AsNoTracking().AnyAsync(x => x.EmploymentId == target.EmploymentId && x.EmploymentStatusPeriodId != existingId && x.EffectiveFrom <= (to ?? DateOnly.MaxValue) && (!x.EffectiveTo.HasValue || from <= x.EffectiveTo.Value), ct))
                        throw new InvalidOperationException($"OVERLAPPING_EMPLOYMENT_STATUS:{userCode}:{from:yyyy-MM-dd}");
                    if(existing is not null&&string.Equals(existing.EmploymentStatus,status,StringComparison.OrdinalIgnoreCase)&&existing.EffectiveTo==to){noChange++;continue;}
                    var oldValues=existing is null?null:new{existing.EmploymentStatus,existing.EffectiveFrom,existing.EffectiveTo};
                    if(existing is null){existing=new EmploymentStatusPeriod{EmploymentId=target.EmploymentId,EffectiveFrom=from,SourceType="Excel",SourceReference="PersonnelBulk"};db.EmploymentStatusPeriods.Add(existing);}
                    existing.EmploymentStatus=status;existing.EffectiveTo=to;
                    db.AuditLogs.Add(new AuditLog{UserId=admin.UserId,EntityType="EmploymentStatusPeriod",EntityId=$"{target.EmploymentId}:{from:yyyy-MM-dd}",Action=existingId==0?"PersonnelBulkCreate":"PersonnelBulkUpdate",OldValues=oldValues is null?null:JsonSerializer.Serialize(oldValues),NewValues=JsonSerializer.Serialize(new{status,from,to}),CreatedAt=DateTime.UtcNow});
                    applied++;
                }
            }
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            result=new V180SimpleBulkConfirmResultDto(applied,noChange,0,[]);
        });
        return result;
    }

    private async Task<Target> ResolveAsync(int orgId,string userCode,CancellationToken ct,bool tracking=false)
    {
        var profiles=tracking?db.UserIdentityProfiles:db.UserIdentityProfiles.AsNoTracking();
        var identity=await profiles.SingleOrDefaultAsync(x=>x.UserCode==userCode&&x.UserType==UserTypes.Internal,ct)
            ??throw new InvalidOperationException("UNKNOWN_USER_CODE");
        if(!identity.EmploymentId.HasValue)throw new InvalidOperationException("INTERNAL_USER_LINK_MISSING");
        var users=tracking?db.Users:db.Users.AsNoTracking();var employments=tracking?db.Employments:db.Employments.AsNoTracking();
        var user=await users.SingleOrDefaultAsync(x=>x.UserId==identity.UserId&&x.OrganizationId==orgId,ct)??throw new InvalidOperationException("UNKNOWN_USER_CODE");
        var employment=await employments.SingleOrDefaultAsync(x=>x.EmploymentId==identity.EmploymentId.Value&&x.OrganizationId==orgId,ct)??throw new InvalidOperationException("INTERNAL_USER_LINK_MISSING");
        return new Target(identity.UserId,employment.EmploymentId,employment.EmployeeNo,user.DisplayName,user.Email,employment.HireDate,employment.TerminationDate);
    }

    private static int RequireOrg(CurrentUserDto admin)=>admin.OrganizationId??throw new UnauthorizedAccessException("管理者缺少 Organization 範圍。");
    private static string NormalizeStatus(string value)=>value.Trim().ToLowerInvariant() switch{"active"=>"Active","leave"=>"Leave","terminated"=>"Terminated","prehire"=>"PreHire",_=>throw new InvalidOperationException("INVALID_EMPLOYMENT_STATUS")};
    private static void ValidateEmail(string? email){if(string.IsNullOrWhiteSpace(email))return;try{_ = new MailAddress(email);}catch{throw new InvalidOperationException("INVALID_EMAIL");}}
    private static void Period(DateOnly from,DateOnly? to){if(to.HasValue&&to.Value<from)throw new InvalidOperationException("EFFECTIVE_TO_BEFORE_FROM");}
    private static bool Overlaps(DateOnly aFrom,DateOnly? aTo,DateOnly bFrom,DateOnly? bTo)=>aFrom<=(bTo??DateOnly.MaxValue)&&bFrom<=(aTo??DateOnly.MaxValue);
    private static V180SimpleBulkPreviewItem Valid(ParsedRow row,string key,string action)=>new(row.RowNumber,row.Sheet,key,action,"Valid",null);
    private static V180SimpleBulkPreviewItem Error(ParsedRow row,string key,string message)=>new(row.RowNumber,row.Sheet,key,"Error","Error",message);
    private static V180SimpleBulkPreviewDto Preview(IReadOnlyList<V180SimpleBulkPreviewItem> items)=>new(items.Count,items.Count(x=>x.Status=="Valid"),items.Count(x=>x.Status=="Error"),items.Take(200).ToList());
    private static string Required(ParsedRow row,string name)=>Optional(row,name)is{Length:>0}value?value:throw new InvalidOperationException($"{name}_REQUIRED");
    private static string? Optional(ParsedRow row,string name){if(!row.Values.TryGetValue(name,out var value))return null;value=value?.Trim();return string.IsNullOrWhiteSpace(value)?null:value;}
    private static DateOnly? Date(ParsedRow row,string name,bool required){var value=Optional(row,name);if(string.IsNullOrWhiteSpace(value)){if(required)throw new InvalidOperationException($"{name}_REQUIRED");return null;}if(!DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))throw new InvalidOperationException($"{name}_INVALID_DATE");return date;}

    private static IReadOnlyList<ParsedRow> ReadWorkbook(byte[] content)
    {
        if(content.Length==0)throw new InvalidOperationException("BULK_WORKBOOK_REQUIRED");
        using var stream=new MemoryStream(content);using var doc=SpreadsheetDocument.Open(stream,false);var wb=doc.WorkbookPart??throw new InvalidOperationException("BULK_XLSX_INVALID");
        var result=new List<ParsedRow>();ReadRows(wb,"Personnel",PersonnelHeaders,result);ReadRows(wb,"EmploymentStatus",StatusHeaders,result);return result;
    }
    private static void ReadRows(WorkbookPart wb,string sheetName,string[] required,List<ParsedRow> result)
    {
        var raw=ReadSheet(wb,sheetName);if(raw.Count==0)throw new InvalidOperationException($"BULK_HEADER_REQUIRED:{sheetName}");var headers=raw[0].Select(x=>x?.Trim()??"").ToArray();
        foreach(var h in required)if(!headers.Contains(h,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException($"BULK_HEADER_REQUIRED:{sheetName}:{h}");
        foreach(var h in headers.Where(x=>x.Length>0))if(!required.Contains(h,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException($"BULK_HEADER_UNKNOWN:{sheetName}:{h}");
        for(var i=1;i<raw.Count;i++){var values=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);var any=false;for(var h=0;h<headers.Length;h++){if(headers[h].Length==0)continue;var value=h<raw[i].Length?raw[i][h]?.Trim():null;values[headers[h]]=value;if(!string.IsNullOrWhiteSpace(value))any=true;}if(any)result.Add(new ParsedRow(sheetName,i+1,values));}
    }
    private static byte[] CreateWorkbook(params(string Name,IEnumerable<string[]> Rows)[]sheets){using var stream=new MemoryStream();using(var doc=SpreadsheetDocument.Create(stream,DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook,true)){var wb=doc.AddWorkbookPart();wb.Workbook=new Workbook();var collection=wb.Workbook.AppendChild(new Sheets());uint id=1;foreach(var spec in sheets)AddSheet(wb,collection,id++,spec.Name,spec.Rows);wb.Workbook.Save();}return stream.ToArray();}
    private static void AddSheet(WorkbookPart wb,Sheets sheets,uint id,string name,IEnumerable<string[]> rows){var part=wb.AddNewPart<WorksheetPart>();var data=new SheetData();part.Worksheet=new Worksheet(data);foreach(var values in rows){var row=new Row();foreach(var value in values)row.Append(new Cell{DataType=CellValues.InlineString,InlineString=new InlineString(new Text(value??""))});data.Append(row);}part.Worksheet.Save();sheets.Append(new Sheet{Id=wb.GetIdOfPart(part),SheetId=id,Name=name});}
    private static IReadOnlyList<string[]> ReadSheet(WorkbookPart wb,string name){var sheet=wb.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault(x=>string.Equals(x.Name?.Value,name,StringComparison.OrdinalIgnoreCase))??throw new InvalidOperationException($"Excel 缺少工作表：{name}");var part=(WorksheetPart)wb.GetPartById(sheet.Id!);var result=new List<string[]>();foreach(var row in part.Worksheet.Descendants<Row>()){var values=new string[32];var sequential=0;foreach(var cell in row.Elements<Cell>()){var index=ColumnIndex(cell.CellReference?.Value);if(index<0)index=sequential;if(index>=0&&index<values.Length)values[index]=CellText(wb,cell);sequential=Math.Max(sequential+1,index+1);}result.Add(values);}return result;}
    private static string CellText(WorkbookPart wb,Cell cell){if(cell.DataType?.Value==CellValues.SharedString&&int.TryParse(cell.CellValue?.Text,out var index))return wb.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().ElementAtOrDefault(index)?.InnerText??"";if(cell.DataType?.Value==CellValues.InlineString)return cell.InlineString?.InnerText??"";return cell.CellValue?.Text??cell.InnerText??"";}
    private static int ColumnIndex(string? reference){if(string.IsNullOrWhiteSpace(reference))return-1;var value=0;foreach(var ch in reference){if(!char.IsLetter(ch))break;value=value*26+char.ToUpperInvariant(ch)-'A'+1;}return value>0?value-1:-1;}
    private sealed record ParsedRow(string Sheet,int RowNumber,Dictionary<string,string?> Values);
    private sealed record Target(int UserId,long EmploymentId,string? EmployeeNo,string DisplayName,string? Email,DateOnly? HireDate,DateOnly? TerminationDate);
}
