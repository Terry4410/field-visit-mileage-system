using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "leader")]
[Route("api/v1/managed-locations/leader-authorization")]
public sealed class V180LeaderLocationAuthorizationController(
    ICurrentUserService current,
    V180LeaderLocationAuthorizationService authorization,
    AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TeamScopeDto>>> Get(CancellationToken ct)
    {
        var user = current.GetRequired();
        var ids = await authorization.TrustedManagedTeamsAsync(user, ct);
        var rows = await db.Teams.AsNoTracking().Where(x => ids.Contains(x.TeamId)).OrderBy(x => x.TeamCode)
            .Select(x => new TeamScopeDto(x.TeamId, x.TeamName, false)).ToListAsync(ct);
        return Ok(rows);
    }
}
