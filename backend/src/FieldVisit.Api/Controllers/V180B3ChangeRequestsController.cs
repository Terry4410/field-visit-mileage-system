using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController,Authorize]
[Route("api/v1/change-requests")]
public sealed class V180B3ChangeRequestsController(V180B3ChangeRequestService service)
    : ControllerBase
{
    private IActionResult Off()=>StatusCode(503,
        new{code="B3_DISABLED",message="B3 核准流程尚未授權啟用。"});
    [HttpPost("locations"),Authorize(Roles="visitor,leader")]
    public async Task<IActionResult> Submit(V180B3SubmitLocation input,CancellationToken ct)=>
        !service.Enabled?Off():Ok(await service.SubmitAsync(input,ct));
    [HttpGet("mine"),Authorize(Roles="visitor,leader,admin")]
    public async Task<IActionResult> Mine(CancellationToken ct)=>
        !service.Enabled?Off():Ok(await service.MineAsync(ct));
    [HttpGet("admin/pending"),Authorize(Roles="admin")]
    public async Task<IActionResult> Pending(CancellationToken ct)=>
        !service.Enabled?Off():Ok(await service.PendingAsync(ct));
    [HttpPost("admin/{id:guid}/reject"),Authorize(Roles="admin")]
    public async Task<IActionResult> Reject(Guid id,V180B3Review input,CancellationToken ct)=>
        !service.Enabled?Off():Ok(await service.RejectAsync(id,input,ct));
    [HttpPost("admin/{id:guid}/approve"),Authorize(Roles="admin")]
    public async Task<IActionResult> Approve(Guid id,V180B3Review input,CancellationToken ct)=>
        !service.Enabled?Off():Ok(await service.ApproveAsync(id,input,ct));
}
