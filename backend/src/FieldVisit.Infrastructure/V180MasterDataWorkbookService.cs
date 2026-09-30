using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180MasterDataWorkbookService(
    AppDbContext db,
    IV180MasterDataAdminRepository repository)
    : IV180MasterDataWorkbookService
{
    private const string ImportType="v180-master-data";
    private const string ExcelType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
    private static readonly string[] ES=["EmployeeNo","EmploymentStatus","EffectiveFrom","EffectiveTo"];
    private static readonly string[] C=["CenterCode","CenterName","EffectiveFrom","EffectiveTo","IsActive","Notes"];
    private static readonly string[] TC=["TeamCode","CenterCode","EffectiveFrom","EffectiveTo","ChangeReason"];
    private static readonly string[] DS=["CenterCode","SiteCode","SiteName","LocationCode","EffectiveFrom","EffectiveTo","IsActive","Notes","ChangeReason"];
    private static readonly string[] TS=["TeamCode","SiteCode","EffectiveFrom","EffectiveTo"];
    private static readonly string[] EPS=["EmployeeNo","SiteCode","IsPrimary","EffectiveFrom","EffectiveTo"];

    public Task<ReportExportContext> CreateTemplateAsync(CurrentUserDto admin,CancellationToken ct)
    {
        _=RequireOrg(admin);
        using var ms=new MemoryStream();
        using(var doc=SpreadsheetDocument.Create(ms,DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook,true))
        {
            var wb=doc.AddWorkbookPart();wb.Workbook=new Workbook();var sheets=wb.Workbook.AppendChild(new Sheets());
            AddSheet(wb,sheets,1,"Employment Status",[ES,["pilotv01","Active","2026-09-01",""]]);
            AddSheet(wb,sheets,2,"Centers",[C,["YL-CENTER","員林中心","2026-09-01","","Y","UAT"]]);
            AddSheet(wb,sheets,3,"Team-Center",[TC,["TEAM-003","YL-CENTER","2026-09-01","","UAT"]]);
            AddSheet(wb,sheets,4,"Deployment Sites",[DS,["YL-CENTER","YL-OFFICE","員林辦公室","請填正式LocationCode","2026-09-01","","Y","UAT","UAT"]]);
            AddSheet(wb,sheets,5,"Team-Site",[TS,["TEAM-003","YL-OFFICE","2026-09-01",""]]);
            AddSheet(wb,sheets,6,"Employment-Site",[EPS,["pilotv01","YL-OFFICE","Y","2026-09-01",""]]);
            AddSheet(wb,sheets,7,"Instructions",[
                ["項目","說明"],
                ["流程","填寫 → 上傳 → Preview → 全部正確 → Confirm；Preview 不修改正式主檔。"],
                ["Business Key","只填 EmployeeNo / TeamCode / CenterCode / SiteCode / LocationCode，不填 DB ID。"],
                ["日期","yyyy-MM-dd；EffectiveTo 可空白。期間採含首含尾。"],
                ["Location","Deployment Site 必須引用既有 Approved 且啟用的 LocationCode。"],
                ["依賴","Centers → Team-Center → Deployment Sites → Team-Site → Employment-Site。"],
                ["Primary","同一 Employment 同日期最多一個 Primary Site。"],
                ["回溯","EffectiveFrom 早於今天時 Confirm 需要二次確認。"],
                ["原子性","Confirm 任一筆失敗，整批 rollback。"]
            ]);
            wb.Workbook.Save();
        }
        return Task.FromResult(new ReportExportContext("UAT主檔設定範本.xlsx",ms.ToArray(),ExcelType));
    }

    public async Task<V180MasterDataBulkPreviewDto> PreviewAsync(CurrentUserDto admin,byte[] content,CancellationToken ct)
    {
        if(content.Length==0)throw new InvalidOperationException("上傳檔案為空。");
        var org=RequireOrg(admin);var today=BusinessTime.Today;
        var batch=new ImportBatch{ImportBatchId=Guid.NewGuid(),ImportType=ImportType,OrganizationId=org,RequestedByUserId=admin.UserId,Status="Previewed",CreatedAt=DateTime.UtcNow,ExpiresAt=DateTime.UtcNow.AddHours(4)};
        db.ImportBatches.Add(batch);
        var snap=await Snapshot.Load(db,org,ct);
        var result=new List<V180MasterDataBulkPreviewItemDto>();
        using var ms=new MemoryStream(content);using var doc=SpreadsheetDocument.Open(ms,false);
        var es=ReadSheet(doc,"Employment Status");var c=ReadSheet(doc,"Centers");var tc=ReadSheet(doc,"Team-Center");var ds=ReadSheet(doc,"Deployment Sites");var ts=ReadSheet(doc,"Team-Site");var eps=ReadSheet(doc,"Employment-Site");
        Headers(es,ES,"Employment Status");Headers(c,C,"Centers");Headers(tc,TC,"Team-Center");Headers(ds,DS,"Deployment Sites");Headers(ts,TS,"Team-Site");Headers(eps,EPS,"Employment-Site");
        PreviewEmploymentStatus(batch,result,snap,es,today);
        PreviewCenters(batch,result,snap,c,today);
        PreviewTeamCenters(batch,result,snap,tc,today);
        PreviewSites(batch,result,snap,ds,today);
        PreviewTeamSites(batch,result,snap,ts,today);
        PreviewEmploymentSites(batch,result,snap,eps,today);
        batch.TotalCount=result.Count;batch.ValidCount=result.Count(x=>x.Status=="Valid");batch.ErrorCount=result.Count(x=>x.Status=="Error");
        db.AuditLogs.Add(new AuditLog{UserId=admin.UserId,EntityType="V180MasterDataBulk",EntityId=org.ToString(CultureInfo.InvariantCulture),Action="Preview",NewValues=JsonSerializer.Serialize(new{batch.ImportBatchId,batch.TotalCount,batch.ValidCount,batch.ErrorCount}),CreatedAt=DateTime.UtcNow});
        await db.SaveChangesAsync(ct);
        return new V180MasterDataBulkPreviewDto(batch.ImportBatchId,batch.TotalCount,batch.ValidCount,batch.ErrorCount,result.Any(x=>x.Status=="Valid"&&x.Action!="NoChange"&&x.IsRetroactive),result);
    }

    public async Task<ReportExportContext> CreateErrorReportAsync(CurrentUserDto admin,Guid id,CancellationToken ct)
    {
        var org=RequireOrg(admin);var batch=await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(x=>x.ImportBatchId==id,ct)??throw new KeyNotFoundException("找不到匯入批次。");Own(batch,org,admin.UserId);
        var errors=await db.ImportBatchItems.AsNoTracking().Where(x=>x.ImportBatchId==id&&(x.Status=="Error"||x.Status=="Failed")).OrderBy(x=>x.EntityType).ThenBy(x=>x.RowNumber).ToListAsync(ct);
        if(errors.Count==0)throw new InvalidOperationException("此匯入批次沒有錯誤資料。");
        using var ms=new MemoryStream();using(var doc=SpreadsheetDocument.Create(ms,DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook,true)){var wb=doc.AddWorkbookPart();wb.Workbook=new Workbook();var sheets=wb.Workbook.AppendChild(new Sheets());var rows=new List<string[]>{["RowNumber","Sheet","EntityType","Action","DisplayKey","ErrorMessage"]};rows.AddRange(errors.Select(x=>new[]{x.RowNumber.ToString(CultureInfo.InvariantCulture),Sheet(x.EntityType),x.EntityType,x.Action,x.DisplayKey,x.ErrorMessage??""}));AddSheet(wb,sheets,1,"Errors",rows);wb.Workbook.Save();}
        return new ReportExportContext($"UAT主檔匯入錯誤_{id:N}.xlsx",ms.ToArray(),ExcelType);
    }

    public async Task<V180MasterDataBulkConfirmResultDto> ConfirmAsync(CurrentUserDto admin,Guid id,V180MasterDataBulkConfirmRequest request,CancellationToken ct)
    {
        var org=RequireOrg(admin);var batch=await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(x=>x.ImportBatchId==id,ct)??throw new KeyNotFoundException("找不到匯入批次。");Own(batch,org,admin.UserId);
        var items=await db.ImportBatchItems.AsNoTracking().Where(x=>x.ImportBatchId==id).ToListAsync(ct);
        var retro=items.Where(x=>x.Status=="Valid"&&x.Action!="NoChange").Any(x=>EffectiveFrom(x)<BusinessTime.Today);
        if(batch.Status!="Previewed")throw new InvalidOperationException("此匯入批次已處理或不可使用。");
        if(batch.ExpiresAt<DateTime.UtcNow)throw new InvalidOperationException("匯入預覽已逾時，請重新上傳 Excel。");
        if(batch.ErrorCount>0)throw new InvalidOperationException("預覽仍有錯誤資料，請修正後重新上傳。");
        if(retro&&!request.ConfirmRetroactive)throw new InvalidOperationException("此批次包含回溯異動，請二次確認後再執行。");

        await using var tx=await db.Database.BeginTransactionAsync(ct);
        try
        {
            var claim=await db.ImportBatches.Where(x=>x.ImportBatchId==id&&x.Status=="Previewed"&&x.ExpiresAt>=DateTime.UtcNow).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Confirming"),ct);
            if(claim!=1)throw new InvalidOperationException("此匯入批次正在處理或已處理。");
            var created=0;var updated=0;var unchanged=0;
            foreach(var item in items.Where(x=>x.Status=="Valid").OrderBy(x=>Order(x.EntityType)).ThenBy(x=>x.RowNumber))
            {
                var saved=item.EntityType switch
                {
                    "EmploymentStatus"=>await repository.SaveEmploymentStatusAsync(admin,Json<SaveV180EmploymentStatusRequest>(item),ct),
                    "Center"=>await repository.SaveCenterAsync(admin,Json<SaveV180CenterRequest>(item),ct),
                    "TeamCenter"=>await repository.SaveTeamCenterAsync(admin,Json<SaveV180TeamCenterRequest>(item),ct),
                    "DeploymentSite"=>await repository.SaveDeploymentSiteAsync(admin,Json<SaveV180DeploymentSiteRequest>(item),ct),
                    "TeamSite"=>await repository.SaveTeamSiteAsync(admin,Json<SaveV180TeamSiteRequest>(item),ct),
                    "EmploymentSite"=>await repository.SaveEmploymentSiteAsync(admin,Json<SaveV180EmploymentSiteRequest>(item),ct),
                    _=>throw new InvalidOperationException($"不支援的匯入類型：{item.EntityType}")
                };
                if(saved.Action=="Create")created++;else if(saved.Action=="Update")updated++;else unchanged++;
                await db.ImportBatchItems.Where(x=>x.ImportBatchItemId==item.ImportBatchItemId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Applied").SetProperty(x=>x.ErrorMessage,(string?)null),ct);
            }
            await db.ImportBatches.Where(x=>x.ImportBatchId==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Confirmed").SetProperty(x=>x.ConfirmedAt,DateTime.UtcNow),ct);
            db.AuditLogs.Add(new AuditLog{UserId=admin.UserId,EntityType="V180MasterDataBulk",EntityId=id.ToString(),Action="Confirm",NewValues=JsonSerializer.Serialize(new{created,updated,unchanged}),CreatedAt=DateTime.UtcNow});
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return new V180MasterDataBulkConfirmResultDto(id,created,updated,unchanged,0,[]);
        }
        catch{await tx.RollbackAsync(ct);throw;}
    }

    private void PreviewEmploymentStatus(ImportBatch b,List<V180MasterDataBulkPreviewItemDto> outp,Snapshot s,IReadOnlyList<string[]> rows,DateOnly today)
    {
        var h=rows[0];var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(var i=1;i<rows.Count;i++){var x=rows[i];if(x.All(string.IsNullOrWhiteSpace))continue;var n=i+1;SaveV180EmploymentStatusRequest? r=null;var action="Create";string? err=null;var key=Get(x,h,"EmployeeNo")??$"Row-{n}";
            try{r=new(V180MasterDataRules.NormalizeCode(Get(x,h,"EmployeeNo"),"EmployeeNo"),V180MasterDataRules.NormalizeEmploymentStatus(Get(x,h,"EmploymentStatus")),V180MasterDataRules.ParseDate(Get(x,h,"EffectiveFrom"),"EffectiveFrom"),V180MasterDataRules.ParseOptionalDate(Get(x,h,"EffectiveTo"),"EffectiveTo"));V180MasterDataRules.ValidatePeriod(r.EffectiveFrom,r.EffectiveTo,"Employment Status");key=r.EmployeeNo;if(!s.Employments.ContainsKey(r.EmployeeNo))throw new InvalidOperationException($"找不到 EmployeeNo={r.EmployeeNo}。");var dk=$"{r.EmployeeNo}|{r.EffectiveFrom:yyyy-MM-dd}";if(!seen.Add(dk))throw new InvalidOperationException("同一份 Excel 內 EmployeeNo + EffectiveFrom 不可重複。");var same=s.Statuses.FirstOrDefault(z=>z.EmployeeNo==r.EmployeeNo&&z.EffectiveFrom==r.EffectiveFrom);if(s.Statuses.Any(z=>z.EmployeeNo==r.EmployeeNo&&z.EffectiveFrom!=r.EffectiveFrom&&V180MasterDataRules.Overlaps(z.EffectiveFrom,z.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException("Employment Status effective period 與既有/本批資料重疊。");action=same==null?"Create":same.Status==r.EmploymentStatus&&same.EffectiveTo==r.EffectiveTo?"NoChange":"Update";s.Statuses.RemoveAll(z=>z.EmployeeNo==r.EmployeeNo&&z.EffectiveFrom==r.EffectiveFrom);s.Statuses.Add(new(r.EmployeeNo,r.EmploymentStatus,r.EffectiveFrom,r.EffectiveTo));}
            catch(Exception ex){err=ex.Message;}Stage(b,outp,n,"EmploymentStatus",action,key,(object?)r??new{RowNumber=n},err,r!=null&&action!="NoChange"&&r.EffectiveFrom<today);}
    }
    private void PreviewCenters(ImportBatch b,List<V180MasterDataBulkPreviewItemDto> o,Snapshot s,IReadOnlyList<string[]> rows,DateOnly today)
    {
        var h=rows[0];var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);for(var i=1;i<rows.Count;i++){var x=rows[i];if(x.All(string.IsNullOrWhiteSpace))continue;var n=i+1;SaveV180CenterRequest? r=null;var action="Create";string? err=null;var key=Get(x,h,"CenterCode")??$"Row-{n}";
            try{r=new(V180MasterDataRules.NormalizeCode(Get(x,h,"CenterCode"),"CenterCode"),V180MasterDataRules.NormalizeName(Get(x,h,"CenterName"),"CenterName"),V180MasterDataRules.ParseDate(Get(x,h,"EffectiveFrom"),"EffectiveFrom"),V180MasterDataRules.ParseOptionalDate(Get(x,h,"EffectiveTo"),"EffectiveTo"),BlankTrue(Get(x,h,"IsActive"),"IsActive"),V180MasterDataRules.NormalizeOptionalText(Get(x,h,"Notes"),1000,"Notes"));V180MasterDataRules.ValidatePeriod(r.EffectiveFrom,r.EffectiveTo,"Center");key=r.CenterCode;if(!seen.Add(key))throw new InvalidOperationException("同一份 Excel 內 CenterCode 不可重複。");s.Centers.TryGetValue(r.CenterCode,out var same);if(same!=null&&same.EffectiveFrom!=r.EffectiveFrom)throw new InvalidOperationException($"既有 Center EffectiveFrom={same.EffectiveFrom:yyyy-MM-dd} 不可改寫。");action=same==null?"Create":same.Name==r.CenterName&&same.EffectiveTo==r.EffectiveTo&&same.IsActive==r.IsActive?"NoChange":"Update";s.Centers[r.CenterCode]=new(r.CenterCode,r.CenterName,r.EffectiveFrom,r.EffectiveTo,r.IsActive);}
            catch(Exception ex){err=ex.Message;}Stage(b,o,n,"Center",action,key,(object?)r??new{RowNumber=n},err,r!=null&&action!="NoChange"&&r.EffectiveFrom<today);}
    }
    private void PreviewTeamCenters(ImportBatch b,List<V180MasterDataBulkPreviewItemDto> o,Snapshot s,IReadOnlyList<string[]> rows,DateOnly today)
    {
        var h=rows[0];var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);for(var i=1;i<rows.Count;i++){var x=rows[i];if(x.All(string.IsNullOrWhiteSpace))continue;var n=i+1;SaveV180TeamCenterRequest? r=null;var action="Create";string? err=null;var key=$"{Get(x,h,"TeamCode")}/{Get(x,h,"CenterCode")}";
            try{r=new(V180MasterDataRules.NormalizeCode(Get(x,h,"TeamCode"),"TeamCode"),V180MasterDataRules.NormalizeCode(Get(x,h,"CenterCode"),"CenterCode"),V180MasterDataRules.ParseDate(Get(x,h,"EffectiveFrom"),"EffectiveFrom"),V180MasterDataRules.ParseOptionalDate(Get(x,h,"EffectiveTo"),"EffectiveTo"),V180MasterDataRules.NormalizeOptionalText(Get(x,h,"ChangeReason"),500,"ChangeReason"));V180MasterDataRules.ValidatePeriod(r.EffectiveFrom,r.EffectiveTo,"Team-Center");key=$"{r.TeamCode}/{r.CenterCode}";var dk=$"{r.TeamCode}|{r.EffectiveFrom:yyyy-MM-dd}";if(!seen.Add(dk))throw new InvalidOperationException("同一份 Excel 內 TeamCode + EffectiveFrom 不可重複。");if(!s.Teams.TryGetValue(r.TeamCode,out var team))throw new InvalidOperationException($"找不到 TeamCode={r.TeamCode}。");if(!s.Centers.TryGetValue(r.CenterCode,out var center))throw new InvalidOperationException($"找不到 CenterCode={r.CenterCode}。");if(!V180MasterDataRules.Covers(team.From??DateOnly.MinValue,team.To,r.EffectiveFrom,r.EffectiveTo)||!V180MasterDataRules.Covers(center.EffectiveFrom,center.EffectiveTo,r.EffectiveFrom,r.EffectiveTo))throw new InvalidOperationException("Team-Center period 必須完整位於 Team 與 Center 有效期間內。");var same=s.TeamCenters.FirstOrDefault(z=>z.TeamCode==r.TeamCode&&z.EffectiveFrom==r.EffectiveFrom);if(s.TeamCenters.Any(z=>z.TeamCode==r.TeamCode&&z.EffectiveFrom!=r.EffectiveFrom&&V180MasterDataRules.Overlaps(z.EffectiveFrom,z.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException("Team-Center period 與既有/本批資料重疊。");action=same==null?"Create":same.CenterCode==r.CenterCode&&same.EffectiveTo==r.EffectiveTo?"NoChange":"Update";s.TeamCenters.RemoveAll(z=>z.TeamCode==r.TeamCode&&z.EffectiveFrom==r.EffectiveFrom);s.TeamCenters.Add(new(r.TeamCode,r.CenterCode,r.EffectiveFrom,r.EffectiveTo));}
            catch(Exception ex){err=ex.Message;}Stage(b,o,n,"TeamCenter",action,key,(object?)r??new{RowNumber=n},err,r!=null&&action!="NoChange"&&r.EffectiveFrom<today);}
    }
    private void PreviewSites(ImportBatch b,List<V180MasterDataBulkPreviewItemDto> o,Snapshot s,IReadOnlyList<string[]> rows,DateOnly today)
    {
        var h=rows[0];var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);for(var i=1;i<rows.Count;i++){var x=rows[i];if(x.All(string.IsNullOrWhiteSpace))continue;var n=i+1;SaveV180DeploymentSiteRequest? r=null;var action="Create";string? err=null;var key=Get(x,h,"SiteCode")??$"Row-{n}";
            try{r=new(V180MasterDataRules.NormalizeCode(Get(x,h,"CenterCode"),"CenterCode"),V180MasterDataRules.NormalizeCode(Get(x,h,"SiteCode"),"SiteCode"),V180MasterDataRules.NormalizeName(Get(x,h,"SiteName"),"SiteName"),V180MasterDataRules.NormalizeCode(Get(x,h,"LocationCode"),"LocationCode"),V180MasterDataRules.ParseDate(Get(x,h,"EffectiveFrom"),"EffectiveFrom"),V180MasterDataRules.ParseOptionalDate(Get(x,h,"EffectiveTo"),"EffectiveTo"),BlankTrue(Get(x,h,"IsActive"),"IsActive"),V180MasterDataRules.NormalizeOptionalText(Get(x,h,"Notes"),1000,"Notes"),V180MasterDataRules.NormalizeOptionalText(Get(x,h,"ChangeReason"),500,"ChangeReason"));V180MasterDataRules.ValidatePeriod(r.EffectiveFrom,r.EffectiveTo,"Deployment Site");key=r.SiteCode;if(!seen.Add(key))throw new InvalidOperationException("同一份 Excel 內 SiteCode 不可重複。");if(s.DuplicateSites.Contains(r.SiteCode))throw new InvalidOperationException($"SiteCode={r.SiteCode} 在目前資料庫跨 Center 重複，請由 IT Review。");if(!s.Centers.TryGetValue(r.CenterCode,out var center)||!center.IsActive||!V180MasterDataRules.Covers(center.EffectiveFrom,center.EffectiveTo,r.EffectiveFrom,r.EffectiveTo))throw new InvalidOperationException("Deployment Site period 必須完整位於啟用 Center 有效期間內。");if(!s.Locations.TryGetValue(r.LocationCode,out var loc)||!loc.Active||!loc.Approved)throw new InvalidOperationException("LocationCode 必須存在、Approved 且啟用。");s.Sites.TryGetValue(r.SiteCode,out var same);if(same!=null&&same.CenterCode!=r.CenterCode)throw new InvalidOperationException("SiteCode 已存在於其他 Center。");if(same!=null&&same.EffectiveFrom!=r.EffectiveFrom)throw new InvalidOperationException($"既有 Site EffectiveFrom={same.EffectiveFrom:yyyy-MM-dd} 不可改寫。");action=same==null?"Create":same.Name==r.SiteName&&same.LocationCode==r.LocationCode&&same.EffectiveTo==r.EffectiveTo&&same.IsActive==r.IsActive?"NoChange":"Update";s.Sites[r.SiteCode]=new(r.SiteCode,r.CenterCode,r.SiteName,r.LocationCode,r.EffectiveFrom,r.EffectiveTo,r.IsActive);}
            catch(Exception ex){err=ex.Message;}Stage(b,o,n,"DeploymentSite",action,key,(object?)r??new{RowNumber=n},err,r!=null&&action!="NoChange"&&r.EffectiveFrom<today);}
    }
    private void PreviewTeamSites(ImportBatch b,List<V180MasterDataBulkPreviewItemDto> o,Snapshot s,IReadOnlyList<string[]> rows,DateOnly today)
    {
        var h=rows[0];var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);for(var i=1;i<rows.Count;i++){var x=rows[i];if(x.All(string.IsNullOrWhiteSpace))continue;var n=i+1;SaveV180TeamSiteRequest? r=null;var action="Create";string? err=null;var key=$"{Get(x,h,"TeamCode")}/{Get(x,h,"SiteCode")}";
            try{r=new(V180MasterDataRules.NormalizeCode(Get(x,h,"TeamCode"),"TeamCode"),V180MasterDataRules.NormalizeCode(Get(x,h,"SiteCode"),"SiteCode"),V180MasterDataRules.ParseDate(Get(x,h,"EffectiveFrom"),"EffectiveFrom"),V180MasterDataRules.ParseOptionalDate(Get(x,h,"EffectiveTo"),"EffectiveTo"));V180MasterDataRules.ValidatePeriod(r.EffectiveFrom,r.EffectiveTo,"Team-Site");key=$"{r.TeamCode}/{r.SiteCode}";var dk=$"{r.TeamCode}|{r.SiteCode}|{r.EffectiveFrom:yyyy-MM-dd}";if(!seen.Add(dk))throw new InvalidOperationException("同一份 Excel 內 Team-Site business key 不可重複。");if(!s.Teams.ContainsKey(r.TeamCode))throw new InvalidOperationException($"找不到 TeamCode={r.TeamCode}。");if(!s.Sites.TryGetValue(r.SiteCode,out var site)||!site.IsActive||!V180MasterDataRules.Covers(site.EffectiveFrom,site.EffectiveTo,r.EffectiveFrom,r.EffectiveTo))throw new InvalidOperationException("Team-Site 必須完整位於啟用 Site 有效期間內。");if(!s.TeamCenters.Any(z=>z.TeamCode==r.TeamCode&&z.CenterCode==site.CenterCode&&V180MasterDataRules.Covers(z.EffectiveFrom,z.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException("Team 在整段期間沒有被指派至 Site 所屬 Center。");var same=s.TeamSites.FirstOrDefault(z=>z.TeamCode==r.TeamCode&&z.SiteCode==r.SiteCode&&z.EffectiveFrom==r.EffectiveFrom);if(s.TeamSites.Any(z=>z.TeamCode==r.TeamCode&&z.SiteCode==r.SiteCode&&z.EffectiveFrom!=r.EffectiveFrom&&V180MasterDataRules.Overlaps(z.EffectiveFrom,z.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException("Team-Site period 與既有/本批資料重疊。");action=same==null?"Create":same.EffectiveTo==r.EffectiveTo?"NoChange":"Update";s.TeamSites.RemoveAll(z=>z.TeamCode==r.TeamCode&&z.SiteCode==r.SiteCode&&z.EffectiveFrom==r.EffectiveFrom);s.TeamSites.Add(new(r.TeamCode,r.SiteCode,r.EffectiveFrom,r.EffectiveTo));}
            catch(Exception ex){err=ex.Message;}Stage(b,o,n,"TeamSite",action,key,(object?)r??new{RowNumber=n},err,r!=null&&action!="NoChange"&&r.EffectiveFrom<today);}
    }
    private void PreviewEmploymentSites(ImportBatch b,List<V180MasterDataBulkPreviewItemDto> o,Snapshot s,IReadOnlyList<string[]> rows,DateOnly today)
    {
        var h=rows[0];var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);for(var i=1;i<rows.Count;i++){var x=rows[i];if(x.All(string.IsNullOrWhiteSpace))continue;var n=i+1;SaveV180EmploymentSiteRequest? r=null;var action="Create";string? err=null;var key=$"{Get(x,h,"EmployeeNo")}/{Get(x,h,"SiteCode")}";
            try{r=new(V180MasterDataRules.NormalizeCode(Get(x,h,"EmployeeNo"),"EmployeeNo"),V180MasterDataRules.NormalizeCode(Get(x,h,"SiteCode"),"SiteCode"),V180MasterDataRules.ParseBoolean(Get(x,h,"IsPrimary"),"IsPrimary"),V180MasterDataRules.ParseDate(Get(x,h,"EffectiveFrom"),"EffectiveFrom"),V180MasterDataRules.ParseOptionalDate(Get(x,h,"EffectiveTo"),"EffectiveTo"));V180MasterDataRules.ValidatePeriod(r.EffectiveFrom,r.EffectiveTo,"Employment-Site");key=$"{r.EmployeeNo}/{r.SiteCode}";var dk=$"{r.EmployeeNo}|{r.SiteCode}|{r.EffectiveFrom:yyyy-MM-dd}";if(!seen.Add(dk))throw new InvalidOperationException("同一份 Excel 內 Employment-Site business key 不可重複。");if(!s.Employments.ContainsKey(r.EmployeeNo))throw new InvalidOperationException($"找不到 EmployeeNo={r.EmployeeNo}。");if(!s.Sites.TryGetValue(r.SiteCode,out var site)||!site.IsActive||!V180MasterDataRules.Covers(site.EffectiveFrom,site.EffectiveTo,r.EffectiveFrom,r.EffectiveTo))throw new InvalidOperationException("Employment-Site 必須完整位於啟用 Site 有效期間內。");if(!s.Statuses.Any(z=>z.EmployeeNo==r.EmployeeNo&&z.Status=="Active"&&V180MasterDataRules.Covers(z.EffectiveFrom,z.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException("Employment 在整段期間沒有 Active status。");var teams=s.Memberships.Where(z=>z.EmployeeNo==r.EmployeeNo&&V180MasterDataRules.Covers(z.EffectiveFrom,z.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)).Select(z=>z.TeamCode).Distinct().ToList();if(teams.Count==0)throw new InvalidOperationException("Employment 在整段期間沒有 Team membership。");if(!s.TeamSites.Any(z=>teams.Contains(z.TeamCode)&&z.SiteCode==r.SiteCode&&V180MasterDataRules.Covers(z.EffectiveFrom,z.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException("Employment 的 Team 在整段期間沒有使用此 Site 的資格。");var same=s.EmploymentSites.FirstOrDefault(z=>z.EmployeeNo==r.EmployeeNo&&z.SiteCode==r.SiteCode&&z.EffectiveFrom==r.EffectiveFrom);if(s.EmploymentSites.Any(z=>z.EmployeeNo==r.EmployeeNo&&z.SiteCode==r.SiteCode&&z.EffectiveFrom!=r.EffectiveFrom&&V180MasterDataRules.Overlaps(z.EffectiveFrom,z.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException("Employment-Site period 與既有/本批資料重疊。");if(r.IsPrimary&&s.EmploymentSites.Any(z=>z.EmployeeNo==r.EmployeeNo&&z.IsPrimary&&!(z.SiteCode==r.SiteCode&&z.EffectiveFrom==r.EffectiveFrom)&&V180MasterDataRules.Overlaps(z.EffectiveFrom,z.EffectiveTo,r.EffectiveFrom,r.EffectiveTo)))throw new InvalidOperationException("同一 Employment 同日期只能有一個 Primary Site。");action=same==null?"Create":same.IsPrimary==r.IsPrimary&&same.EffectiveTo==r.EffectiveTo?"NoChange":"Update";s.EmploymentSites.RemoveAll(z=>z.EmployeeNo==r.EmployeeNo&&z.SiteCode==r.SiteCode&&z.EffectiveFrom==r.EffectiveFrom);s.EmploymentSites.Add(new(r.EmployeeNo,r.SiteCode,r.IsPrimary,r.EffectiveFrom,r.EffectiveTo));}
            catch(Exception ex){err=ex.Message;}Stage(b,o,n,"EmploymentSite",action,key,(object?)r??new{RowNumber=n},err,r!=null&&action!="NoChange"&&r.EffectiveFrom<today);}
    }

    private void Stage(ImportBatch b,ICollection<V180MasterDataBulkPreviewItemDto> output,int row,string type,string action,string key,object data,string? error,bool retro)
    {
        var status=error==null?"Valid":"Error";var item=new ImportBatchItem{ImportBatchId=b.ImportBatchId,RowNumber=row,EntityType=type,Action=action,Status=status,DisplayKey=key,DataJson=JsonSerializer.Serialize(data,JsonOptions),ErrorMessage=error,CreatedAt=DateTime.UtcNow};db.ImportBatchItems.Add(item);output.Add(new(row,Sheet(type),type,action,key,status,error,retro));
    }
    private static T Json<T>(ImportBatchItem i)=>JsonSerializer.Deserialize<T>(i.DataJson,JsonOptions)??throw new InvalidOperationException("批次暫存資料格式不正確。");
    private static DateOnly EffectiveFrom(ImportBatchItem i)=>i.EntityType switch{"EmploymentStatus"=>Json<SaveV180EmploymentStatusRequest>(i).EffectiveFrom,"Center"=>Json<SaveV180CenterRequest>(i).EffectiveFrom,"TeamCenter"=>Json<SaveV180TeamCenterRequest>(i).EffectiveFrom,"DeploymentSite"=>Json<SaveV180DeploymentSiteRequest>(i).EffectiveFrom,"TeamSite"=>Json<SaveV180TeamSiteRequest>(i).EffectiveFrom,"EmploymentSite"=>Json<SaveV180EmploymentSiteRequest>(i).EffectiveFrom,_=>DateOnly.MaxValue};
    private static int Order(string type)=>type switch{"EmploymentStatus"=>0,"Center"=>1,"TeamCenter"=>2,"DeploymentSite"=>3,"TeamSite"=>4,"EmploymentSite"=>5,_=>99};
    private static string Sheet(string type)=>type switch{"EmploymentStatus"=>"Employment Status","Center"=>"Centers","TeamCenter"=>"Team-Center","DeploymentSite"=>"Deployment Sites","TeamSite"=>"Team-Site","EmploymentSite"=>"Employment-Site",_=>type};
    private static bool BlankTrue(string? raw,string field)=>string.IsNullOrWhiteSpace(raw)||V180MasterDataRules.ParseBoolean(raw,field);
    private static int RequireOrg(CurrentUserDto a)=>a.OrganizationId??throw new InvalidOperationException("目前 Business Admin 缺少 OrganizationId。");
    private static void Own(ImportBatch b,int org,int user){if(b.ImportType!=ImportType)throw new InvalidOperationException("此批次不是 v1.8 主檔匯入。");if(b.OrganizationId!=org||b.RequestedByUserId!=user)throw new UnauthorizedAccessException("只能處理自己建立的主檔匯入預覽。");}

    private static IReadOnlyList<string[]> ReadSheet(SpreadsheetDocument doc,string name)
    {
        var wb=doc.WorkbookPart??throw new InvalidOperationException("Excel Workbook 不正確。");var sheet=wb.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault(x=>string.Equals(x.Name?.Value,name,StringComparison.OrdinalIgnoreCase))??throw new InvalidOperationException($"Excel 缺少工作表：{name}");var wp=(WorksheetPart)wb.GetPartById(sheet.Id!);var result=new List<string[]>();
        foreach(var row in wp.Worksheet.Descendants<Row>()){var vals=new string[32];var seq=0;foreach(var cell in row.Elements<Cell>()){var idx=Column(cell.CellReference?.Value);if(idx<0)idx=seq;if(idx<vals.Length)vals[idx]=Cell(wb,cell);seq=Math.Max(seq+1,idx+1);}result.Add(vals);}return result;
    }
    private static string Cell(WorkbookPart wb,Cell c){if(c.DataType?.Value==CellValues.SharedString&&int.TryParse(c.CellValue?.Text,out var i))return wb.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().ElementAtOrDefault(i)?.InnerText??"";if(c.DataType?.Value==CellValues.InlineString)return c.InlineString?.InnerText??"";return c.CellValue?.Text??c.InnerText??"";}
    private static int Column(string? r){if(string.IsNullOrWhiteSpace(r))return -1;var v=0;foreach(var ch in r){if(!char.IsLetter(ch))break;v=v*26+char.ToUpperInvariant(ch)-'A'+1;}return v>0?v-1:-1;}
    private static string? Get(string[] row,string[] headers,string name){var i=Array.FindIndex(headers,x=>x.Equals(name,StringComparison.OrdinalIgnoreCase));if(i<0||i>=row.Length)return null;var v=row[i]?.Trim();return string.IsNullOrWhiteSpace(v)?null:v;}
    private static void Headers(IReadOnlyList<string[]> rows,IReadOnlyList<string> required,string name){if(rows.Count==0)throw new InvalidOperationException($"工作表 {name} 沒有標題列。");var h=rows[0].Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);var miss=required.Where(x=>!h.Contains(x)).ToList();if(miss.Count>0)throw new InvalidOperationException($"工作表 {name} 缺少欄位：{string.Join(", ",miss)}");}
    private static void AddSheet(WorkbookPart wb,Sheets sheets,uint id,string name,IEnumerable<string[]> rows){var wp=wb.AddNewPart<WorksheetPart>();var data=new SheetData();wp.Worksheet=new Worksheet(data);foreach(var vals in rows){var row=new Row();foreach(var v in vals)row.Append(new Cell{DataType=CellValues.InlineString,InlineString=new InlineString(new Text(v??""))});data.Append(row);}wp.Worksheet.Save();sheets.Append(new Sheet{Id=wb.GetIdOfPart(wp),SheetId=id,Name=name});}

    private sealed record TeamState(string Code,DateOnly? From,DateOnly? To);
    private sealed record LocationState(bool Active,bool Approved);
    private sealed record CenterState(string Code,string Name,DateOnly EffectiveFrom,DateOnly? EffectiveTo,bool IsActive);
    private sealed record TeamCenterState(string TeamCode,string CenterCode,DateOnly EffectiveFrom,DateOnly? EffectiveTo);
    private sealed record SiteState(string Code,string CenterCode,string Name,string LocationCode,DateOnly EffectiveFrom,DateOnly? EffectiveTo,bool IsActive);
    private sealed record StatusState(string EmployeeNo,string Status,DateOnly EffectiveFrom,DateOnly? EffectiveTo);
    private sealed record MembershipState(string EmployeeNo,string TeamCode,DateOnly EffectiveFrom,DateOnly? EffectiveTo);
    private sealed record TeamSiteState(string TeamCode,string SiteCode,DateOnly EffectiveFrom,DateOnly? EffectiveTo);
    private sealed record EmploymentSiteState(string EmployeeNo,string SiteCode,bool IsPrimary,DateOnly EffectiveFrom,DateOnly? EffectiveTo);

    private sealed class Snapshot
    {
        public Dictionary<string,Employment> Employments {get;}=new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string,TeamState> Teams {get;}=new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string,LocationState> Locations {get;}=new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string,CenterState> Centers {get;}=new(StringComparer.OrdinalIgnoreCase);
        public List<TeamCenterState> TeamCenters {get;}=[];
        public Dictionary<string,SiteState> Sites {get;}=new(StringComparer.OrdinalIgnoreCase);
        public List<StatusState> Statuses {get;}=[];
        public List<MembershipState> Memberships {get;}=[];
        public List<TeamSiteState> TeamSites {get;}=[];
        public List<EmploymentSiteState> EmploymentSites {get;}=[];
        public HashSet<string> DuplicateSites {get;}=new(StringComparer.OrdinalIgnoreCase);

        public static async Task<Snapshot> Load(AppDbContext db,int org,CancellationToken ct)
        {
            var s=new Snapshot();
            var emp=await db.Employments.AsNoTracking().Where(x=>x.OrganizationId==org&&x.EmployeeNo!=null).ToListAsync(ct);foreach(var x in emp)s.Employments[x.EmployeeNo!.ToUpperInvariant()]=x;var empIds=emp.Select(x=>x.EmploymentId).ToList();var empById=emp.ToDictionary(x=>x.EmploymentId);
            var teams=await db.Teams.AsNoTracking().Where(x=>x.OrganizationId==org).ToListAsync(ct);foreach(var x in teams)s.Teams[x.TeamCode.ToUpperInvariant()]=new(x.TeamCode.ToUpperInvariant(),x.EffectiveFrom,x.EffectiveTo);var teamIds=teams.Select(x=>x.TeamId).ToList();var teamById=teams.ToDictionary(x=>x.TeamId);
            var locs=await db.Locations.AsNoTracking().Where(x=>x.OrganizationId==org&&x.LocationCode!=null).ToListAsync(ct);foreach(var x in locs)s.Locations[x.LocationCode!.ToUpperInvariant()]=new(x.IsActive,x.ApprovalStatus.Equals("Approved",StringComparison.OrdinalIgnoreCase));var locById=locs.ToDictionary(x=>x.LocationId);
            var centers=await db.Centers.AsNoTracking().Where(x=>x.OrganizationId==org).ToListAsync(ct);foreach(var x in centers)s.Centers[x.CenterCode.ToUpperInvariant()]=new(x.CenterCode.ToUpperInvariant(),x.CenterName,x.EffectiveFrom,x.EffectiveTo,x.IsActive);var centerIds=centers.Select(x=>x.CenterId).ToList();var centerById=centers.ToDictionary(x=>x.CenterId);
            var tc=await db.TeamCenterAssignments.AsNoTracking().Where(x=>teamIds.Contains(x.TeamId)&&centerIds.Contains(x.CenterId)).ToListAsync(ct);s.TeamCenters.AddRange(tc.Select(x=>new TeamCenterState(teamById[x.TeamId].TeamCode.ToUpperInvariant(),centerById[x.CenterId].CenterCode.ToUpperInvariant(),x.EffectiveFrom,x.EffectiveTo)));
            var sites=await db.DeploymentSites.AsNoTracking().Where(x=>centerIds.Contains(x.CenterId)).ToListAsync(ct);foreach(var g in sites.GroupBy(x=>x.SiteCode,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))s.DuplicateSites.Add(g.Key.ToUpperInvariant());var siteIds=sites.Select(x=>x.DeploymentSiteId).ToList();var siteById=sites.ToDictionary(x=>x.DeploymentSiteId);var sla=await db.DeploymentSiteLocationAssignments.AsNoTracking().Where(x=>siteIds.Contains(x.DeploymentSiteId)).ToListAsync(ct);
            foreach(var x in sites.Where(x=>!s.DuplicateSites.Contains(x.SiteCode))){var l=sla.Where(a=>a.DeploymentSiteId==x.DeploymentSiteId).OrderByDescending(a=>a.EffectiveFrom).FirstOrDefault();var lc=l!=null&&locById.TryGetValue(l.LocationId,out var loc)?loc.LocationCode?.ToUpperInvariant()??"":"";s.Sites[x.SiteCode.ToUpperInvariant()]=new(x.SiteCode.ToUpperInvariant(),centerById[x.CenterId].CenterCode.ToUpperInvariant(),x.SiteName,lc,x.EffectiveFrom,x.EffectiveTo,x.IsActive);}
            var st=await db.EmploymentStatusPeriods.AsNoTracking().Where(x=>empIds.Contains(x.EmploymentId)).ToListAsync(ct);s.Statuses.AddRange(st.Select(x=>new StatusState(empById[x.EmploymentId].EmployeeNo!.ToUpperInvariant(),x.EmploymentStatus,x.EffectiveFrom,x.EffectiveTo)));
            var mem=await db.TeamMemberships.AsNoTracking().Where(x=>empIds.Contains(x.EmploymentId)&&teamIds.Contains(x.TeamId)).ToListAsync(ct);s.Memberships.AddRange(mem.Select(x=>new MembershipState(empById[x.EmploymentId].EmployeeNo!.ToUpperInvariant(),teamById[x.TeamId].TeamCode.ToUpperInvariant(),x.EffectiveFrom,x.EffectiveTo)));
            var ts=await db.TeamDeploymentSiteAssignments.AsNoTracking().Where(x=>teamIds.Contains(x.TeamId)&&siteIds.Contains(x.DeploymentSiteId)).ToListAsync(ct);s.TeamSites.AddRange(ts.Select(x=>new TeamSiteState(teamById[x.TeamId].TeamCode.ToUpperInvariant(),siteById[x.DeploymentSiteId].SiteCode.ToUpperInvariant(),x.EffectiveFrom,x.EffectiveTo)));
            var es=await db.EmploymentDeploymentSiteAssignments.AsNoTracking().Where(x=>empIds.Contains(x.EmploymentId)&&siteIds.Contains(x.DeploymentSiteId)).ToListAsync(ct);s.EmploymentSites.AddRange(es.Select(x=>new EmploymentSiteState(empById[x.EmploymentId].EmployeeNo!.ToUpperInvariant(),siteById[x.DeploymentSiteId].SiteCode.ToUpperInvariant(),x.IsPrimary,x.EffectiveFrom,x.EffectiveTo)));
            return s;
        }
    }
}
