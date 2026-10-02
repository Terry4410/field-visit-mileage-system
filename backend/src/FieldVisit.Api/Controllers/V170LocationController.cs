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
        [FromQuery] int? teamId,
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
                    pageSize,
                    teamId),
                ct);

        return Ok(result);
    }

    [HttpGet("favorites")]
    [Authorize(Roles = "visitor,leader,admin")]
    public async Task<ActionResult<
        IReadOnlyList<V170LocationFavoriteDto>>> Favorites(
        [FromQuery] int? teamId,
        CancellationToken ct)
    {
        return Ok(
            await locations.GetFavoritesAsync(
                teamId,
                ct));
    }

    [HttpPost("{locationId:int}/favorite")]
    [Authorize(Roles = "visitor,leader,admin")]
    public async Task<IActionResult> AddFavorite(
        int locationId,
        CancellationToken ct)
    {
        await locations.AddFavoriteAsync(
            locationId,
            ct);

        return NoContent();
    }

    [HttpDelete("{locationId:int}/favorite")]
    [Authorize(Roles = "visitor,leader,admin")]
    public async Task<IActionResult> RemoveFavorite(
        int locationId,
        CancellationToken ct)
    {
        await locations.RemoveFavoriteAsync(
            locationId,
            ct);

        return NoContent();
    }

    [HttpPut("favorites/order")]
    [Authorize(Roles = "visitor,leader,admin")]
    public async Task<IActionResult> ReorderFavorites(
        V170LocationFavoriteOrderRequest request,
        CancellationToken ct)
    {
        await locations.ReorderFavoritesAsync(
            request,
            ct);

        return NoContent();
    }

    [HttpGet("recent")]
    [Authorize(Roles = "visitor,leader,admin")]
    public async Task<ActionResult<
        IReadOnlyList<V170LocationRecentDto>>> Recent(
        [FromQuery] int limit = 20,
        [FromQuery] int? teamId = null,
        CancellationToken ct = default)
    {
        return Ok(
            await locations.GetRecentAsync(
                limit,
                teamId,
                ct));
    }

    [HttpGet("{locationId:int}/maintenance")]
    [Authorize(Roles = "visitor,leader,admin")]
    public Task<V170LocationMaintenanceDto> Maintenance(int locationId,[FromQuery]int? teamId,CancellationToken ct)
        => locations.GetMaintenanceAsync(locationId,teamId,ct);

    [HttpPut("{locationId:int}/maintenance")]
    [Authorize(Roles = "visitor,leader,admin")]
    public Task<V170LocationMaintenanceDto> UpdateMaintenance(int locationId,V170LocationMaintenanceUpdateRequest request,CancellationToken ct)
        => locations.UpdateMaintenanceAsync(locationId,request,ct);

    [HttpPost("{locationId:int}/notes")]
    [Authorize(Roles = "visitor,leader,admin")]
    public Task<V170LocationMaintenanceDto> AddNote(int locationId,V170LocationNoteRequest request,CancellationToken ct)
        => locations.AddNoteAsync(locationId,request,ct);

    [HttpGet("{locationId:int}/duplicate-candidates")]
    [Authorize(Roles = "admin")]
    public Task<IReadOnlyList<V170LocationDuplicateCandidateDto>> DuplicateCandidates(int locationId,CancellationToken ct)
        => locations.GetDuplicateCandidatesAsync(locationId,ct);

    [HttpGet("{locationId:int}/merge-preview")]
    [Authorize(Roles = "admin")]
    public Task<V170LocationMergePreviewDto> MergePreview(int locationId,[FromQuery]int survivorLocationId,CancellationToken ct)
        => locations.PreviewMergeAsync(locationId,survivorLocationId,ct);

    [HttpPost("{locationId:int}/merge")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Merge(int locationId,V170LocationMergeRequest request,CancellationToken ct)
    {
        await locations.MergeAsync(locationId,request,ct);
        return NoContent();
    }

    [HttpGet("nearby")]
    [Authorize(Roles = "visitor,leader,admin")]
    public async Task<ActionResult<
        IReadOnlyList<V170LocationNearbyDto>>> Nearby(
        [FromQuery] decimal latitude,
        [FromQuery] decimal longitude,
        [FromQuery] int? projectId,
        [FromQuery] int? teamId,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        return Ok(
            await locations.GetNearbyAsync(
                latitude,
                longitude,
                projectId,
                limit,
                teamId,
                ct));
    }
}
