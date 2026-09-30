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
    public async Task<ActionResult<V180MasterDataWorkspaceDto>> Workspace(CancellationToken ct)
        =>Ok(await service.GetWorkspaceAsync(ct));
    [HttpPost("employment-status")]
    public async Task<ActionResult<V180MasterDataSaveResultDto>> SaveEmploymentStatus(SaveV180EmploymentStatusRequest request,CancellationToken ct)
        =>Ok(await service.SaveEmploymentStatusAsync(request,ct));
    [HttpPost("centers")]
    public async Task<ActionResult<V180MasterDataSaveResultDto>> SaveCenter(SaveV180CenterRequest request,CancellationToken ct)
        =>Ok(await service.SaveCenterAsync(request,ct));
    [HttpPost("team-centers")]
    public async Task<ActionResult<V180MasterDataSaveResultDto>> SaveTeamCenter(SaveV180TeamCenterRequest request,CancellationToken ct)
        =>Ok(await service.SaveTeamCenterAsync(request,ct));
    [HttpPost("deployment-sites")]
    public async Task<ActionResult<V180MasterDataSaveResultDto>> SaveDeploymentSite(SaveV180DeploymentSiteRequest request,CancellationToken ct)
        =>Ok(await service.SaveDeploymentSiteAsync(request,ct));
    [HttpPost("team-sites")]
    public async Task<ActionResult<V180MasterDataSaveResultDto>> SaveTeamSite(SaveV180TeamSiteRequest request,CancellationToken ct)
        =>Ok(await service.SaveTeamSiteAsync(request,ct));
    [HttpPost("employment-sites")]
    public async Task<ActionResult<V180MasterDataSaveResultDto>> SaveEmploymentSite(SaveV180EmploymentSiteRequest request,CancellationToken ct)
        =>Ok(await service.SaveEmploymentSiteAsync(request,ct));
}
