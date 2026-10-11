using System.Security.Claims;
using System.Text.Json;
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
        AppDbContext db, IV170AccessControl access, IUserRepository users)
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

        // Team claims in JWT are a point-in-time snapshot. Resolve the live
        // profile from authoritative effective-dated/legacy memberships;
        // no token may retain a removed, disabled or re-primary team.
        var profile=await users.GetProfileAsync(userId,context.RequestAborted)
            ??throw new UnauthorizedAccessException("SESSION_TEAM_REVOKED");
        var teamClaims=context.User.FindAll("team_id").ToArray();
        if(teamClaims.Length>1)
            throw new UnauthorizedAccessException("SESSION_TEAM_REVOKED");
        int? tokenPrimary=null;
        if(teamClaims.Length==1)
        {
            if(!int.TryParse(teamClaims[0].Value,out var primary)||primary<=0)
                throw new UnauthorizedAccessException("SESSION_TEAM_REVOKED");
            tokenPrimary=primary;
        }
        if(profile.TeamId!=tokenPrimary)
            throw new UnauthorizedAccessException("SESSION_TEAM_REVOKED");

        var scopeClaims=context.User.FindAll("team_scopes").ToArray();
        if(scopeClaims.Length>1)
            throw new UnauthorizedAccessException("SESSION_TEAM_REVOKED");
        List<TeamScopeDto> tokenScopes=[];
        if(scopeClaims.Length==1)
        {
            try
            {
                tokenScopes=JsonSerializer.Deserialize<List<TeamScopeDto>>(
                    scopeClaims[0].Value)
                    ??throw new JsonException("team scope null");
            }
            catch(JsonException)
            {
                throw new UnauthorizedAccessException("SESSION_TEAM_REVOKED");
            }
        }
        var liveTeams=(profile.TeamScopes??[]).ToList();
        // Team names are display-only and may be renamed legitimately.
        // Authorization relies on exact team IDs and primary flags.
        if(tokenScopes.Any(x=>x.TeamId<=0)
            ||tokenScopes.Select(x=>x.TeamId).Distinct().Count()!=tokenScopes.Count
            ||liveTeams.Count!=tokenScopes.Count
            ||tokenScopes.Any(x=>!liveTeams.Any(y=>
                y.TeamId==x.TeamId && y.IsPrimary==x.IsPrimary)))
            throw new UnauthorizedAccessException("SESSION_TEAM_REVOKED");

        var teamIds=liveTeams.Select(x=>x.TeamId)
            .Concat(profile.TeamId.HasValue?[profile.TeamId.Value]:Array.Empty<int>())
            .Distinct().ToArray();
        if(teamIds.Length>0)
        {
            var today=BusinessTime.Today;
            var valid=await db.Teams.AsNoTracking()
                .Where(x=>teamIds.Contains(x.TeamId)
                    && x.IsActive && x.OrganizationId==account.OrganizationId
                    && (!x.EffectiveFrom.HasValue||x.EffectiveFrom.Value<=today)
                    && (!x.EffectiveTo.HasValue||x.EffectiveTo.Value>=today))
                .Select(x=>x.TeamId).ToListAsync(context.RequestAborted);
            if(valid.Count!=teamIds.Length)
                throw new UnauthorizedAccessException("SESSION_TEAM_REVOKED");
        }

        // Supervisor DataScopes are NOT JWT claims. Each request therefore
        // requires an independently live, effective, same-tenant grant.
        // This does not confer export/mutation or Manager privileges.
        if(jwtRoles.Contains("supervisor",StringComparer.OrdinalIgnoreCase)
            && string.Equals(userType,UserTypes.External,StringComparison.OrdinalIgnoreCase))
        {
            var today=BusinessTime.Today;
            var grants=await db.UserDataScopes.AsNoTracking()
                .Where(x=>x.UserId==userId&&x.EffectiveFrom<=today
                    && (!x.EffectiveTo.HasValue||x.EffectiveTo.Value>=today))
                .Select(x=>new {x.ScopeType,x.OrganizationId,x.TeamId})
                .ToListAsync(context.RequestAborted);
            if(grants.Count==0)
                throw new UnauthorizedAccessException("SESSION_DATA_SCOPE_REVOKED");

            var scopedTeams=grants.Where(x=>x.TeamId.HasValue)
                .Select(x=>x.TeamId!.Value).Distinct().ToArray();
            var allowedTeams=await db.Teams.AsNoTracking()
                .Where(x=>scopedTeams.Contains(x.TeamId)
                    && x.IsActive && x.OrganizationId==account.OrganizationId
                    && (!x.EffectiveFrom.HasValue||x.EffectiveFrom.Value<=today)
                    && (!x.EffectiveTo.HasValue||x.EffectiveTo.Value>=today))
                .Select(x=>x.TeamId).ToListAsync(context.RequestAborted);
            var allowedTeamIds=allowedTeams.ToHashSet();
            if(grants.Any(x=>
                x.ScopeType==DataScopeTypes.Organization
                    ? x.OrganizationId!=account.OrganizationId || x.TeamId.HasValue
                    : x.ScopeType==DataScopeTypes.Team
                        ? !x.TeamId.HasValue
                            || !allowedTeamIds.Contains(x.TeamId.Value)
                            || (x.OrganizationId.HasValue
                                && x.OrganizationId!=account.OrganizationId)
                        : true))
                throw new UnauthorizedAccessException("SESSION_DATA_SCOPE_INVALID");
        }

        await next(context);
    }

    private static string NormalizeRole(string role)=>
        role.Trim().ToLowerInvariant() switch
        {
            "government"=>"supervisor",
            var normalized=>normalized
        };
}
