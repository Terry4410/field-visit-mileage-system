using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/admin/v180")]
public sealed class V180TeamCenterAdminController(
    IV180TeamCenterAdminReader reader,
    ICurrentUserService current) : ControllerBase
{
    [HttpGet("teams/{teamId:int}")]
    public async Task<ActionResult<V180TeamLifecycleDetailDto>> Team(int teamId, CancellationToken ct)
    {
        var result = await reader.GetTeamAsync(current.GetRequired(), teamId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("centers/{centerId:int}")]
    public async Task<ActionResult<V180CenterLifecycleDetailDto>> Center(int centerId, CancellationToken ct)
    {
        var result = await reader.GetCenterAsync(current.GetRequired(), centerId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("team-center-assignments")]
    public async Task<ActionResult<IReadOnlyList<V180TeamCenterAssignmentAdminDto>>> Assignments(
        [FromQuery] int teamId,
        [FromQuery] bool includeHistory = true,
        [FromQuery] DateOnly? asOf = null,
        CancellationToken ct = default) =>
        Ok(await reader.ListAssignmentsAsync(current.GetRequired(), teamId, includeHistory, asOf, ct));
}
