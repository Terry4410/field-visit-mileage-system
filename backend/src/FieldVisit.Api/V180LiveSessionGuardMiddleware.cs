using System.Security.Claims;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Api;

/// <summary>
/// WORK-only security candidate. OFF by default and not approved for
/// UAT/production activation. Checks already-issued JWT sessions against
/// authoritative HR state AND effective roles; grants no roles, Manager rights, or B3 approval.
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

        // JWT role claims are a historical snapshot, not an authorization
        // grant. Revalidate the entire claimed set, not only the selected
        // X-Active-Role. Partial removal also requires a new JWT.
        var jwtRoles=context.User.FindAll(ClaimTypes.Role)
            .Select(x=>NormalizeRole(x.Value)).ToArray();
        if(jwtRoles.Length==0 || jwtRoles.Any(string.IsNullOrWhiteSpace))
            throw new UnauthorizedAccessException("SESSION_ROLE_REVOKED");

        var projection=await (
            from userRole in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking()
                on userRole.RoleId equals role.RoleId
            where userRole.UserId==userId && role.IsActive
            select role.RoleCode
        ).ToListAsync(context.RequestAborted);
        var projected=new HashSet<string>(
            projection.Select(NormalizeRole),StringComparer.OrdinalIgnoreCase);

        var userType=await db.UserIdentityProfiles.AsNoTracking()
            .Where(x=>x.UserId==userId).Select(x=>x.UserType)
            .SingleOrDefaultAsync(context.RequestAborted);
        if(userType is not null
            && !string.Equals(userType,UserTypes.Internal,StringComparison.OrdinalIgnoreCase)
            && !string.Equals(userType,UserTypes.External,StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("SESSION_ROLE_REVOKED");
        // An external supervisor may read within separately authorized
        // scopes, never retain a legacy visitor/leader/admin JWT.
        if(string.Equals(userType,UserTypes.External,StringComparison.OrdinalIgnoreCase)
            && jwtRoles.Any(x=>x!="supervisor"))
            throw new UnauthorizedAccessException("SESSION_ROLE_REVOKED");
        if(userType is not null)
        {
            var today=BusinessTime.Today;
            var effective=await (
                from assignment in db.UserRoleAssignments.AsNoTracking()
                join role in db.Roles.AsNoTracking()
                    on assignment.RoleId equals role.RoleId
                where assignment.UserId==userId && role.IsActive
                    && assignment.EffectiveFrom<=today
                    && (!assignment.EffectiveTo.HasValue
                        || assignment.EffectiveTo.Value>=today)
                select role.RoleCode
            ).ToListAsync(context.RequestAborted);
            projected.IntersectWith(effective.Select(NormalizeRole));
        }

        if(jwtRoles.Any(role=>!projected.Contains(role)))
            throw new UnauthorizedAccessException("SESSION_ROLE_REVOKED");

        await next(context);
    }

    private static string NormalizeRole(string role)=>
        role.Trim().ToLowerInvariant() switch
        {
            "government"=>"supervisor",
            var normalized=>normalized
        };
}
