using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/locations")]
public sealed class V170LocationController(
    V170LocationService locations) : ControllerBase
{
    [HttpGet("search")]
    [Authorize(Roles = "visitor,leader,admin")]
    public async Task<ActionResult<V170LocationSearchResult>> Search(
        [FromQuery] string? q,
        [FromQuery] string? city,
        [FromQuery] string? district,
        [FromQuery] int? projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result =
            await locations.SearchAsync(
                new V170LocationSearchRequest(
                    q,
                    city,
                    district,
                    projectId,
                    page,
                    pageSize),
                ct);

        return Ok(result);
    }
}
