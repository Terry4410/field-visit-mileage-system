using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class MasterController(MasterService master) : ControllerBase
{
    [HttpGet("teams")]
    public async Task<ActionResult<List<TeamDto>>> Teams(CancellationToken ct) => Ok(await master.TeamsAsync(ct));

    [HttpGet("locations")]
    public async Task<ActionResult<List<LocationDto>>> Locations(CancellationToken ct) => Ok(await master.LocationsAsync(ct));

    [HttpGet("locations/pending")]
    [Authorize(Roles = "leader,admin")]
    public async Task<ActionResult<List<LocationDto>>> Pending([FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, CancellationToken ct) => Ok(await master.PendingLocationsAsync(startDate, endDate, ct));

    [HttpPut("locations/{locationId:int}")]
    [Authorize(Roles = "leader,admin")]
    public async Task<ActionResult<LocationDto>> UpdateLocation(int locationId, UpdateLocationRequest request, CancellationToken ct) => Ok(await master.UpdateLocationAsync(locationId, request, ct));

    [HttpPost("locations/batch-publish")]
    [Authorize(Roles = "leader,admin")]
    public async Task<ActionResult<BatchPublishLocationsResult>> BatchPublish(BatchPublishLocationsRequest request, CancellationToken ct) => Ok(await master.BatchPublishAsync(request, ct));

    [HttpGet("projects")]
    public async Task<ActionResult<List<ProjectDto>>> Projects(CancellationToken ct) => Ok(await master.ProjectsAsync(ct));

    [HttpGet("projects/{projectId:int}/locations")]
    public async Task<ActionResult<List<LocationDto>>> ProjectLocations(int projectId, CancellationToken ct) => Ok(await master.ProjectLocationsAsync(projectId, ct));

    [HttpGet("visit-types")]
    public async Task<ActionResult<List<VisitTypeDto>>> VisitTypes(CancellationToken ct) => Ok(await master.VisitTypesAsync(ct));

    [HttpGet("mileage-rate-rules")]
    public async Task<ActionResult<List<MileageRateDto>>> Rates(CancellationToken ct) => Ok(await master.RatesAsync(ct));

    [HttpPost("mileage-rate-rules")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<MileageRateDto>> CreateRate(CreateMileageRateRequest request, CancellationToken ct) => Ok(await master.CreateRateAsync(request, ct));
}
