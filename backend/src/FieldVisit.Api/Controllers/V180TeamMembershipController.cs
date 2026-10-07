using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/admin/people/internal-users")]
public sealed class V180TeamMembershipController(V180TeamMembershipCommandService memberships) : ControllerBase
{
    [HttpPut("{userId:int}/team-memberships")]
    public async Task<IActionResult> Update(
        int userId,
        [FromBody] V180ReplaceTeamMembershipsRequest request,
        CancellationToken ct)
    {
        await memberships.UpdateAsync(userId, request, ct);
        return NoContent();
    }
}
