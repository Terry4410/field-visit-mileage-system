using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/admin/v180")]
public sealed class V180OrganizationPeopleAdminController(
    IV180OrganizationPeopleReader reader, IV180OrganizationPeopleWriter writer,
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
}
