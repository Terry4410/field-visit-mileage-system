using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class TripsController(TripService trips, LeaderService leader, V180GoogleMileageOrchestrationService googleMileage, AppDbContext db, IConfiguration configuration) : ControllerBase
{
    [HttpGet("trips/context")]
    [Authorize(Roles = "visitor")]
    public async Task<ActionResult<V180TripContextDto>> Context(
        [FromQuery] DateOnly visitDate,
        [FromQuery] int? teamId,
        CancellationToken ct) =>
        Ok(await trips.ContextAsync(visitDate, teamId, ct));


    [HttpPost("trips")]
    [Authorize(Roles = "visitor")]
    public async Task<ActionResult<TripDto>> Create(SaveTripRequest request, CancellationToken ct) => Ok(await trips.CreateAsync(request, ct));

    [HttpPut("trips/{tripId:long}")]
    [Authorize(Roles = "visitor")]
    public async Task<ActionResult<TripDto>> Update(long tripId, SaveTripRequest request, [FromHeader(Name="If-Match")] string rowVersion, CancellationToken ct) =>
        Ok(await trips.UpdateAsync(tripId, request, rowVersion.Trim('"'), ct));

    [HttpDelete("trips/{tripId:long}")]
    [Authorize(Roles = "visitor")]
    public async Task<IActionResult> DeleteDraft(long tripId, [FromHeader(Name="If-Match")] string rowVersion, CancellationToken ct)
    {
        await trips.DeleteDraftAsync(tripId, rowVersion.Trim('"'), ct);
        return NoContent();
    }

    [HttpGet("trips/{tripId:long}")]
    public async Task<ActionResult<TripDto>> Get(long tripId, CancellationToken ct) => Ok(await trips.GetDtoAsync(tripId, ct));

    [HttpPost("trips/time-overlap-check")]
    [Authorize(Roles = "visitor")]
    public async Task<ActionResult<TimeOverlapResult>> Overlap(TimeOverlapRequest request, CancellationToken ct) => Ok(await trips.CheckOverlapAsync(request, ct));

    [HttpPost("trips/{tripId:long}/submit")]
    [Authorize(Roles = "visitor")]
    public async Task<ActionResult<TripDto>> Submit(long tripId, SubmitTripRequest request, [FromHeader(Name="If-Match")] string rowVersion, CancellationToken ct) =>
        Ok(await trips.SubmitAsync(tripId, request, rowVersion.Trim('"'), ct));

    [HttpPost("trips/{tripId:long}/route-preview")]
    [Authorize(Roles = "visitor")]
    public async Task<ActionResult<V180RouteOrchestrationResult>> PreviewRoute(long tripId, CancellationToken ct) =>
        Ok(await googleMileage.PreviewRouteAsync(tripId, ct));

    // Candidate-only leader route retry.  The route is deliberately inert in
    // deployed environments: both the candidate flag and the explicit
    // loopback OptionCIsolated_* execution boundary are required.  This keeps
    // the real HTTP/DI path testable without turning on UAT/Production.
    [HttpPost("leader/trips/{tripId:long}/route-retry")]
    [Authorize(Roles = "leader")]
    public async Task<ActionResult<V180RouteOrchestrationResult>> RetryRoute(long tripId, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("OptionC:Candidate:Enabled"))
            throw new InvalidOperationException("OPTIONC_CANDIDATE_DISABLED");
        if (db.Database.GetDbConnection() is not SqlConnection connection)
            throw new InvalidOperationException("OPTIONC_EXECUTION_TARGET_DENIED");
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);
        V180OptionCExecutionPolicy.RequireIsolatedSqlTest(connection);
        return Ok(await googleMileage.RetryRouteAsync(tripId, ct));
    }

    [HttpGet("leader/review-queue")]
    [Authorize(Roles = "leader")]
    public async Task<ActionResult<List<TripDto>>> Queue(CancellationToken ct) => Ok(await leader.ReviewQueueAsync(ct));

    [HttpPost("leader/trips/calculate")]
    [Authorize(Roles = "leader")]
    public async Task<ActionResult<MileageBatchResult>> CalculateMileage(MileageBatchRequest request, CancellationToken ct) =>
        Ok(await leader.CalculateBatchAsync(request, ct));

    [HttpPost("trips/{tripId:long}/approve")]
    [Authorize(Roles = "leader")]
    public async Task<ActionResult<TripDto>> Approve(long tripId, ApproveTripRequest request, CancellationToken ct) => Ok(await leader.ApproveAsync(tripId, request, ct));

    [HttpPost("trips/{tripId:long}/return")]
    [Authorize(Roles = "leader")]
    public async Task<ActionResult<TripDto>> Return(long tripId, ReturnTripRequest request, CancellationToken ct) => Ok(await leader.ReturnAsync(tripId, request, ct));

    [HttpPost("trips/batch-approve")]
    [Authorize(Roles = "leader")]
    public async Task<ActionResult<BatchApproveResult>> BatchApprove(BatchApproveRequest request, CancellationToken ct) => Ok(await leader.BatchApproveAsync(request, ct));
}
