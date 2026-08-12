using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class AuthController(AuthService auth, IUserRepository users, IConfiguration config) : ControllerBase
{
    [HttpPost("auth/demo-login")]
    [AllowAnonymous]
    public async Task<ActionResult<DemoLoginResponse>> Login(DemoLoginRequest request, CancellationToken ct) =>
        Ok(await auth.LoginAsync(request, config["Auth:DemoPassword"] ?? "", ct));

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
    {
        var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(raw, out var userId)) throw new UnauthorizedAccessException("Token 缺少 UserId。");
        return Ok(await users.GetProfileAsync(userId, ct) ?? throw new UnauthorizedAccessException("使用者不存在或已停用。"));
    }
}
