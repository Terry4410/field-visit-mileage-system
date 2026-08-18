using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/admin/projects")]
public sealed class V170ProjectLocationSummaryController(
    V170ProjectLocationAdminService service)
    : ControllerBase
{
    [HttpGet("location-counts")]
    public async Task<ActionResult<IReadOnlyList<V170ProjectLocationCountDto>>> GetLocationCounts(
        CancellationToken ct)
        => Ok(await service.GetLocationCountsAsync(ct));
}
