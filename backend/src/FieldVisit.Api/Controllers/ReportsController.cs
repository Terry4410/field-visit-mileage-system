using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "visitor,leader,admin,supervisor")]
[Route("api/v1/reports")]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    [HttpGet("mileage")]
    public async Task<ActionResult<List<MileageReportRow>>> Mileage([FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, CancellationToken ct) => Ok(await reports.MileageAsync(startDate, endDate, ct));
}
