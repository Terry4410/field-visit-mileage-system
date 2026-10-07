using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/corrections")]
public sealed class V180CorrectionMileageController(V180CorrectionMileageService mileage) : ControllerBase
{
    [HttpPost("{id:long}/route-preview")]
    public async Task<ActionResult<V180RouteOrchestrationResult>> Calculate(long id, CancellationToken ct) =>
        Ok(await mileage.CalculateAsync(id, ct));
}
