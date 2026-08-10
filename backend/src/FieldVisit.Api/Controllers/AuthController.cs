using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class AuthController(AuthService auth, ICurrentUserService current, IConfiguration config) : ControllerBase
{
    [HttpPost("auth/demo-login")]
    [AllowAnonymous]
    public async Task<ActionResult<DemoLoginResponse>> Login(DemoLoginRequest request, CancellationToken ct) =>
        Ok(await auth.LoginAsync(request, config["Auth:DemoPassword"] ?? "", ct));

    [HttpGet("me")]
    [Authorize]
    public ActionResult<CurrentUserDto> Me() => Ok(current.GetRequired());
}
