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

    [HttpGet("visit-types/{visitTypeId:int}/delete-impact")]
    public Task<V180VisitTypeDeleteImpactDto> VisitTypeImpact(int visitTypeId,CancellationToken ct)
        => service.VisitTypeImpactAsync(visitTypeId,ct);

    [HttpDelete("visit-types/{visitTypeId:int}/permanent")]
    public async Task<IActionResult> DeleteVisitType(int visitTypeId,CancellationToken ct)
    {
        await service.DeleteVisitTypeAsync(visitTypeId,ct);
        return NoContent();
    }

    [HttpGet("mileage-rate-rules/{mileageRateRuleId:int}/delete-impact")]
    public Task<V180MileageRateDeleteImpactDto> MileageRateImpact(int mileageRateRuleId,CancellationToken ct)
        => service.MileageRateImpactAsync(mileageRateRuleId,ct);

    [HttpDelete("mileage-rate-rules/{mileageRateRuleId:int}/permanent")]
    public async Task<IActionResult> DeleteMileageRate(int mileageRateRuleId,CancellationToken ct)
    {
        await service.DeleteMileageRateAsync(mileageRateRuleId,ct);
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
