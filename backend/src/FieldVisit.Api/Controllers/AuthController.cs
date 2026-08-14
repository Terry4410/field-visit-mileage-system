using FieldVisit.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class AuthController(
    AuthService auth,
    IUserRepository users,
    IConfiguration config) : ControllerBase
{
    [HttpPost("auth/demo-login")]
    [AllowAnonymous]
    public async Task<ActionResult<DemoLoginResponse>>
        Login(
            DemoLoginRequest request,
            CancellationToken ct)
    {
        V170AuthenticationRules.EnsureMode(
            config["Auth:Mode"],
            AuthenticationModes.Demo);

        return Ok(
            await auth.LoginAsync(
                request,
                config["Auth:DemoPassword"] ?? "",
                ct));
    }

    [HttpPost("auth/entra-login")]
    [AllowAnonymous]
    public async Task<ActionResult<DemoLoginResponse>>
        EntraLogin(
            CancellationToken ct)
    {
        V170AuthenticationRules.EnsureMode(
            config["Auth:Mode"],
            AuthenticationModes.Entra);

        var authResult =
            await HttpContext.AuthenticateAsync(
                AuthSchemes.Entra);

        if (!authResult.Succeeded
            || authResult.Principal is null)
        {
            throw new UnauthorizedAccessException(
                "Microsoft Entra token 驗證失敗。");
        }

        var principal =
            authResult.Principal;

        var tenantId =
            V170AuthenticationRules.ParseRequiredGuidClaim(
                principal.FindFirst("tid")?.Value,
                "tid");

        var objectId =
            V170AuthenticationRules.ParseRequiredGuidClaim(
                principal.FindFirst("oid")?.Value,
                "oid");

        var configuredTenant =
            V170AuthenticationRules.ParseRequiredGuidClaim(
                config["Auth:Entra:TenantId"],
                "configured tenant id");

        if (tenantId != configuredTenant)
        {
            throw new UnauthorizedAccessException(
                "Microsoft Entra Tenant 不符合系統設定。");
        }

        var requiredScope =
            config["Auth:Entra:RequiredScope"];

        if (!V170AuthenticationRules.HasRequiredScope(
            principal.FindFirst("scp")?.Value,
            requiredScope))
        {
            throw new UnauthorizedAccessException(
                "Microsoft Entra token 缺少必要的 API Scope。");
        }

        var email =
            V170AuthenticationRules.NormalizeEmail(
                principal.FindFirst("email")?.Value
                ?? principal.FindFirst(
                    "preferred_username")?.Value
                ?? principal.FindFirst("upn")?.Value);

        var allowEmailBinding =
            config.GetValue<bool>(
                "Auth:Entra:AllowFirstLoginEmailBinding");

        return Ok(
            await auth.LoginEntraAsync(
                new EntraLoginIdentity(
                    tenantId,
                    objectId,
                    email),
                allowEmailBinding,
                ct));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<CurrentUserDto>>
        Me(
            CancellationToken ct)
    {
        var raw =
            User.FindFirst(
                System.Security.Claims
                    .ClaimTypes.NameIdentifier)
                ?.Value;

        if (!int.TryParse(raw, out var userId))
            throw new UnauthorizedAccessException(
                "Token 缺少 UserId。");

        return Ok(
            await users.GetProfileAsync(
                userId,
                ct)
            ?? throw new UnauthorizedAccessException(
                "使用者不存在或已停用。"));
    }
}
