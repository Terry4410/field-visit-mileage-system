using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class WorkbookImportService(AppDbContext db) : IWorkbookImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record LocationImportRow(string? LocationCode, string? TeamCode, string LocationName, string? City, string? District, string? Address, string? PlusCode, string Status);
    private sealed record ProjectImportRow(string ProjectCode, string ProjectName, string? TeamCode, string LocationMode, string? StartDate, string? EndDate, string Status, string? Description);
    private sealed record ProjectLocationImportRow(string ProjectCode, string LocationCode, bool IsPrimary, string Status);

    public Task<ReportExportContext> CreateTemplateAsync(CurrentUserDto user, string importType, CancellationToken ct)
    {
        importType = NormalizeType(importType);
        if (importType == "projects" && !HasRole(user, "admin")) throw new UnauthorizedAccessException("只有管理者可以匯入專案。");
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook, true))
        {
            var wb = doc.AddWorkbookPart(); wb.Workbook = new Workbook(); var sheets = wb.Workbook.AppendChild(new Sheets());
            if (importType == "locations")
            {
                AddSheet(wb, sheets, 1, "Locations", new[]
                {
                    new[] { "LocationCode", "TeamCode", "LocationName", "City", "District", "Address", "PlusCode" },
                    new[] { "", "TEAM-N01", "客戶A", "台北市", "內湖區", "台北市內湖區示例路1號", "" }
                });
                AddSheet(wb, sheets, 2, "說明", new[]
                {
                    new[] { "項目", "規則" },
                    new[] { "LocationCode", "新增地點請留空；更新既有地點時必須填寫現有 LocationCode。" },
                    new[] { "TeamCode", "選填；若填寫必須是目前啟用且有權限的小組代碼。" },
                    new[] { "LocationName", "必填。" },
                    new[] { "Address / PlusCode", "至少需填一項。" },
                    new[] { "新增地點", "Excel 匯入後一律為正式地點資料，但 ApprovalStatus=Pending、GeocodingStatus=Pending、IsActive=false；完成解析與核准後才可使用。" },
                    new[] { "更新既有地點", "只要主檔內容有變更，匯入後會重新進入 Pending / Pending / 未啟用，需重新解析與核准。" },
                    new[] { "重複資料", "同一 Excel 不可重複使用相同 LocationCode；新增資料不可在同一小組重複相同地點名稱＋地址／PlusCode。" },
                    new[] { "Status", "新版範本已移除。舊版檔案若仍有 Status 欄可相容讀取，但不會直接控制啟用或核准狀態。" }
                });
            }
            else
            {
                AddSheet(wb, sheets, 1, "Projects", new[]
                {
                    new[] { "ProjectCode", "ProjectName", "TeamCode", "LocationMode", "StartDate", "EndDate", "Status", "Description" },
                    new[] { "CARE-001", "高齡關懷訪視專案", "TEAM-N01", "List", "2026-01-01", "", "Active", "範例" }
                });
                AddSheet(wb, sheets, 2, "ProjectLocations", new[]
                {
                    new[] { "ProjectCode", "LocationCode", "Status" },
                    new[] { "CARE-001", "LOC-000001", "Active" }
                });
                AddSheet(wb, sheets, 3, "說明", new[]
                {
                    new[] { "項目", "規則" },
                    new[] { "Projects.ProjectCode", "必填；同一 Excel 不可重複。" },
                    new[] { "Projects.TeamCode", "選填；若填寫必須是目前啟用的小組代碼。" },
                    new[] { "Projects.LocationMode", "只接受 List / SelfMaintained（亦相容：專案清單優先 / 臨時維護優先）。" },
                    new[] { "Projects.StartDate / EndDate", "可留空；接受 yyyy-MM-dd、yyyy/MM/dd 或 Excel 真正日期；結束日期不得早於開始日期。" },
                    new[] { "Status", "只接受 Active / Inactive / 啟用 / 停用 / 1 / 0。" },
                    new[] { "ProjectLocations.LocationCode", "啟用關聯時只允許正式、啟用、Approved，且符合專案小組範圍的地點。" },
                    new[] { "ProjectLocations", "同一 ProjectCode + LocationCode 不可重複；每個專案最多 500 個啟用固定地點。" },
                    new[] { "IsPrimary", "舊版欄位仍可讀取，但新版不再使用；匯入後一律視為非主要地點。" }
                });
            }
            wb.Workbook.Save();
        }
        var name = importType == "locations" ? "地點主檔匯入範例.xlsx" : "專案主檔匯入範例.xlsx";
        return Task.FromResult(new ReportExportContext(name, stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    }

    public async Task<ImportPreviewDto> PreviewAsync(CurrentUserDto user, string importType, byte[] content, CancellationToken ct)
    {
        importType = NormalizeType(importType);
        if (content.Length == 0) throw new InvalidOperationException("上傳檔案為空。");
        if (importType == "projects" && !HasRole(user, "admin")) throw new UnauthorizedAccessException("只有管理者可以匯入專案。");
        var orgId = user.OrganizationId ?? throw new InvalidOperationException("目前帳號缺少 OrganizationId。");
        var batch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(), ImportType = importType, OrganizationId = orgId, RequestedByUserId = user.UserId,
            Status = "Previewed", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(4)
        };
        var preview = new List<ImportPreviewItemDto>();
        using var stream = new MemoryStream(content);
        using var doc = SpreadsheetDocument.Open(stream, false);
        if (importType == "locations") await PreviewLocationsAsync(doc, user, batch, preview, ct);
        else await PreviewProjectsAsync(doc, user, batch, preview, ct);
        batch.TotalCount = preview.Count;
        batch.ValidCount = preview.Count(x => x.Status == "Valid");
        batch.ErrorCount = preview.Count(x => x.Status == "Error");
        await db.ImportBatches.AddAsync(batch, ct);
        await db.SaveChangesAsync(ct);
        AddAudit(user.UserId, "ImportBatch", batch.ImportBatchId.ToString(), "ImportPreview", new { batch.ImportType, batch.TotalCount, batch.ValidCount, batch.ErrorCount });
        await db.SaveChangesAsync(ct);
        return new ImportPreviewDto(batch.ImportBatchId, importType, batch.TotalCount, batch.ValidCount, batch.ErrorCount, preview);
    }

    public async Task<ReportExportContext> CreateErrorReportAsync(CurrentUserDto user, Guid importBatchId, CancellationToken ct)
    {
        var batch = await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(x => x.ImportBatchId == importBatchId, ct)
            ?? throw new KeyNotFoundException("找不到匯入批次。");
        if (batch.OrganizationId != user.OrganizationId || batch.RequestedByUserId != user.UserId)
            throw new UnauthorizedAccessException("只能下載自己建立的匯入錯誤報告。");
        var errors = await db.ImportBatchItems.AsNoTracking()
            .Where(x => x.ImportBatchId == importBatchId && x.Status == "Error")
            .OrderBy(x => x.RowNumber).ThenBy(x => x.EntityType)
            .ToListAsync(ct);
        if (errors.Count == 0) throw new InvalidOperationException("此匯入批次沒有錯誤資料。");

        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook, true))
        {
            var wb = doc.AddWorkbookPart(); wb.Workbook = new Workbook(); var sheets = wb.Workbook.AppendChild(new Sheets());
            var rows = new List<string[]>
            {
                new[] { "RowNumber", "EntityType", "DisplayKey", "Action", "ErrorMessage" }
            };
            rows.AddRange(errors.Select(x => new[] { x.RowNumber.ToString(CultureInfo.InvariantCulture), x.EntityType, x.DisplayKey ?? "", x.Action ?? "", x.ErrorMessage ?? "" }));
            AddSheet(wb, sheets, 1, "Errors", rows);
            wb.Workbook.Save();
        }
        AddAudit(user.UserId, "ImportBatch", importBatchId.ToString(), "ImportErrorReport", new { Count = errors.Count });
        await db.SaveChangesAsync(ct);
        return new ReportExportContext($"匯入錯誤_{batch.ImportType}_{importBatchId:N}.xlsx", stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    public async Task<ImportConfirmResultDto> ConfirmAsync(CurrentUserDto user, Guid importBatchId, CancellationToken ct)
    {
        var batch = await db.ImportBatches.FirstOrDefaultAsync(x => x.ImportBatchId == importBatchId, ct) ?? throw new KeyNotFoundException("找不到匯入批次。");
        if (batch.OrganizationId != user.OrganizationId || batch.RequestedByUserId != user.UserId) throw new UnauthorizedAccessException("只能確認自己建立的匯入預覽。");
        if (batch.Status != "Previewed") throw new InvalidOperationException("此匯入批次已處理或不可使用。");
        if (batch.ExpiresAt < DateTime.UtcNow) throw new InvalidOperationException("匯入預覽已逾時，請重新上傳。");
        if (batch.ErrorCount > 0) throw new InvalidOperationException("預覽仍有錯誤資料，請修正 Excel 後重新上傳。");
        if (batch.ImportType == "projects" && !HasRole(user, "admin")) throw new UnauthorizedAccessException("只有管理者可以確認專案匯入。");

        var items = await db.ImportBatchItems.Where(x => x.ImportBatchId == importBatchId && x.Status == "Valid").OrderBy(x => x.EntityType == "ProjectLocation" ? 2 : 1).ThenBy(x => x.RowNumber).ToListAsync(ct);
        int created = 0, updated = 0, unchanged = 0, failed = 0;
        var errors = new List<string>();
        foreach (var item in items)
        {
            try
            {
                if (item.Action == "NoChange") { unchanged++; item.Status = "Applied"; await db.SaveChangesAsync(ct); continue; }
                if (item.EntityType == "Location")
                {
                    var data = JsonSerializer.Deserialize<LocationImportRow>(item.DataJson, JsonOptions)!;
                    if (item.Action == "Create")
                    {
                        var teamId = await ResolveTeamIdAsync(user, data.TeamCode, ct);
                        // Excel imports can never bypass the location lifecycle:
                        // new Locations always require geocoding and approval.
                        await db.Locations.AddAsync(new FieldVisit.Domain.Entities.Location
                        {
                            OrganizationId = user.OrganizationId, TeamId = teamId, LocationCode = NewLocationCode(), LocationName = data.LocationName.Trim(),
                            LocationType = "Customer", City = data.City?.Trim(), District = data.District?.Trim(), Address = data.Address?.Trim(), PlusCode = data.PlusCode?.Trim(),
                            IsTemporary = false, ApprovalStatus = "Pending", GeocodingStatus = "Pending", CreatedByUserId = user.UserId,
                            IsActive = false, CreatedAt = DateTime.UtcNow
                        }, ct);
                        created++;
                    }
                    else
                    {
                        var row = await db.Locations.FirstAsync(x => x.OrganizationId == user.OrganizationId && x.LocationCode == data.LocationCode, ct);
                        EnsureLeaderTeam(user, row.TeamId);
                        // Any imported master-data change reopens geocoding /
                        // approval before the Location can be used again.
                        row.TeamId = await ResolveTeamIdAsync(user, data.TeamCode, ct);
                        row.LocationName = data.LocationName.Trim(); row.City = data.City?.Trim(); row.District = data.District?.Trim(); row.Address = data.Address?.Trim(); row.PlusCode = data.PlusCode?.Trim();
                        row.GeocodingStatus = "Pending"; row.ApprovalStatus = "Pending"; row.IsActive = false; row.UpdatedAt = DateTime.UtcNow;
                        updated++;
                    }
                }
                else if (item.EntityType == "Project")
                {
                    var data = JsonSerializer.Deserialize<ProjectImportRow>(item.DataJson, JsonOptions)!;
                    var teamId = await ResolveTeamIdAsync(user, data.TeamCode, ct);
                    var row = await db.Projects.FirstOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.ProjectCode == data.ProjectCode, ct);
                    if (row is null)
                    {
                        row = new Project { OrganizationId = user.OrganizationId!.Value, ProjectCode = data.ProjectCode.Trim(), CreatedAt = DateTime.UtcNow };
                        await db.Projects.AddAsync(row, ct); created++;
                    }
                    else updated++;
                    row.TeamId = teamId; row.ProjectName = data.ProjectName.Trim(); row.LocationMode = NormalizeLocationMode(data.LocationMode); row.Description = data.Description?.Trim();
                    row.StartDate = ParseDate(data.StartDate); row.EndDate = ParseDate(data.EndDate); row.IsActive = ParseActive(data.Status); row.UpdatedAt = DateTime.UtcNow;
                }
                else if (item.EntityType == "ProjectLocation")
                {
                    var data = JsonSerializer.Deserialize<ProjectLocationImportRow>(item.DataJson, JsonOptions)!;
                    var project = await db.Projects.FirstAsync(
                        x => x.OrganizationId == user.OrganizationId
                             && x.ProjectCode == data.ProjectCode,
                        ct);

                    var location = await db.Locations.FirstOrDefaultAsync(
                        x => (x.OrganizationId == user.OrganizationId || x.OrganizationId == null)
                             && x.LocationCode == data.LocationCode,
                        ct)
                        ?? throw new InvalidOperationException($"找不到地點代碼 {data.LocationCode}。");

                    var requestedActive = ParseActive(data.Status);

                    if (requestedActive)
                    {
                        EnsureProjectLocationEligible(project, location);

                        var currentActiveCount = await db.ProjectLocations
                            .CountAsync(x => x.ProjectId == project.ProjectId && x.IsActive, ct);

                        var existingActive = await db.ProjectLocations.AnyAsync(
                            x => x.ProjectId == project.ProjectId
                                 && x.LocationId == location.LocationId
                                 && x.IsActive,
                            ct);

                        if (!existingActive
                            && currentActiveCount >= V170ProjectLocationAdminRules.MaxAssignedLocations)
                            throw new InvalidOperationException(
                                $"單一專案最多可設定 {V170ProjectLocationAdminRules.MaxAssignedLocations} 個固定地點。");
                    }

                    var link = await db.ProjectLocations
                        .FirstOrDefaultAsync(
                            x => x.ProjectId == project.ProjectId
                                 && x.LocationId == location.LocationId,
                            ct);

                    if (link is null)
                    {
                        await db.ProjectLocations.AddAsync(
                            new ProjectLocation
                            {
                                ProjectId = project.ProjectId,
                                LocationId = location.LocationId,
                                IsPrimary = false,
                                IsActive = requestedActive,
                                CreatedAt = DateTime.UtcNow
                            },
                            ct);
                        created++;
                    }
                    else
                    {
                        link.IsPrimary = false;
                        link.IsActive = requestedActive;
                        updated++;
                    }
                }
                item.Status = "Applied";
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                failed++; item.Status = "Failed"; item.ErrorMessage = ex.Message; errors.Add($"第 {item.RowNumber} 列：{ex.Message}");
                await db.SaveChangesAsync(ct);
            }
        }
        batch.Status = failed == 0 ? "Confirmed" : "PartiallyFailed";
        batch.ConfirmedAt = DateTime.UtcNow;
        AddAudit(user.UserId, "ImportBatch", batch.ImportBatchId.ToString(), "ImportConfirm", new { created, updated, unchanged, failed });
        await db.SaveChangesAsync(ct);
        return new ImportConfirmResultDto(batch.ImportBatchId, created, updated, unchanged, failed, errors);
    }

    private async Task PreviewLocationsAsync(SpreadsheetDocument doc, CurrentUserDto user, ImportBatch batch, List<ImportPreviewItemDto> preview, CancellationToken ct)
    {
        var rows = ReadSheet(doc, "Locations");
        var workbookLocationCodes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workbookNewLocationKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rowNo = 1;
        foreach (var raw in rows.Skip(1))
        {
            rowNo++;
            if (raw.All(string.IsNullOrWhiteSpace)) continue;

            var headers = rows[0];
            var data = new LocationImportRow(
                Get(raw, headers, "LocationCode", "地點代碼"),
                Get(raw, headers, "TeamCode", "小組代碼"),
                Get(raw, headers, "LocationName", "地點名稱") ?? "",
                Get(raw, headers, "City", "縣市"),
                Get(raw, headers, "District", "鄉鎮區"),
                Get(raw, headers, "Address", "地址"),
                Get(raw, headers, "PlusCode", "Plus Code"),
                // Legacy column compatibility only. Location Status never
                // bypasses geocoding / approval during Confirm.
                Get(raw, headers, "Status", "狀態") ?? "Active");

            var error = await ValidateLocationAsync(user, data, ct);
            var action = "Create";
            var code = data.LocationCode?.Trim();
            int? teamId = null;

            if (error is null)
            {
                teamId = await ResolveTeamIdAsync(user, data.TeamCode, ct);

                if (!string.IsNullOrWhiteSpace(code))
                {
                    if (!workbookLocationCodes.Add(code))
                    {
                        error = $"Excel 內 LocationCode 重複：{code}。";
                    }
                    else
                    {
                        var existing = await db.Locations
                            .AsNoTracking()
                            .FirstOrDefaultAsync(
                                x => x.OrganizationId == user.OrganizationId
                                     && x.LocationCode == code,
                                ct);

                        if (existing is null)
                        {
                            error = "LocationCode 不存在；新增地點請留空 LocationCode。";
                        }
                        else
                        {
                            action =
                                SameLocation(existing, data)
                                && existing.TeamId == teamId
                                    ? "NoChange"
                                    : "Update";
                        }
                    }
                }
                else
                {
                    var newLocationKey =
                        $"{teamId?.ToString(CultureInfo.InvariantCulture) ?? "ALL"}|"
                        + $"{data.LocationName.Trim()}|"
                        + $"{(data.Address ?? "").Trim()}|"
                        + $"{(data.PlusCode ?? "").Trim()}";

                    if (!workbookNewLocationKeys.Add(newLocationKey))
                    {
                        error =
                            "Excel 內有重複新增地點：同一小組的地點名稱與地址／PlusCode 相同。";
                    }
                    else
                    {
                        var same = await db.Locations
                            .AsNoTracking()
                            .FirstOrDefaultAsync(
                                x => x.OrganizationId == user.OrganizationId
                                     && x.TeamId == teamId
                                     && x.LocationName == data.LocationName
                                     && x.Address == data.Address
                                     && x.PlusCode == data.PlusCode,
                                ct);

                        if (same is not null)
                        {
                            // Re-uploading the same new-location row is
                            // idempotent and must not create a duplicate.
                            action = "NoChange";
                        }
                        else if (await db.Locations
                                     .AsNoTracking()
                                     .AnyAsync(
                                         x => x.OrganizationId == user.OrganizationId
                                              && x.TeamId == teamId
                                              && x.LocationName == data.LocationName,
                                         ct))
                        {
                            error =
                                "同小組已有相同地點名稱；若要更新既有地點，請先下載資料並使用 LocationCode。";
                        }
                    }
                }
            }

            await StageAsync(
                batch,
                preview,
                rowNo,
                "Location",
                action,
                code ?? data.LocationName,
                data,
                error,
                ct);
        }
    }

    private async Task PreviewProjectsAsync(SpreadsheetDocument doc, CurrentUserDto user, ImportBatch batch, List<ImportPreviewItemDto> preview, CancellationToken ct)
    {
        var projectRows = ReadSheet(doc, "Projects");
        var workbookProjectCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workbookProjects = new Dictionary<string, ProjectImportRow>(StringComparer.OrdinalIgnoreCase);

        var rowNo = 1;
        foreach (var raw in projectRows.Skip(1))
        {
            rowNo++;
            if (raw.All(string.IsNullOrWhiteSpace)) continue;

            var headers = projectRows[0];
            var data = new ProjectImportRow(
                Get(raw, headers, "ProjectCode", "專案代碼") ?? "",
                Get(raw, headers, "ProjectName", "專案名稱") ?? "",
                Get(raw, headers, "TeamCode", "小組代碼"),
                Get(raw, headers, "LocationMode", "預設地點方式") ?? "List",
                Get(raw, headers, "StartDate", "開始日期"),
                Get(raw, headers, "EndDate", "結束日期"),
                Get(raw, headers, "Status", "狀態") ?? "Active",
                Get(raw, headers, "Description", "說明"));

            var code = data.ProjectCode.Trim();
            string? error = null;

            if (string.IsNullOrWhiteSpace(code)
                || string.IsNullOrWhiteSpace(data.ProjectName))
            {
                error = "ProjectCode 與 ProjectName 為必填。";
            }
            else if (!workbookProjectCodes.Add(code))
            {
                error = $"Excel 內 ProjectCode 重複：{code}。";
            }
            else
            {
                workbookProjects[code] = data;
                try
                {
                    _ = NormalizeLocationMode(data.LocationMode);
                    var startDate = ParseDate(data.StartDate);
                    var endDate = ParseDate(data.EndDate);
                    _ = ParseActive(data.Status);

                    if (startDate.HasValue
                        && endDate.HasValue
                        && endDate.Value < startDate.Value)
                        throw new InvalidOperationException(
                            "結束日期不得早於開始日期。");

                    if (!string.IsNullOrWhiteSpace(data.TeamCode))
                        _ = await ResolveTeamIdAsync(user, data.TeamCode, ct);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }

            var existing = error is null
                ? await db.Projects.AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.OrganizationId == user.OrganizationId
                             && x.ProjectCode == code,
                        ct)
                : null;

            var requestedTeamId = error is null
                ? await ResolveTeamIdAsync(user, data.TeamCode, ct)
                : null;

            var action = existing is null
                ? "Create"
                : SameProject(existing, data)
                  && existing.TeamId == requestedTeamId
                    ? "NoChange"
                    : "Update";

            await StageAsync(
                batch,
                preview,
                rowNo,
                "Project",
                action,
                code,
                data,
                error,
                ct);
        }

        var linkRows = ReadSheet(doc, "ProjectLocations", optional: true);
        if (linkRows.Count == 0) return;

        var workbookLinkKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var pendingLinks =
            new List<(int RowNo, ProjectLocationImportRow Data, string? Error)>();

        rowNo = 1;
        foreach (var raw in linkRows.Skip(1))
        {
            rowNo++;
            if (raw.All(string.IsNullOrWhiteSpace)) continue;

            var headers = linkRows[0];
            var data = new ProjectLocationImportRow(
                Get(raw, headers, "ProjectCode", "專案代碼") ?? "",
                Get(raw, headers, "LocationCode", "地點代碼") ?? "",
                ParseYes(Get(raw, headers, "IsPrimary", "主要地點")),
                Get(raw, headers, "Status", "狀態") ?? "Active");

            var projectCode = data.ProjectCode.Trim();
            var locationCode = data.LocationCode.Trim();
            var key = $"{projectCode}/{locationCode}";
            string? error = null;
            bool requestedActive = false;

            if (string.IsNullOrWhiteSpace(projectCode)
                || string.IsNullOrWhiteSpace(locationCode))
            {
                error = "ProjectCode 與 LocationCode 為必填。";
            }
            else if (!workbookLinkKeys.Add(key))
            {
                error = $"Excel 內專案固定地點重複：{key}。";
            }
            else
            {
                try
                {
                    requestedActive = ParseActive(data.Status);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }

            int? requestedProjectTeamId = null;

            if (error is null)
            {
                if (workbookProjects.TryGetValue(projectCode, out var workbookProject))
                {
                    try
                    {
                        requestedProjectTeamId =
                            await ResolveTeamIdAsync(
                                user,
                                workbookProject.TeamCode,
                                ct);
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                }
                else
                {
                    var existingProject = await db.Projects.AsNoTracking()
                        .FirstOrDefaultAsync(
                            x => x.OrganizationId == user.OrganizationId
                                 && x.ProjectCode == projectCode,
                            ct);

                    if (existingProject is null)
                        error = "ProjectCode 不存在。";
                    else
                        requestedProjectTeamId = existingProject.TeamId;
                }
            }

            if (error is null)
            {
                var location = await db.Locations.AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => (x.OrganizationId == user.OrganizationId
                              || x.OrganizationId == null)
                             && x.LocationCode == locationCode,
                        ct);

                if (location is null)
                {
                    error = "LocationCode 不存在。";
                }
                else if (requestedActive)
                {
                    if (location.IsTemporary)
                        error = "專案固定地點不可使用臨時地點；請先由管理者轉為正式地點。";
                    else if (!location.IsActive)
                        error = "專案固定地點必須為啟用地點。";
                    else if (!string.Equals(
                                 location.ApprovalStatus,
                                 "Approved",
                                 StringComparison.OrdinalIgnoreCase))
                        error = "專案固定地點必須為 Approved。";
                    else if (requestedProjectTeamId.HasValue
                             && location.TeamId.HasValue
                             && location.TeamId != requestedProjectTeamId)
                        error = "地點不符合專案歸屬小組範圍。";
                }
            }

            pendingLinks.Add((rowNo, data, error));
        }

        foreach (var projectGroup in pendingLinks
                     .Where(x => x.Error is null)
                     .GroupBy(
                         x => x.Data.ProjectCode.Trim(),
                         StringComparer.OrdinalIgnoreCase))
        {
            var projectCode = projectGroup.Key;
            var dbProject = await db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.OrganizationId == user.OrganizationId
                         && x.ProjectCode == projectCode,
                    ct);

            var activeCodes =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (dbProject is not null)
            {
                var existingCodes = await (
                    from link in db.ProjectLocations.AsNoTracking()
                    join location in db.Locations.AsNoTracking()
                        on link.LocationId equals location.LocationId
                    where link.ProjectId == dbProject.ProjectId
                          && link.IsActive
                    select location.LocationCode)
                    .Where(x => x != null)
                    .ToListAsync(ct);

                foreach (var code in existingCodes)
                    if (!string.IsNullOrWhiteSpace(code))
                        activeCodes.Add(code!);
            }

            foreach (var row in projectGroup)
            {
                if (ParseActive(row.Data.Status))
                    activeCodes.Add(row.Data.LocationCode.Trim());
                else
                    activeCodes.Remove(row.Data.LocationCode.Trim());
            }

            if (activeCodes.Count
                > V170ProjectLocationAdminRules.MaxAssignedLocations)
            {
                var message =
                    $"單一專案最多可設定 {V170ProjectLocationAdminRules.MaxAssignedLocations} 個固定地點；"
                    + $"目前匯入後預計為 {activeCodes.Count} 個。";

                for (var i = 0; i < pendingLinks.Count; i++)
                {
                    var row = pendingLinks[i];
                    if (row.Error is null
                        && row.Data.ProjectCode.Trim().Equals(
                            projectCode,
                            StringComparison.OrdinalIgnoreCase))
                        pendingLinks[i] = (row.RowNo, row.Data, message);
                }
            }
        }

        foreach (var row in pendingLinks)
        {
            var action = "Create";

            if (row.Error is null)
            {
                var projectCode = row.Data.ProjectCode.Trim();
                var locationCode = row.Data.LocationCode.Trim();
                var requestedActive = ParseActive(row.Data.Status);

                var project = await db.Projects
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.OrganizationId == user.OrganizationId
                             && x.ProjectCode == projectCode,
                        ct);

                // If the Project does not exist in DB yet but was validly staged
                // in this same workbook, the relation is a true Create.
                if (project is not null)
                {
                    var location = await db.Locations
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x => (x.OrganizationId == user.OrganizationId
                                  || x.OrganizationId == null)
                                 && x.LocationCode == locationCode,
                            ct);

                    if (location is not null)
                    {
                        var existingLink = await db.ProjectLocations
                            .AsNoTracking()
                            .FirstOrDefaultAsync(
                                x => x.ProjectId == project.ProjectId
                                     && x.LocationId == location.LocationId,
                                ct);

                        if (existingLink is null)
                        {
                            action = "Create";
                        }
                        else if (existingLink.IsActive == requestedActive
                                 && !existingLink.IsPrimary)
                        {
                            action = "NoChange";
                        }
                        else
                        {
                            action = "Update";
                        }
                    }
                }
            }

            await StageAsync(
                batch,
                preview,
                row.RowNo,
                "ProjectLocation",
                action,
                $"{row.Data.ProjectCode.Trim()}/{row.Data.LocationCode.Trim()}",
                row.Data,
                row.Error,
                ct);
        }
    }

    private async Task StageAsync<T>(ImportBatch batch, List<ImportPreviewItemDto> preview, int rowNo, string entityType, string action, string key, T data, string? error, CancellationToken ct)
    {
        var status = error is null ? "Valid" : "Error";
        await db.ImportBatchItems.AddAsync(new ImportBatchItem { ImportBatchId = batch.ImportBatchId, RowNumber = rowNo, EntityType = entityType, Action = action, Status = status, DisplayKey = key, DataJson = JsonSerializer.Serialize(data, JsonOptions), ErrorMessage = error, CreatedAt = DateTime.UtcNow }, ct);
        preview.Add(new ImportPreviewItemDto(rowNo, entityType, action, status, key, error));
    }

    private async Task<string?> ValidateLocationAsync(CurrentUserDto user, LocationImportRow row, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(row.LocationName)) return "LocationName 為必填。";
        if (string.IsNullOrWhiteSpace(row.Address) && string.IsNullOrWhiteSpace(row.PlusCode)) return "Address 與 PlusCode 至少需要一項。";
        try { _ = ParseActive(row.Status); }
        catch (Exception ex) { return ex.Message; }
        if (!string.IsNullOrWhiteSpace(row.TeamCode))
        {
            try { _ = await ResolveTeamIdAsync(user, row.TeamCode, ct); }
            catch (Exception ex) { return ex.Message; }
        }
        return null;
    }

    private static void EnsureProjectLocationEligible(
        Project project,
        FieldVisit.Domain.Entities.Location location)
    {
        if (location.IsTemporary)
            throw new InvalidOperationException(
                "專案固定地點不可使用臨時地點；請先由管理者轉為正式地點。");

        if (!location.IsActive)
            throw new InvalidOperationException(
                "專案固定地點必須為啟用地點。");

        if (!string.Equals(
                location.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "專案固定地點必須為 Approved。");

        if (project.TeamId.HasValue
            && location.TeamId.HasValue
            && project.TeamId != location.TeamId)
            throw new InvalidOperationException(
                "地點不符合專案歸屬小組範圍。");
    }

    private async Task<int?> ResolveTeamIdAsync(CurrentUserDto user, string? teamCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(teamCode)) return null;
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(x => x.OrganizationId == user.OrganizationId && x.TeamCode == teamCode && x.IsActive, ct) ?? throw new InvalidOperationException($"找不到小組代碼 {teamCode}。");
        if (HasRole(user, "leader") && !user.TeamIds.Contains(team.TeamId)) throw new UnauthorizedAccessException($"無權匯入小組 {team.TeamName} 的資料。");
        return team.TeamId;
    }

    private static List<List<string?>> ReadSheet(SpreadsheetDocument doc, string name, bool optional = false)
    {
        var wb = doc.WorkbookPart ?? throw new InvalidOperationException("Excel 缺少 Workbook。");
        var sheet = wb.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault(x => string.Equals(x.Name?.Value, name, StringComparison.OrdinalIgnoreCase));
        if (sheet is null)
        {
            if (optional) return [];
            throw new InvalidOperationException($"Excel 缺少工作表 {name}。");
        }
        var ws = (WorksheetPart)wb.GetPartById(sheet.Id?.Value ?? throw new InvalidOperationException($"工作表 {name} 缺少 Relationship Id。"));
        var shared = wb.SharedStringTablePart?.SharedStringTable;
        var result = new List<List<string?>>();
        foreach (var row in ws.Worksheet.Descendants<Row>())
        {
            var values = new List<string?>();
            foreach (var cell in row.Elements<Cell>())
            {
                var col = ColumnIndex(cell.CellReference?.Value);
                while (values.Count < col) values.Add(null);
                values[col - 1] = CellText(cell, shared);
            }
            result.Add(values);
        }
        return result;
    }

    private static string? CellText(Cell cell, SharedStringTable? shared)
    {
        if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString?.Text?.Text ?? cell.InlineString?.InnerText;
        var raw = cell.CellValue?.Text;
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(raw, out var i)) return shared?.Elements<SharedStringItem>().ElementAtOrDefault(i)?.InnerText;
        return raw;
    }

    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return 1;
        var letters = new string(reference.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        var n = 0; foreach (var c in letters) n = n * 26 + (c - 'A' + 1); return Math.Max(1, n);
    }

    private static string? Get(IReadOnlyList<string?> row, IReadOnlyList<string?> headers, params string[] aliases)
    {
        for (var i = 0; i < headers.Count; i++) if (aliases.Any(a => string.Equals(headers[i]?.Trim(), a, StringComparison.OrdinalIgnoreCase))) return i < row.Count ? row[i]?.Trim() : null;
        return null;
    }

    private static bool SameLocation(FieldVisit.Domain.Entities.Location x, LocationImportRow r) => x.LocationName == r.LocationName && x.City == r.City && x.District == r.District && x.Address == r.Address && x.PlusCode == r.PlusCode;
    private static bool SameProject(Project x, ProjectImportRow r) => x.ProjectName == r.ProjectName && x.LocationMode == NormalizeLocationMode(r.LocationMode) && x.StartDate == ParseDate(r.StartDate) && x.EndDate == ParseDate(r.EndDate) && x.IsActive == ParseActive(r.Status) && x.Description == r.Description;
    private static string NormalizeType(string value) => value.Trim().ToLowerInvariant() switch { "location" or "locations" => "locations", "project" or "projects" => "projects", _ => throw new InvalidOperationException("匯入類型只支援 locations 或 projects。") };
    private static string NormalizeLocationMode(string value) => value.Trim().ToLowerInvariant() switch { "list" or "清單" or "專案清單優先" => "List", "selfmaintained" or "self-maintained" or "自行維護" or "臨時維護優先" => "SelfMaintained", _ => throw new InvalidOperationException("LocationMode 只支援 List 或 SelfMaintained。") };
    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();
        var formats = new[]
        {
            "yyyy-MM-dd",
            "yyyy/M/d",
            "yyyy/MM/dd",
            "M/d/yyyy",
            "MM/dd/yyyy"
        };

        if (DateOnly.TryParseExact(
                text,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var exact))
            return exact;

        if (double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var serial))
        {
            try
            {
                return DateOnly.FromDateTime(DateTime.FromOADate(serial));
            }
            catch (ArgumentException)
            {
            }
        }

        throw new InvalidOperationException(
            $"日期格式錯誤：{value}；請使用 yyyy-MM-dd、yyyy/MM/dd 或 Excel 日期格式。");
    }

    private static bool ParseActive(string? value)
    {
        var text = (value ?? "Active").Trim();

        if (text.Equals("Active", StringComparison.OrdinalIgnoreCase)
            || text.Equals("啟用", StringComparison.OrdinalIgnoreCase)
            || text == "1")
            return true;

        if (text.Equals("Inactive", StringComparison.OrdinalIgnoreCase)
            || text.Equals("停用", StringComparison.OrdinalIgnoreCase)
            || text == "0")
            return false;

        throw new InvalidOperationException(
            $"Status 不合法：{value}；只接受 Active / Inactive / 啟用 / 停用 / 1 / 0。");
    }
    private static bool ParseYes(string? value) => (value ?? "").Trim().ToLowerInvariant() is "y" or "yes" or "true" or "1" or "是";
    private static bool HasRole(CurrentUserDto user, string role) => user.Roles.Any(x => x.Equals(role, StringComparison.OrdinalIgnoreCase));
    private static string NewLocationCode() => $"LOC-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
    private static void EnsureLeaderTeam(CurrentUserDto user, int? teamId) { if (HasRole(user, "leader") && (!teamId.HasValue || !user.TeamIds.Contains(teamId.Value))) throw new UnauthorizedAccessException("無權更新未授權小組地點。"); }

    private void AddAudit(int userId, string entityType, string? entityId, string action, object value) => db.AuditLogs.Add(new AuditLog { UserId = userId, EntityType = entityType, EntityId = entityId, Action = action, NewValues = JsonSerializer.Serialize(value, JsonOptions), CorrelationId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow });

    private static void AddSheet(WorkbookPart wb, Sheets sheets, uint id, string name, IEnumerable<string[]> rows)
    {
        var ws = wb.AddNewPart<WorksheetPart>(); var data = new SheetData();
        foreach (var values in rows)
        {
            var row = new Row(); foreach (var value in values) row.Append(new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value)) }); data.Append(row);
        }
        ws.Worksheet = new Worksheet(data); ws.Worksheet.Save(); sheets.Append(new Sheet { Id = wb.GetIdOfPart(ws), SheetId = id, Name = name });
    }
}
