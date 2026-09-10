using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class V180TeamLocationNotesController(
    IV180TeamLocationNoteService notes,
    ICurrentUserService current) : ControllerBase
{
    [HttpGet("teams/{teamId:int}/location-notes")]
    [Authorize(Roles = "visitor,leader,admin")]
    public async Task<ActionResult<V180TeamLocationNoteDto>> Get(
        int teamId, [FromQuery] int locationId, CancellationToken ct) =>
        Ok(await notes.GetAsync(current.GetRequired(), teamId, locationId, ct));

    [HttpGet("team-location-notes/teams")]
    [Authorize(Roles = "leader,admin")]
    public async Task<ActionResult<IReadOnlyList<TeamDto>>> WritableTeams(CancellationToken ct) =>
        Ok(await notes.GetWritableTeamsAsync(current.GetRequired(), ct));

    [HttpPost("teams/{teamId:int}/location-notes")]
    [Authorize(Roles = "leader,admin")]
    public async Task<ActionResult<V180TeamLocationNoteDto>> Create(
        int teamId, [FromBody] V180CreateTeamLocationNoteRequest request, CancellationToken ct) =>
        Ok(await notes.CreateAsync(current.GetRequired(), teamId, request, ct));

    [HttpPut("team-location-notes/{teamLocationNoteId:long}")]
    [Authorize(Roles = "leader,admin")]
    public async Task<ActionResult<V180TeamLocationNoteDto>> Update(
        long teamLocationNoteId, [FromBody] V180UpdateTeamLocationNoteRequest request, CancellationToken ct) =>
        Ok(await notes.UpdateAsync(current.GetRequired(), teamLocationNoteId, request, ct));

    [HttpPost("team-location-notes/{teamLocationNoteId:long}/clear")]
    [Authorize(Roles = "leader,admin")]
    public async Task<ActionResult<V180TeamLocationNoteDto>> Clear(
        long teamLocationNoteId, [FromBody] V180ClearTeamLocationNoteRequest request, CancellationToken ct) =>
        Ok(await notes.ClearAsync(current.GetRequired(), teamLocationNoteId, request, ct));
}
