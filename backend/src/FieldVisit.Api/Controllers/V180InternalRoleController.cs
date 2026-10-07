using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/admin/people/internal-users")]
public sealed class V180InternalRoleController(V180InternalRoleCommandService roles) : ControllerBase
{
    [HttpPut("{userId:int}/roles")]
    public async Task<IActionResult> Update(
        int userId,
        [FromBody] V180InternalRoleAccessRequest request,
        CancellationToken ct)
    {
        await roles.UpdateAsync(userId, request, ct);
        return NoContent();
    }
}
