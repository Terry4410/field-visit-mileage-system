using System.Security.Claims;
using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Api;

/// <summary>
/// WORK-only security candidate. OFF by default and not approved for
/// UAT/production activation. Checks already-issued JWT sessions against
/// authoritative HR state; grants no roles, Manager rights, or B3 approval.
/// </summary>
public sealed class V180LiveSessionGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IConfiguration configuration,
        AppDbContext db, IV170AccessControl access)
    {
        if (!configuration.GetValue<bool>("Auth:LiveSessionGuard:Enabled")
            || !context.Request.Path.StartsWithSegments("/api/v1")
            || context.User.Identity?.IsAuthenticated != true
            || context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() != null)
        {
            await next(context);
            return;
        }

        var identityClaims=context.User.FindAll(ClaimTypes.NameIdentifier).ToArray();
        if (identityClaims.Length!=1
            || !int.TryParse(identityClaims[0].Value,out var userId)
            || userId<=0)
            throw new UnauthorizedAccessException("SESSION_ACCESS_REVOKED");

        // Reads from the persisted account and HR period *on each request*.
        // Missing DB access fails closed (exception, never calls next).
        var account=await db.Users.AsNoTracking()
            .Where(x=>x.UserId==userId)
            .Select(x=>new {x.IsActive,x.OrganizationId})
            .SingleOrDefaultAsync(context.RequestAborted);
        if(account is null || !account.IsActive)
            throw new UnauthorizedAccessException("SESSION_ACCESS_REVOKED");

        var orgClaims=context.User.FindAll("organization_id").ToArray();
        if(orgClaims.Length>1
           || (orgClaims.Length==1 &&
               (!int.TryParse(orgClaims[0].Value,out var tokenOrg)
                || account.OrganizationId!=tokenOrg))
           || (orgClaims.Length==0 && account.OrganizationId.HasValue))
            throw new UnauthorizedAccessException("SESSION_ACCESS_REVOKED");

        var hr=await access.EvaluateLoginAsync(userId,account.IsActive,context.RequestAborted);
        if(!hr.IsAllowed)
            throw new UnauthorizedAccessException("SESSION_ACCESS_REVOKED");

        await next(context);
    }
}
