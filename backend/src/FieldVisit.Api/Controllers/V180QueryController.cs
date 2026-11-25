using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class V180QueryController(IV160FinalRepository repository, ICurrentUserService current) : ControllerBase
{
    [HttpGet("admin/users/search"), Authorize(Roles = "admin")]
    public async Task<IActionResult> Users([FromQuery] V180SearchRequest request, CancellationToken ct)
        => Ok(await repository.SearchUsersAsync(current.GetRequired(), request, ct));

    [HttpGet("admin/teams/search"), Authorize(Roles = "admin")]
    public async Task<IActionResult> Teams([FromQuery] V180SearchRequest request, CancellationToken ct)
        => Ok(await repository.SearchTeamsAsync(current.GetRequired(), request, ct));

    [HttpGet("admin/projects/search"), Authorize(Roles = "admin")]
    public async Task<IActionResult> Projects([FromQuery] V180SearchRequest request, CancellationToken ct)
        => Ok(await repository.SearchProjectsAsync(current.GetRequired(), request, ct));

    [HttpGet("corrections/search")]
    public async Task<IActionResult> Corrections([FromQuery] V180SearchRequest request, CancellationToken ct)
        => Ok(await repository.SearchCorrectionsAsync(current.GetRequired(), request, ct));

    [HttpPost("visit-types/{id:int}/move"), Authorize(Roles = "admin")]
    public async Task<IActionResult> MoveVisitType(int id, V180MoveVisitTypeRequest request, CancellationToken ct)
        => Ok(await repository.MoveVisitTypeAsync(current.GetRequired(), id, request, ct));
}
