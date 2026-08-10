using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FieldVisit.Application;
using Microsoft.IdentityModel.Tokens;

namespace FieldVisit.Api;

public sealed class AuthOptions
{
    public string Issuer { get; set; } = "FieldVisit.UAT";
    public string Audience { get; set; } = "FieldVisit.UAT.Users";
    public string JwtKey { get; set; } = "";
    public string DemoPassword { get; set; } = "";
}

public sealed class TokenService(IConfiguration configuration) : ITokenService
{
    public (string Token, DateTime ExpiresAtUtc) Create(CurrentUserDto user)
    {
        var opt = configuration.GetSection("Auth").Get<AuthOptions>() ?? throw new InvalidOperationException("Auth 設定不存在。");
        if (opt.JwtKey.Length < 32) throw new InvalidOperationException("Auth__JwtKey 至少需要 32 字元。");
        var expires = DateTime.UtcNow.AddHours(8);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new("employee_no", user.EmployeeNo)
        };
        if (user.OrganizationId.HasValue) claims.Add(new("organization_id", user.OrganizationId.Value.ToString()));
        if (user.TeamId.HasValue) claims.Add(new("team_id", user.TeamId.Value.ToString()));
        if (!string.IsNullOrWhiteSpace(user.TeamName)) claims.Add(new("team_name", user.TeamName));
        foreach (var role in user.Roles) claims.Add(new(ClaimTypes.Role, role.ToLowerInvariant()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.JwtKey));
        var token = new JwtSecurityToken(opt.Issuer, opt.Audience, claims, expires: expires, signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public CurrentUserDto GetRequired()
    {
        var p = accessor.HttpContext?.User;
        if (p?.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException("尚未登入。");
        var userId = int.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException("Token 缺少 UserId。"));
        int? orgId = int.TryParse(p.FindFirstValue("organization_id"), out var o) ? o : null;
        int? teamId = int.TryParse(p.FindFirstValue("team_id"), out var t) ? t : null;
        return new CurrentUserDto(
            userId,
            p.FindFirstValue("employee_no") ?? "",
            p.FindFirstValue(ClaimTypes.Name) ?? "",
            null,
            orgId,
            teamId,
            p.FindFirstValue("team_name"),
            p.FindAll(ClaimTypes.Role).Select(x => x.Value.ToLowerInvariant()).Distinct().ToList());
    }
}
