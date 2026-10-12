using System.Text;
using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/admin/leader-grants")]
public sealed class V180LeaderGrantAdminController(V180LeaderGrantAdminService service) : ControllerBase
{
    [HttpGet]
    public Task<V180LeaderGrantSnapshotDto> Query([FromQuery] V180LeaderGrantQueryRequest request, CancellationToken ct) => service.QueryAsync(request, ct);

    [HttpPost("appointments")]
    public async Task<ActionResult<V180LeaderAppointmentDto>> CreateAppointment(V180CreateLeaderAppointmentRequest request, CancellationToken ct) => Ok(await service.CreateAppointmentAsync(request, ct));

    [HttpPost("delegations")]
    public async Task<ActionResult<V180LeaderDelegationDto>> CreateDelegation(V180CreateLeaderDelegationRequest request, CancellationToken ct) => Ok(await service.CreateDelegationAsync(request, ct));

    [HttpPut("appointments/{id:long}/revoke")]
    public async Task<IActionResult> RevokeAppointment(long id, V180EndLeaderGrantRequest request, CancellationToken ct) { await service.EndAppointmentAsync(id, request, ct); return NoContent(); }

    [HttpPut("delegations/{id:long}/revoke")]
    public async Task<IActionResult> RevokeDelegation(long id, V180EndLeaderGrantRequest request, CancellationToken ct) { await service.EndDelegationAsync(id, request, ct); return NoContent(); }

    [HttpGet("export.csv")]
    public async Task<FileContentResult> Export([FromQuery] V180LeaderGrantQueryRequest request, CancellationToken ct)
    {
        var data = await service.QueryAsync(request, ct);
        var csv = new StringBuilder("Kind,GrantId,Team,UserId,EffectiveFrom,EffectiveTo,Status,EvidenceReference\n");
        foreach (var x in data.Appointments) csv.AppendLine($"Appointment,{x.GrantId},{Escape(x.TeamName)},{x.LeaderUserId},{x.EffectiveFrom:yyyy-MM-dd},{x.EffectiveTo:yyyy-MM-dd},{x.GrantStatus},{Escape(x.EvidenceReference)}");
        foreach (var x in data.Delegations) csv.AppendLine($"Delegation,{x.GrantId},{Escape(x.TeamName)},{x.DelegateUserId},{x.EffectiveFrom:yyyy-MM-dd},{x.EffectiveTo:yyyy-MM-dd},{x.GrantStatus},{Escape(x.EvidenceReference)}");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", "小組長任命與代理授權.csv");
    }

    private static string Escape(string? value) => '"' + (value ?? "").Replace("\"", "\"\"") + '"';
}
