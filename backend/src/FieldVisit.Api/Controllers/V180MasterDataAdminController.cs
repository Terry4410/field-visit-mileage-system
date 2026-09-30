using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles="admin")]
[Route("api/v1/admin/master-data")]
public sealed class V180MasterDataAdminController(V180MasterDataAdminService service):ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<V180MasterDataWorkspaceDto>> Workspace(CancellationToken ct)=>Ok(await service.GetWorkspaceAsync(ct));
    [HttpPost("employment-status")]
    public async Task<ActionResult<V180MasterDataSaveResultDto>> SaveEmploymentStatus(SaveV180EmploymentStatusRequest r,CancellationToken ct)=>Ok(await service.SaveEmploymentStatusAsync(r,ct));
    [HttpPost("centers")]
    public async Task<ActionResult<V180MasterDataSaveResultDto>> SaveCenter(SaveV180CenterRequest r,CancellationToken ct)=>Ok(await service.SaveCenterAsync(r,ct));
    [HttpPost("team-centers")]
    public async Task<ActionResult<V180MasterDataSaveResultDto>> SaveTeamCenter(SaveV180TeamCenterRequest r,CancellationToken ct)=>Ok(await service.SaveTeamCenterAsync(r,ct));
    [HttpPost("deployment-sites")]
    public async Task<ActionResult<V180MasterDataSaveResultDto>> SaveDeploymentSite(SaveV180DeploymentSiteRequest r,CancellationToken ct)=>Ok(await service.SaveDeploymentSiteAsync(r,ct));
    [HttpPost("team-sites")]
    public async Task<ActionResult<V180MasterDataSaveResultDto>> SaveTeamSite(SaveV180TeamSiteRequest r,CancellationToken ct)=>Ok(await service.SaveTeamSiteAsync(r,ct));
    [HttpPost("employment-sites")]
    public async Task<ActionResult<V180MasterDataSaveResultDto>> SaveEmploymentSite(SaveV180EmploymentSiteRequest r,CancellationToken ct)=>Ok(await service.SaveEmploymentSiteAsync(r,ct));

    [HttpGet("bulk/template.xlsx")]
    public async Task<IActionResult> Template(CancellationToken ct)
    {
        var f=await service.CreateTemplateAsync(ct);return File(f.Content,f.ContentType,f.FileName);
    }

    [HttpPost("bulk/preview")]
    [RequestSizeLimit(10*1024*1024)]
    public async Task<ActionResult<V180MasterDataBulkPreviewDto>> Preview(IFormFile file,CancellationToken ct)
    {
        if(file is null||file.Length==0)throw new InvalidOperationException("請選擇匯入檔案。");
        if(!ImportFileCompatibility.IsSupported(file.FileName))throw new InvalidOperationException("只支援 .xlsx、.xls、.csv 檔案。");
        await using var ms=new MemoryStream();await file.CopyToAsync(ms,ct);
        var content=ImportFileCompatibility.NormalizeToXlsx(file.FileName,ms.ToArray(),"master-data");
        return Ok(await service.PreviewBulkAsync(content,ct));
    }

    [HttpPost("bulk/{importBatchId:guid}/confirm")]
    public async Task<ActionResult<V180MasterDataBulkConfirmResultDto>> Confirm(Guid importBatchId,V180MasterDataBulkConfirmRequest r,CancellationToken ct)
        =>Ok(await service.ConfirmBulkAsync(importBatchId,r,ct));

    [HttpGet("bulk/{importBatchId:guid}/errors.xlsx")]
    public async Task<IActionResult> Errors(Guid importBatchId,CancellationToken ct)
    {
        var f=await service.BulkErrorReportAsync(importBatchId,ct);return File(f.Content,f.ContentType,f.FileName);
    }
}
