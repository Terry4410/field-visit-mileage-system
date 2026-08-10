using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class TripsController(TripService trips, LeaderService leader) : ControllerBase
{
    [HttpPost("trips")]
    [Authorize(Roles = "visitor")]
    public async Task<ActionResult<TripDto>> Create(SaveTripRequest request, CancellationToken ct) => Ok(await trips.CreateAsync(request, ct));

    [HttpPut("trips/{tripId:long}")]
    [Authorize(Roles = "visitor")]
    public async Task<ActionResult<TripDto>> Update(long tripId, SaveTripRequest request, [FromHeader(Name="If-Match")] string rowVersion, CancellationToken ct) =>
        Ok(await trips.UpdateAsync(tripId, request, rowVersion.Trim('"'), ct));

    [HttpGet("trips/{tripId:long}")]
    public async Task<ActionResult<TripDto>> Get(long tripId, CancellationToken ct) => Ok(await trips.GetDtoAsync(tripId, ct));

    [HttpPost("trips/time-overlap-check")]
    [Authorize(Roles = "visitor")]
    public async Task<ActionResult<TimeOverlapResult>> Overlap(TimeOverlapRequest request, CancellationToken ct) => Ok(await trips.CheckOverlapAsync(request, ct));

    [HttpPost("trips/{tripId:long}/submit")]
    [Authorize(Roles = "visitor")]
    public async Task<ActionResult<TripDto>> Submit(long tripId, SubmitTripRequest request, [FromHeader(Name="If-Match")] string rowVersion, CancellationToken ct) =>
        Ok(await trips.SubmitAsync(tripId, request, rowVersion.Trim('"'), ct));

    [HttpGet("trips/history")]
    [Authorize(Roles = "visitor")]
    public async Task<ActionResult<List<TripDto>>> History([FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, [FromQuery] string? locationKeyword, CancellationToken ct) =>
        Ok(await trips.HistoryAsync(startDate, endDate, locationKeyword, ct));

    [HttpGet("leader/review-queue")]
    [Authorize(Roles = "leader")]
    public async Task<ActionResult<List<TripDto>>> Queue(CancellationToken ct) => Ok(await leader.ReviewQueueAsync(ct));

    [HttpPost("mileage-jobs")]
    [Authorize(Roles = "leader")]
    public async Task<ActionResult<MileageBatchResult>> Mileage(MileageBatchRequest request, CancellationToken ct) => Ok(await leader.CalculateBatchAsync(request, ct));

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
