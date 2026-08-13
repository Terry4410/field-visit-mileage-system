using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/admin/people")]
public sealed class V170PeopleAdminController(
    V170PeopleAdminService service)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<
        PagedResult<V170PeopleRowDto>>> Query(
        [FromQuery] V170PeopleQueryRequest request,
        CancellationToken ct)
        => Ok(
            await service.QueryAsync(
                request,
                ct));

    [HttpGet("{userId:int}")]
    public async Task<ActionResult<
        V170PersonDetailDto>> Get(
        int userId,
        CancellationToken ct)
        => Ok(
            await service.GetAsync(
                userId,
                ct));
}
