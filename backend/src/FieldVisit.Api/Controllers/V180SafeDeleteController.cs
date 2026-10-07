using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles="admin")]
[Route("api/v1/admin")]
public sealed class V180SafeDeleteController(
    V180SafeDeleteService service) : ControllerBase
{
    [HttpGet("people/{userId:int}/delete-impact")]
    public Task<V180PersonDeleteImpactDto> PersonImpact(int userId,CancellationToken ct)
        => service.PersonImpactAsync(userId,ct);

    [HttpDelete("people/{userId:int}/permanent")]
    public async Task<IActionResult> DeletePerson(int userId,CancellationToken ct)
    {
        await service.DeletePersonAsync(userId,ct);
        return NoContent();
    }

    [HttpGet("teams/{teamId:int}/delete-impact")]
    public Task<V180TeamDeleteImpactDto> TeamImpact(int teamId,CancellationToken ct)
        => service.TeamImpactAsync(teamId,ct);

    [HttpDelete("teams/{teamId:int}/permanent")]
    public async Task<IActionResult> DeleteTeam(int teamId,CancellationToken ct)
    {
        await service.DeleteTeamAsync(teamId,ct);
        return NoContent();
    }

    [HttpGet("projects/{projectId:int}/delete-impact")]
    public Task<V180ProjectDeleteImpactDto> ProjectImpact(int projectId,CancellationToken ct)
        => service.ProjectImpactAsync(projectId,ct);

    [HttpDelete("projects/{projectId:int}/permanent")]
    public async Task<IActionResult> DeleteProject(int projectId,CancellationToken ct)
    {
        await service.DeleteProjectAsync(projectId,ct);
        return NoContent();
    }
}
