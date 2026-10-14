using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/optionc-candidate")]
public sealed class V180OptionCCandidateController(V180OptionCCandidateService service) : ControllerBase
{
    [HttpPost("locations/{locationId:int}/changes")]
    public Task<V180B3RequestView> Submit(int locationId, V180B3SubmitLocation input, CancellationToken ct)
    {
        if (locationId != input.LocationId) throw new InvalidOperationException("OPTIONC_LOCATION_ROUTE_MISMATCH");
        return service.SubmitAsync(input, ct);
    }

    [HttpGet("requests/mine")]
    public Task<IReadOnlyList<V180B3RequestView>> Mine(CancellationToken ct) => service.OwnAsync(ct);

    [HttpGet("requests/queue")]
    public Task<IReadOnlyList<V180B3RequestView>> Queue(CancellationToken ct) => service.QueueAsync(ct);

    [HttpPost("requests/{id:guid}/approve")]
    public Task<V180B3RequestView> Approve(Guid id, V180B3Review input, CancellationToken ct)
        => service.ApproveAsync(id, input, ct);

    [HttpPost("requests/{id:guid}/reject")]
    public Task<V180B3RequestView> Reject(Guid id, V180B3Review input, CancellationToken ct)
        => service.RejectAsync(id, input, ct);

    [HttpGet("requests/{id:guid}/reconcile")]
    public Task<string> Reconcile(Guid id, [FromQuery] Guid decisionKey, CancellationToken ct)
        => service.ReconcileAsync(id, decisionKey, ct);
}
