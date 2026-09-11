using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class MasterController(MasterService master, MileageRateRuntimeService mileageRates) : ControllerBase
{
    [HttpGet("teams")]
    public async Task<ActionResult<List<TeamDto>>> Teams(CancellationToken ct) => Ok(await master.TeamsAsync(ct));
    [HttpGet("locations")]
    public async Task<ActionResult<List<LocationDto>>> Locations(CancellationToken ct) => Ok(await master.LocationsAsync(ct));
    [HttpGet("locations/pending")][Authorize(Roles="leader,admin")]
    public async Task<ActionResult<List<LocationDto>>> Pending([FromQuery] DateOnly? startDate,[FromQuery] DateOnly? endDate,CancellationToken ct)=>Ok(await master.PendingLocationsAsync(startDate,endDate,ct));
    [HttpPut("locations/{locationId:int}")][Authorize(Roles="leader,admin")]
    public async Task<ActionResult<LocationDto>> UpdateLocation(int locationId,UpdateLocationRequest request,CancellationToken ct)=>Ok(await master.UpdateLocationAsync(locationId,request,ct));
    [HttpPost("locations/{locationId:int}/promote")][Authorize(Roles="admin")]
    public async Task<ActionResult<LocationDto>> PromoteLocation(int locationId,PromoteLocationRequest request,CancellationToken ct)=>Ok(await master.PromoteTemporaryLocationAsync(locationId,request,ct));

    [HttpGet("projects")]
    public async Task<ActionResult<List<ProjectDto>>> Projects(CancellationToken ct)=>Ok(await master.ProjectsAsync(ct));
    [HttpPost("projects")][Authorize(Roles="admin")]
    public async Task<ActionResult<ProjectDto>> CreateProject(SaveProjectRequest request,CancellationToken ct)=>Ok(await master.CreateProjectAsync(request,ct));
    [HttpPut("projects/{projectId:int}")][Authorize(Roles="admin")]
    public async Task<ActionResult<ProjectDto>> UpdateProject(int projectId,SaveProjectRequest request,CancellationToken ct)=>Ok(await master.UpdateProjectAsync(projectId,request,ct));
    [HttpPost("projects/{projectId:int}/deactivate")][Authorize(Roles="admin")]
    public async Task<ActionResult<ProjectDto>> DeactivateProject(int projectId,LifecycleRequest request,CancellationToken ct)=>Ok(await master.DeactivateProjectAsync(projectId,request.RowVersion,ct));
    [HttpPost("projects/{projectId:int}/reactivate")][Authorize(Roles="admin")]
    public async Task<ActionResult<ProjectDto>> ReactivateProject(int projectId,LifecycleRequest request,CancellationToken ct)=>Ok(await master.ReactivateProjectAsync(projectId,request,ct));
    [HttpGet("projects/{projectId:int}/locations")]
    public async Task<ActionResult<List<LocationDto>>> ProjectLocations(int projectId,CancellationToken ct)=>Ok(await master.ProjectLocationsAsync(projectId,ct));

    [HttpGet("visit-types")]
    public async Task<ActionResult<List<VisitTypeDto>>> VisitTypes(CancellationToken ct)=>Ok(await master.VisitTypesAsync(ct));
    [HttpPost("visit-types")][Authorize(Roles="admin")]
    public async Task<ActionResult<VisitTypeDto>> CreateVisitType(SaveVisitTypeRequest request,CancellationToken ct)=>Ok(await master.CreateVisitTypeAsync(request,ct));
    [HttpPut("visit-types/{visitTypeId:int}")][Authorize(Roles="admin")]
    public async Task<ActionResult<VisitTypeDto>> UpdateVisitType(int visitTypeId,SaveVisitTypeRequest request,CancellationToken ct)=>Ok(await master.UpdateVisitTypeAsync(visitTypeId,request,ct));
    [HttpPost("visit-types/{visitTypeId:int}/deactivate")][Authorize(Roles="admin")]
    public async Task<ActionResult<VisitTypeDto>> DeactivateVisitType(int visitTypeId,LifecycleRequest request,CancellationToken ct)=>Ok(await master.DeactivateVisitTypeAsync(visitTypeId,request.RowVersion,ct));
    [HttpPost("visit-types/{visitTypeId:int}/reactivate")][Authorize(Roles="admin")]
    public async Task<ActionResult<VisitTypeDto>> ReactivateVisitType(int visitTypeId,LifecycleRequest request,CancellationToken ct)=>Ok(await master.ReactivateVisitTypeAsync(visitTypeId,request,ct));
    [HttpPost("visit-types/reorder")][Authorize(Roles="admin")]
    public async Task<ActionResult<List<VisitTypeDto>>> ReorderVisitTypes(VisitTypeReorderRequest request,CancellationToken ct)=>Ok(await master.ReorderVisitTypesAsync(request,ct));

    [HttpGet("mileage-rate-rules")]
    public async Task<ActionResult<List<MileageRateRuntimeDto>>> Rates(CancellationToken ct)=>Ok(await mileageRates.RatesAsync(ct));
    [HttpGet("mileage-rate-rules/impact")][Authorize(Roles="admin")]
    public async Task<ActionResult<MileageRateImpactDto>> RateImpact([FromQuery] DateOnly effectiveFrom,[FromQuery] string? vehicleType,CancellationToken ct)=>Ok(await mileageRates.RateImpactAsync(effectiveFrom,vehicleType,ct));
    [HttpPost("mileage-rate-rules")][Authorize(Roles="admin")]
    public async Task<ActionResult<MileageRateRuntimeDto>> CreateRate(CreateMileageRateRequest request,CancellationToken ct)=>Ok(await mileageRates.CreateRateAsync(request,ct));
    [HttpPut("mileage-rate-rules/{mileageRateRuleId:int}")][Authorize(Roles="admin")]
    public async Task<ActionResult<MileageRateRuntimeDto>> UpdateRate(int mileageRateRuleId,UpdateMileageRateRuntimeRequest request,CancellationToken ct)=>Ok(await mileageRates.UpdateRateAsync(mileageRateRuleId,request,ct));
    [HttpDelete("mileage-rate-rules/{mileageRateRuleId:int}")][Authorize(Roles="admin")]
    public async Task<IActionResult> DeleteRate(int mileageRateRuleId,[FromQuery] bool acknowledgeHistoricalImpact,[FromQuery] string rowVersion,CancellationToken ct){await mileageRates.DeleteRateAsync(mileageRateRuleId,acknowledgeHistoricalImpact,rowVersion,ct);return NoContent();}
}
