using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Team-membership-only bulk maintenance.
/// Roles, account state and personnel master data are intentionally out of scope.
/// v1.7 UserTeamAssignments and v1.8 TeamMemberships are written together.
/// </summary>
public sealed class V180TeamMembershipBulkService(AppDbContext db)
    : IV180PeopleManagementBulkService
{
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly string[] TeamMembershipHeaders =
        ["EmployeeNo", "TeamCode", "IsPrimary", "EffectiveFrom", "EffectiveTo"];

    public Task<ReportExportContext> CreateTeamMembershipTemplateAsync(
        CurrentUserDto admin,
        CancellationToken ct)
    {
        RequireOrg(admin);
        var bytes = CreateWorkbook(
            ("TeamMemberships", new[]
            {
                TeamMembershipHeaders,
                new[] { "E100", "TEAM-N01", "Y", "2026-10-01", "" }
            }),
            ("Instructions", new[]
            {
                new[] { "項目", "說明" },
                new[] { "用途", "批次新增或排程小組成員；不修改 Roles、帳號、人事主檔。" },
                new[] { "IsPrimary", "Y/N。若人員在該有效期間尚無任何小組，第一筆必須為主要小組。" },
                new[] { "安全", "不提供 Excel 批次刪除；既有 membership 只會 NoChange，期間重疊會拒絕。" }
            }));
        return Task.FromResult(new ReportExportContext(
            "小組成員批次維護.xlsx",
            bytes,
            ExcelContentType));
    }

    public async Task<V180SimpleBulkPreviewDto> PreviewTeamMembershipAsync(
        CurrentUserDto admin,
        byte[] content,
        CancellationToken ct)
    {
        var orgId = RequireOrg(admin);
        var rows = ReadWorkbook(content, ("TeamMemberships", TeamMembershipHeaders));
        var items = new List<V180SimpleBulkPreviewItem>();
        var stagedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stagedPrimary = new Dictionary<string, List<(DateOnly From, DateOnly? To)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var employeeNo = Required(row, "EmployeeNo");
            var teamCode = Required(row, "TeamCode");
            var key = $"{employeeNo}|{teamCode}|{Optional(row, "EffectiveFrom")}";
            try
            {
                var employment = await db.Employments.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.OrganizationId == orgId && x.EmployeeNo == employeeNo, ct)
                    ?? throw new InvalidOperationException("UNKNOWN_EMPLOYEE_NO");
                var userId = employment.LegacyUserId
                    ?? throw new InvalidOperationException("INTERNAL_USER_LINK_MISSING");
                var team = await db.Teams.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.OrganizationId == orgId && x.TeamCode == teamCode && x.IsActive, ct)
                    ?? throw new InvalidOperationException("UNKNOWN_OR_INACTIVE_TEAM");

                var primary = Bool(row, "IsPrimary");
                var from = Date(row, "EffectiveFrom", true)!.Value;
                var to = Date(row, "EffectiveTo", false);
                Period(from, to);
                key = $"{employeeNo}|{teamCode}|{from:yyyy-MM-dd}";
                if (!stagedKeys.Add(key))
                    throw new InvalidOperationException("DUPLICATE_TEAM_MEMBERSHIP_ROW");

                var v17 = await db.UserTeamAssignments.AsNoTracking()
                    .Where(x => x.UserId == userId && x.TeamId == team.TeamId)
                    .ToListAsync(ct);
                var v18 = await db.TeamMemberships.AsNoTracking()
                    .Where(x => x.EmploymentId == employment.EmploymentId && x.TeamId == team.TeamId)
                    .ToListAsync(ct);

                var exact17 = v17.SingleOrDefault(x =>
                    x.EffectiveFrom == from && x.EffectiveTo == to && x.IsPrimary == primary);
                var exact18 = v18.SingleOrDefault(x =>
                    x.EffectiveFrom == from && x.EffectiveTo == to && x.IsPrimary == primary);
                if ((exact17 is null) != (exact18 is null))
                    throw new InvalidOperationException("TEAM_MEMBERSHIP_MODEL_DRIFT");
                if (exact17 is not null && exact18 is not null)
                {
                    items.Add(Valid(row, key, "NoChange"));
                    continue;
                }

                if (v17.Any(x => Overlaps(from, to, x.EffectiveFrom, x.EffectiveTo))
                    || v18.Any(x => Overlaps(from, to, x.EffectiveFrom, x.EffectiveTo)))
                    throw new InvalidOperationException("OVERLAPPING_TEAM_MEMBERSHIP");

                var existingMemberships = await db.TeamMemberships.AsNoTracking()
                    .Where(x => x.EmploymentId == employment.EmploymentId)
                    .ToListAsync(ct);
                var overlapping = existingMemberships
                    .Where(x => Overlaps(from, to, x.EffectiveFrom, x.EffectiveTo))
                    .ToList();
                var primaryOverlapping = overlapping.Where(x => x.IsPrimary).ToList();

                if (primary && primaryOverlapping.Count > 0)
                    throw new InvalidOperationException("MULTIPLE_PRIMARY_TEAM");
                if (!primary && overlapping.Count == 0)
                    throw new InvalidOperationException("PRIMARY_TEAM_REQUIRED");
                if (overlapping.Count > 0 && primaryOverlapping.Count != 1)
                    throw new InvalidOperationException("TEAM_MEMBERSHIP_PRIMARY_DRIFT");

                if (primary)
                {
                    if (!stagedPrimary.TryGetValue(employeeNo, out var primaryRows))
                    {
                        primaryRows = [];
                        stagedPrimary[employeeNo] = primaryRows;
                    }
                    if (primaryRows.Any(x => Overlaps(from, to, x.From, x.To)))
                        throw new InvalidOperationException("MULTIPLE_PRIMARY_TEAM_IN_WORKBOOK");
                    primaryRows.Add((from, to));
                }

                items.Add(Valid(row, key, "Create"));
            }
            catch (Exception ex)
            {
                items.Add(Error(row, key, ex.Message));
            }
        }

        return Preview(items);
    }

    public async Task<V180SimpleBulkConfirmResultDto> ConfirmTeamMembershipAsync(
        CurrentUserDto admin,
        byte[] content,
        CancellationToken ct)
    {
        await V180CurrentAdminWriteGuard.RequireAsync(db,admin,ct);
        var preview = await PreviewTeamMembershipAsync(admin, content, ct);
        if (preview.ErrorCount > 0)
            throw new InvalidOperationException("TEAM_MEMBERSHIP_BULK_HAS_ERRORS");

        var orgId = RequireOrg(admin);
        var rows = ReadWorkbook(content, ("TeamMemberships", TeamMembershipHeaders));
        var result = new V180SimpleBulkConfirmResultDto(0, 0, 0, []);
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await V180CurrentAdminWriteGuard.RequireAsync(db,admin,ct);
            var applied = 0;
            var noChange = 0;

            foreach (var row in rows)
            {
                var employeeNo = Required(row, "EmployeeNo");
                var teamCode = Required(row, "TeamCode");
                var employment = await db.Employments
                    .SingleAsync(x => x.OrganizationId == orgId && x.EmployeeNo == employeeNo, ct);
                var team = await db.Teams
                    .SingleAsync(x => x.OrganizationId == orgId && x.TeamCode == teamCode && x.IsActive, ct);
                var primary = Bool(row, "IsPrimary");
                var from = Date(row, "EffectiveFrom", true)!.Value;
                var to = Date(row, "EffectiveTo", false);

                var changed = await AddMembershipAsync(
                    admin,
                    employment,
                    team.TeamId,
                    primary,
                    from,
                    to,
                    ct);
                if (changed) applied++; else noChange++;
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            result = new V180SimpleBulkConfirmResultDto(applied, noChange, 0, []);
        });

        return result;
    }

    public async Task<V180BatchAddTeamMembersResult> BatchAddTeamMembersAsync(
        CurrentUserDto admin,
        int teamId,
        V180BatchAddTeamMembersRequest request,
        CancellationToken ct)
    {
        await V180CurrentAdminWriteGuard.RequireAsync(db,admin,ct);
        var orgId = RequireOrg(admin);
        if (request.EffectiveFrom == default)
            throw new InvalidOperationException("EffectiveFrom 必填。");
        var ids = (request.UserIds ?? []).Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0)
            throw new InvalidOperationException("請至少選擇一位人員。");

        var team = await db.Teams.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TeamId == teamId && x.OrganizationId == orgId && x.IsActive, ct)
            ?? throw new InvalidOperationException("UNKNOWN_OR_INACTIVE_TEAM");

        var users = await (
            from profile in db.UserIdentityProfiles.AsNoTracking()
            join employment in db.Employments.AsNoTracking()
                on profile.EmploymentId equals employment.EmploymentId
            where ids.Contains(profile.UserId)
                  && profile.UserType == UserTypes.Internal
                  && employment.OrganizationId == orgId
            select new { profile.UserId, Employment = employment })
            .ToListAsync(ct);
        if (users.Count != ids.Length)
            throw new InvalidOperationException("選取人員包含無效、外部或其他 Organization 帳號。");

        var result = new V180BatchAddTeamMembersResult(0, 0);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await V180CurrentAdminWriteGuard.RequireAsync(db,admin,ct);
            var added = 0;
            var noChange = 0;

            foreach (var row in users.OrderBy(x => x.UserId))
            {
                var v17Current = await db.UserTeamAssignments.AsNoTracking()
                    .Where(x => x.UserId == row.UserId
                                && x.EffectiveFrom <= request.EffectiveFrom
                                && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.EffectiveFrom))
                    .ToListAsync(ct);
                var v18Current = await db.TeamMemberships.AsNoTracking()
                    .Where(x => x.EmploymentId == row.Employment.EmploymentId
                                && x.EffectiveFrom <= request.EffectiveFrom
                                && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.EffectiveFrom))
                    .ToListAsync(ct);

                var set17 = v17Current.Select(x => (x.TeamId, x.IsPrimary)).OrderBy(x => x.TeamId).ToArray();
                var set18 = v18Current.Select(x => (x.TeamId, x.IsPrimary)).OrderBy(x => x.TeamId).ToArray();
                if (!set17.SequenceEqual(set18))
                    throw new InvalidOperationException($"TEAM_MEMBERSHIP_MODEL_DRIFT：UserId={row.UserId}");

                if (v18Current.Any(x => x.TeamId == teamId))
                {
                    noChange++;
                    continue;
                }

                var primary = v18Current.Count == 0;
                if (v18Current.Count > 0 && v18Current.Count(x => x.IsPrimary) != 1)
                    throw new InvalidOperationException($"TEAM_MEMBERSHIP_PRIMARY_DRIFT：UserId={row.UserId}");

                var changed = await AddMembershipAsync(
                    admin,
                    row.Employment,
                    teamId,
                    primary,
                    request.EffectiveFrom,
                    null,
                    ct);
                if (changed) added++; else noChange++;
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            result = new V180BatchAddTeamMembersResult(added, noChange);
        });

        return result;
    }

    private async Task<bool> AddMembershipAsync(
        CurrentUserDto admin,
        Employment employment,
        int teamId,
        bool isPrimary,
        DateOnly from,
        DateOnly? to,
        CancellationToken ct)
    {
        var userId = employment.LegacyUserId
            ?? throw new InvalidOperationException("INTERNAL_USER_LINK_MISSING");

        var exact17 = await db.UserTeamAssignments.AnyAsync(x =>
            x.UserId == userId && x.TeamId == teamId
            && x.IsPrimary == isPrimary
            && x.EffectiveFrom == from && x.EffectiveTo == to, ct);
        var exact18 = await db.TeamMemberships.AnyAsync(x =>
            x.EmploymentId == employment.EmploymentId && x.TeamId == teamId
            && x.IsPrimary == isPrimary
            && x.EffectiveFrom == from && x.EffectiveTo == to, ct);

        if (exact17 != exact18)
            throw new InvalidOperationException("TEAM_MEMBERSHIP_MODEL_DRIFT");
        if (exact17)
            return false;

        var overlap17 = await db.UserTeamAssignments.AnyAsync(x =>
            x.UserId == userId && x.TeamId == teamId
            && x.EffectiveFrom <= (to ?? DateOnly.MaxValue)
            && (!x.EffectiveTo.HasValue || from <= x.EffectiveTo.Value), ct);
        var overlap18 = await db.TeamMemberships.AnyAsync(x =>
            x.EmploymentId == employment.EmploymentId && x.TeamId == teamId
            && x.EffectiveFrom <= (to ?? DateOnly.MaxValue)
            && (!x.EffectiveTo.HasValue || from <= x.EffectiveTo.Value), ct);
        if (overlap17 || overlap18)
            throw new InvalidOperationException("OVERLAPPING_TEAM_MEMBERSHIP");

        if (isPrimary)
        {
            var primary17 = await db.UserTeamAssignments.AnyAsync(x =>
                x.UserId == userId && x.IsPrimary
                && x.EffectiveFrom <= (to ?? DateOnly.MaxValue)
                && (!x.EffectiveTo.HasValue || from <= x.EffectiveTo.Value), ct);
            var primary18 = await db.TeamMemberships.AnyAsync(x =>
                x.EmploymentId == employment.EmploymentId && x.IsPrimary
                && x.EffectiveFrom <= (to ?? DateOnly.MaxValue)
                && (!x.EffectiveTo.HasValue || from <= x.EffectiveTo.Value), ct);
            if (primary17 || primary18)
                throw new InvalidOperationException("MULTIPLE_PRIMARY_TEAM");
        }

        db.UserTeamAssignments.Add(new UserTeamAssignment
        {
            UserId = userId,
            TeamId = teamId,
            IsPrimary = isPrimary,
            EffectiveFrom = from,
            EffectiveTo = to,
            AssignedByUserId = admin.UserId,
            CreatedAt = DateTime.UtcNow
        });
        db.TeamMemberships.Add(new TeamMembership
        {
            EmploymentId = employment.EmploymentId,
            TeamId = teamId,
            IsPrimary = isPrimary,
            EffectiveFrom = from,
            EffectiveTo = to,
            ChangeReason = "OwnerPreUatFinalGapClosure",
            AssignedByUserId = admin.UserId
        });

        var today = BusinessTime.Today;
        if (from <= today && (!to.HasValue || to.Value >= today))
        {
            if (isPrimary)
            {
                var otherPrimaryScopes = await db.UserTeamScopes
                    .Where(x => x.UserId == userId && x.TeamId != teamId && x.IsPrimary)
                    .ToListAsync(ct);
                if (otherPrimaryScopes.Count > 0)
                {
                    foreach (var other in otherPrimaryScopes) other.IsPrimary = false;
                    // SQL Server has a filtered unique index for the current primary scope.
                    // Flush the old primary before enabling the new one; outer transaction
                    // still keeps the whole operation atomic.
                    await db.SaveChangesAsync(ct);
                }
            }

            var scope = await db.UserTeamScopes
                .SingleOrDefaultAsync(x => x.UserId == userId && x.TeamId == teamId, ct);
            if (scope is null)
            {
                db.UserTeamScopes.Add(new UserTeamScope
                {
                    UserId = userId,
                    TeamId = teamId,
                    IsPrimary = isPrimary,
                    IsActive = true,
                    AssignedAt = DateTime.UtcNow,
                    AssignedByUserId = admin.UserId
                });
            }
            else
            {
                scope.IsActive = true;
                scope.IsPrimary = isPrimary;
                scope.EndedAt = null;
                scope.AssignedAt = DateTime.UtcNow;
                scope.AssignedByUserId = admin.UserId;
            }

            if (isPrimary)
            {
                var user = await db.Users.SingleAsync(x => x.UserId == userId, ct);
                user.TeamId = teamId;
                user.UpdatedAt = DateTime.UtcNow;
            }
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = admin.UserId,
            EntityType = "TeamMembership",
            EntityId = $"{employment.EmploymentId}:{teamId}:{from:yyyy-MM-dd}",
            Action = "BatchAdd",
            NewValues = JsonSerializer.Serialize(new
            {
                employment.EmployeeNo,
                TeamId = teamId,
                IsPrimary = isPrimary,
                EffectiveFrom = from,
                EffectiveTo = to
            }),
            CreatedAt = DateTime.UtcNow
        });

        return true;
    }

    private static int RequireOrg(CurrentUserDto admin)
        => admin.OrganizationId
           ?? throw new InvalidOperationException("管理者缺少 Organization。");

    private static V180SimpleBulkPreviewDto Preview(
        IReadOnlyList<V180SimpleBulkPreviewItem> items)
        => new(
            items.Count,
            items.Count(x => x.Status == "Valid"),
            items.Count(x => x.Status == "Error"),
            items);

    private static V180SimpleBulkPreviewItem Valid(
        ParsedRow row,
        string key,
        string action)
        => new(row.RowNumber, row.Sheet, key, action, "Valid", null);

    private static V180SimpleBulkPreviewItem Error(
        ParsedRow row,
        string key,
        string error)
        => new(row.RowNumber, row.Sheet, key, "NoChange", "Error", error);

    private static void Period(DateOnly from, DateOnly? to)
    {
        if (to.HasValue && to.Value < from)
            throw new InvalidOperationException("EFFECTIVE_TO_BEFORE_FROM");
    }

    private static bool Overlaps(
        DateOnly from,
        DateOnly? to,
        DateOnly otherFrom,
        DateOnly? otherTo)
        => from <= (otherTo ?? DateOnly.MaxValue)
           && otherFrom <= (to ?? DateOnly.MaxValue);

    private static bool Bool(ParsedRow row, string name)
        => Required(row, name).Trim().ToLowerInvariant() switch
        {
            "y" or "yes" or "true" or "1" or "是" => true,
            "n" or "no" or "false" or "0" or "否" => false,
            _ => throw new InvalidOperationException($"{name}_INVALID_BOOLEAN")
        };

    private static DateOnly? Date(ParsedRow row, string name, bool required)
    {
        var value = Optional(row, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) throw new InvalidOperationException($"{name}_REQUIRED");
            return null;
        }
        if (!DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
            throw new InvalidOperationException($"{name}_INVALID_DATE");
        return date;
    }

    private static string Required(ParsedRow row, string name)
        => Optional(row, name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name}_REQUIRED");

    private static string? Optional(ParsedRow row, string name)
    {
        if (!row.Values.TryGetValue(name, out var value)) return null;
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static byte[] CreateWorkbook(
        params (string Name, IEnumerable<string[]> Rows)[] sheets)
    {
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(
                   stream,
                   DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook,
                   true))
        {
            var wb = doc.AddWorkbookPart();
            wb.Workbook = new Workbook();
            var sheetCollection = wb.Workbook.AppendChild(new Sheets());
            uint id = 1;
            foreach (var spec in sheets)
                AddSheet(wb, sheetCollection, id++, spec.Name, spec.Rows);
            wb.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static void AddSheet(
        WorkbookPart wb,
        Sheets sheets,
        uint id,
        string name,
        IEnumerable<string[]> rows)
    {
        var part = wb.AddNewPart<WorksheetPart>();
        var data = new SheetData();
        part.Worksheet = new Worksheet(data);
        foreach (var values in rows)
        {
            var row = new Row();
            foreach (var value in values)
            {
                row.Append(new Cell
                {
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(value ?? ""))
                });
            }
            data.Append(row);
        }
        part.Worksheet.Save();
        sheets.Append(new Sheet
        {
            Id = wb.GetIdOfPart(part),
            SheetId = id,
            Name = name
        });
    }

    private static IReadOnlyList<ParsedRow> ReadWorkbook(
        byte[] content,
        params (string Name, string[] Headers)[] specs)
    {
        if (content.Length == 0)
            throw new InvalidOperationException("BULK_WORKBOOK_REQUIRED");

        using var stream = new MemoryStream(content);
        using var doc = SpreadsheetDocument.Open(stream, false);
        var workbook = doc.WorkbookPart
            ?? throw new InvalidOperationException("BULK_XLSX_INVALID");

        var result = new List<ParsedRow>();
        foreach (var spec in specs)
        {
            var raw = ReadSheet(workbook, spec.Name);
            if (raw.Count == 0)
                throw new InvalidOperationException($"BULK_HEADER_REQUIRED:{spec.Name}");
            var headers = raw[0].Select(x => x?.Trim() ?? "").ToArray();
            foreach (var required in spec.Headers)
                if (!headers.Contains(required, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"BULK_HEADER_REQUIRED:{spec.Name}:{required}");
            foreach (var header in headers.Where(x => x.Length > 0))
                if (!spec.Headers.Contains(header, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"BULK_HEADER_UNKNOWN:{spec.Name}:{header}");

            for (var i = 1; i < raw.Count; i++)
            {
                var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                var any = false;
                for (var h = 0; h < headers.Length; h++)
                {
                    if (headers[h].Length == 0) continue;
                    var value = h < raw[i].Length ? raw[i][h]?.Trim() : null;
                    values[headers[h]] = value;
                    if (!string.IsNullOrWhiteSpace(value)) any = true;
                }
                if (any) result.Add(new ParsedRow(spec.Name, i + 1, values));
            }
        }
        return result;
    }

    private static IReadOnlyList<string[]> ReadSheet(
        WorkbookPart workbook,
        string sheetName)
    {
        var sheet = workbook.Workbook.Sheets?
            .Elements<Sheet>()
            .FirstOrDefault(x => string.Equals(
                x.Name?.Value,
                sheetName,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Excel 缺少工作表：{sheetName}");

        var part = (WorksheetPart)workbook.GetPartById(sheet.Id!);
        var result = new List<string[]>();
        foreach (var row in part.Worksheet.Descendants<Row>())
        {
            var values = new string[32];
            var sequential = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                var index = ColumnIndex(cell.CellReference?.Value);
                if (index < 0) index = sequential;
                if (index >= 0 && index < values.Length)
                    values[index] = CellText(workbook, cell);
                sequential = Math.Max(sequential + 1, index + 1);
            }
            result.Add(values);
        }
        return result;
    }

    private static string CellText(WorkbookPart workbook, Cell cell)
    {
        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(cell.CellValue?.Text, out var index))
            return workbook.SharedStringTablePart?
                .SharedStringTable?
                .Elements<SharedStringItem>()
                .ElementAtOrDefault(index)?
                .InnerText ?? "";

        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.InnerText ?? "";

        return cell.CellValue?.Text ?? cell.InnerText ?? "";
    }

    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return -1;
        var value = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch)) break;
            value = value * 26 + char.ToUpperInvariant(ch) - 'A' + 1;
        }
        return value > 0 ? value - 1 : -1;
    }

    private sealed record ParsedRow(
        string Sheet,
        int RowNumber,
        Dictionary<string, string?> Values);
}
