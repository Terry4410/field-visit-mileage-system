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

    [HttpGet("favorites")]
    [Authorize(Roles = "visitor,leader,admin")]
    public async Task<ActionResult<
        IReadOnlyList<V170LocationFavoriteDto>>> Favorites(
        CancellationToken ct)
    {
        return Ok(
            await locations.GetFavoritesAsync(ct));
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
        CancellationToken ct = default)
    {
        return Ok(
            await locations.GetRecentAsync(
                limit,
                ct));
    }

    [HttpGet("nearby")]
    [Authorize(Roles = "visitor,leader,admin")]
    public async Task<ActionResult<
        IReadOnlyList<V170LocationNearbyDto>>> Nearby(
        [FromQuery] decimal latitude,
        [FromQuery] decimal longitude,
        [FromQuery] int? projectId,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        return Ok(
            await locations.GetNearbyAsync(
                latitude,
                longitude,
                projectId,
                limit,
                ct));
    }
}
