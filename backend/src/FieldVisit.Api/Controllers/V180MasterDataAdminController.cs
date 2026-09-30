using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/admin/master-data")]
public sealed class V180MasterDataAdminController(V180MasterDataAdminService service) : ControllerBase
{
    [HttpGet("readiness")] public Task<V180MasterDataReadinessDto> Readiness(CancellationToken ct)=>service.ReadinessAsync(ct);
    [HttpGet("{kind}")] public Task<IReadOnlyList<V180MasterDataRow>> List(string kind,CancellationToken ct)=>service.ListAsync(kind,ct);
    [HttpPost("employment-status")] public Task<V180MasterDataRow> CreateEmploymentStatus(V180EmploymentStatusInput x,CancellationToken ct)=>service.SaveEmploymentStatusAsync(null,x,ct);
    [HttpPut("employment-status/{id:long}")] public Task<V180MasterDataRow> UpdateEmploymentStatus(long id,V180EmploymentStatusInput x,CancellationToken ct)=>service.SaveEmploymentStatusAsync(id,x,ct);
    [HttpPost("centers")] public Task<V180MasterDataRow> CreateCenter(V180CenterInput x,CancellationToken ct)=>service.SaveCenterAsync(null,x,ct);
    [HttpPut("centers/{id:int}")] public Task<V180MasterDataRow> UpdateCenter(int id,V180CenterInput x,CancellationToken ct)=>service.SaveCenterAsync(id,x,ct);
    [HttpPost("team-centers")] public Task<V180MasterDataRow> CreateTeamCenter(V180TeamCenterInput x,CancellationToken ct)=>service.SaveTeamCenterAsync(null,x,ct);
    [HttpPut("team-centers/{id:long}")] public Task<V180MasterDataRow> UpdateTeamCenter(long id,V180TeamCenterInput x,CancellationToken ct)=>service.SaveTeamCenterAsync(id,x,ct);
    [HttpPost("deployment-sites")] public Task<V180MasterDataRow> CreateSite(V180DeploymentSiteInput x,CancellationToken ct)=>service.SaveDeploymentSiteAsync(null,x,ct);
    [HttpPut("deployment-sites/{id:int}")] public Task<V180MasterDataRow> UpdateSite(int id,V180DeploymentSiteInput x,CancellationToken ct)=>service.SaveDeploymentSiteAsync(id,x,ct);
    [HttpPost("team-sites")] public Task<V180MasterDataRow> CreateTeamSite(V180TeamSiteInput x,CancellationToken ct)=>service.SaveTeamSiteAsync(null,x,ct);
    [HttpPut("team-sites/{id:long}")] public Task<V180MasterDataRow> UpdateTeamSite(long id,V180TeamSiteInput x,CancellationToken ct)=>service.SaveTeamSiteAsync(id,x,ct);
    [HttpPost("employment-sites")] public Task<V180MasterDataRow> CreateEmploymentSite(V180EmploymentSiteInput x,CancellationToken ct)=>service.SaveEmploymentSiteAsync(null,x,ct);
    [HttpPut("employment-sites/{id:long}")] public Task<V180MasterDataRow> UpdateEmploymentSite(long id,V180EmploymentSiteInput x,CancellationToken ct)=>service.SaveEmploymentSiteAsync(id,x,ct);
}
