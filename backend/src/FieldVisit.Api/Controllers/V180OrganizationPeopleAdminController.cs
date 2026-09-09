using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/admin/v180")]
public sealed class V180OrganizationPeopleAdminController(
    IV180OrganizationPeopleReader reader, IV180OrganizationPeopleWriter writer,
    IV180TeamCenterLifecycleWriter lifecycleWriter,
    ICurrentUserService current) : ControllerBase
{
    [HttpGet("people")]
    public async Task<ActionResult<PagedResult<V180PersonRowDto>>> People(
        [FromQuery] V180AdminAsOfQuery query, CancellationToken ct) =>
        Ok(await reader.SearchPeopleAsync(current.GetRequired(), query, ct));

    [HttpGet("people/{employmentId:long}")]
    public async Task<ActionResult<V180PersonRowDto>> Person(long employmentId,
        [FromQuery] DateOnly? asOf, CancellationToken ct)
    {
        var result = await reader.GetPersonAsync(current.GetRequired(), employmentId, asOf, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("teams")]
    public async Task<ActionResult<PagedResult<V180TeamAdminDto>>> Teams(
        [FromQuery] V180AdminAsOfQuery query, CancellationToken ct) =>
        Ok(await reader.SearchTeamsAsync(current.GetRequired(), query, ct));

    [HttpGet("centers")]
    public async Task<ActionResult<PagedResult<V180CenterAdminDto>>> Centers(
        [FromQuery] V180AdminAsOfQuery query, CancellationToken ct) =>
        Ok(await reader.SearchCentersAsync(current.GetRequired(), query, ct));

    [HttpPut("people/{employmentId:long}/access")]
    public async Task<ActionResult<V180PeopleAccessWriteResult>> UpdateAccess(
        long employmentId, [FromBody] V180UpdatePeopleAccessRequest request, CancellationToken ct) =>
        Ok(await writer.UpdateAccessAsync(current.GetRequired(), employmentId, request, ct));

    [HttpPost("teams")]
    public async Task<ActionResult<V180TeamWriteResult>> CreateTeam(
        [FromBody] V180CreateTeamRequest request, CancellationToken ct) =>
        Ok(await lifecycleWriter.CreateTeamAsync(current.GetRequired(), request, ct));

    [HttpPut("teams/{teamId:int}")]
    public async Task<ActionResult<V180TeamWriteResult>> UpdateTeam(int teamId,
        [FromBody] V180UpdateTeamRequest request, CancellationToken ct) =>
        Ok(await lifecycleWriter.UpdateTeamAsync(current.GetRequired(), teamId, request, ct));

    [HttpPost("teams/{teamId:int}/deactivate")]
    public async Task<ActionResult<V180TeamWriteResult>> DeactivateTeam(int teamId,
        [FromBody] V180DeactivateRequest request, CancellationToken ct) =>
        Ok(await lifecycleWriter.DeactivateTeamAsync(current.GetRequired(), teamId, request, ct));

    [HttpPost("centers")]
    public async Task<ActionResult<V180CenterWriteResult>> CreateCenter(
        [FromBody] V180CreateCenterRequest request, CancellationToken ct) =>
        Ok(await lifecycleWriter.CreateCenterAsync(current.GetRequired(), request, ct));

    [HttpPut("centers/{centerId:int}")]
    public async Task<ActionResult<V180CenterWriteResult>> UpdateCenter(int centerId,
        [FromBody] V180UpdateCenterRequest request, CancellationToken ct) =>
        Ok(await lifecycleWriter.UpdateCenterAsync(current.GetRequired(), centerId, request, ct));

    [HttpPost("centers/{centerId:int}/deactivate")]
    public async Task<ActionResult<V180CenterWriteResult>> DeactivateCenter(int centerId,
        [FromBody] V180DeactivateRequest request, CancellationToken ct) =>
        Ok(await lifecycleWriter.DeactivateCenterAsync(current.GetRequired(), centerId, request, ct));

    [HttpPost("team-center-assignments")]
    public async Task<ActionResult<V180TeamCenterAssignmentWriteResult>> CreateTeamCenterAssignment(
        [FromBody] V180CreateTeamCenterAssignmentRequest request, CancellationToken ct) =>
        Ok(await lifecycleWriter.CreateTeamCenterAssignmentAsync(current.GetRequired(), request, ct));

    [HttpPut("team-center-assignments/{assignmentId:long}")]
    public async Task<ActionResult<V180TeamCenterAssignmentWriteResult>> UpdateTeamCenterAssignment(long assignmentId,
        [FromBody] V180UpdateTeamCenterAssignmentRequest request, CancellationToken ct) =>
        Ok(await lifecycleWriter.UpdateTeamCenterAssignmentAsync(current.GetRequired(), assignmentId, request, ct));

    [HttpPost("team-center-assignments/{assignmentId:long}/end")]
    public async Task<ActionResult<V180TeamCenterAssignmentWriteResult>> EndTeamCenterAssignment(long assignmentId,
        [FromBody] V180EndTeamCenterAssignmentRequest request, CancellationToken ct) =>
        Ok(await lifecycleWriter.EndTeamCenterAssignmentAsync(current.GetRequired(), assignmentId, request, ct));
}
