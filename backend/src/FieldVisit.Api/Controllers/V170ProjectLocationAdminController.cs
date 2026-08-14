using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/admin/projects/{projectId:int}")]
public sealed class V170ProjectLocationAdminController(
    V170ProjectLocationAdminService service)
    : ControllerBase
{
    [HttpGet("locations")]
    public async Task<ActionResult<
        IReadOnlyList<V170ProjectLocationItemDto>>>
        Get(
            int projectId,
            CancellationToken ct)
    {
        return Ok(
            await service.GetAsync(
                projectId,
                ct));
    }

    [HttpGet("location-candidates")]
    public async Task<ActionResult<
        V170ProjectLocationCandidateResult>>
        SearchCandidates(
            int projectId,
            [FromQuery] string? q,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
    {
        return Ok(
            await service.SearchCandidatesAsync(
                projectId,
                q,
                page,
                pageSize,
                ct));
    }

    [HttpPut("locations")]
    public async Task<ActionResult<
        IReadOnlyList<V170ProjectLocationItemDto>>>
        Save(
            int projectId,
            V170SaveProjectLocationsRequest request,
            CancellationToken ct)
    {
        return Ok(
            await service.SaveAsync(
                projectId,
                request,
                ct));
    }
}
