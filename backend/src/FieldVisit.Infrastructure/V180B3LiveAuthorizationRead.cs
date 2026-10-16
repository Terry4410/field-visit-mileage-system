using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Existing B3 read-only live role and team scope queries, shared with
/// isolated real-SQL regression tests. These queries DO NOT grant a role:
/// Active HR/identity is validated first, and token roles MUST intersect
/// with BOTH effective assignment and live compatibility projection.
/// Caller must own its own read/serializable transaction for mutations.
/// Does not enable ApproveAsync, SQL migrations, or feature flag.
/// </summary>
public static class V180B3LiveAuthorizationRead
{
    public static async Task<(CurrentUserDto User,bool Admin,bool Visitor)>
        EvaluateActorAsync(AppDbContext db,CurrentUserDto user,DateOnly today,
            CancellationToken ct)
    {
        await V180B3ActorEligibility.RequireAsync(db,user,ct);
        var assigned=await (from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.RoleId
            where a.UserId==user.UserId&&r.IsActive&&a.EffectiveFrom<=today
                &&(!a.EffectiveTo.HasValue||a.EffectiveTo.Value>=today)
            select r.RoleCode).ToListAsync(ct);
        var projected=await (from a in db.UserRoles.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.RoleId
            where a.UserId==user.UserId&&r.IsActive
            select r.RoleCode).ToListAsync(ct);
        var roles=V180LocationLiveRoleRules.Evaluate(user.Roles,assigned,projected);
        return(user,roles.Admin,roles.Visitor);
    }

    /// <summary>
    /// Team membership gate only. A true result alone does not authorize
    /// Location mutations. Caller MUST first require a live Visitor actor,
    /// token team scope, Location organization, owner and published state.
    /// </summary>
    public static async Task<bool> IsVisitorTeamValidAsync(
        AppDbContext db,int userId,int organizationId,int teamId,
        DateOnly today,CancellationToken ct)
    {
        // Scope and membership alone do not prove that TEAM is usable.
        var team=await db.Teams.AsNoTracking().Where(x=>x.TeamId==teamId)
            .Select(x=>new{x.OrganizationId,x.IsActive,x.EffectiveFrom,x.EffectiveTo})
            .SingleOrDefaultAsync(ct);
        if(team is null || !V180B3LocationTeamRules.IsEffectiveForOrganization(
            organizationId,team.OrganizationId,team.IsActive,
            team.EffectiveFrom,team.EffectiveTo,today))return false;
        var employment=await db.UserIdentityProfiles.AsNoTracking()
            .Where(x=>x.UserId==userId&&x.UserType==UserTypes.Internal)
            .Select(x=>x.EmploymentId).FirstOrDefaultAsync(ct);
        if(!employment.HasValue)return false;
        return await db.UserTeamScopes.AsNoTracking().AnyAsync(x=>
               x.UserId==userId&&x.TeamId==teamId&&x.IsActive,ct)
            &&await db.UserTeamAssignments.AsNoTracking().AnyAsync(x=>
               x.UserId==userId&&x.TeamId==teamId&&x.EffectiveFrom<=today
               &&(!x.EffectiveTo.HasValue||x.EffectiveTo.Value>=today),ct)
            &&await db.TeamMemberships.AsNoTracking().AnyAsync(x=>
               x.EmploymentId==employment.Value&&x.TeamId==teamId
               &&x.EffectiveFrom<=today
               &&(!x.EffectiveTo.HasValue||x.EffectiveTo.Value>=today),ct);
    }

    /// <summary>
    /// Revalidate the original requester's CURRENT authority when an
    /// independent Admin reviews the request. The requester JWT is no longer
    /// available; the original submit operation already required token/live
    /// intersection. This read never grants new submit or approve authority.
    /// </summary>
    public static async Task<bool> IsSubmitterTeamStillValidAsync(
        AppDbContext db, int userId, int organizationId, int teamId,
        DateOnly today, CancellationToken ct)
    {
        if (userId <= 0 || organizationId <= 0 || teamId <= 0) return false;
        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            x => x.UserId == userId && x.OrganizationId == organizationId, ct);
        if (account is null || !account.IsActive) return false;
        var profile = await db.UserIdentityProfiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.UserType, x.EmploymentId })
            .SingleOrDefaultAsync(ct);
        if (profile is null || profile.UserType != UserTypes.Internal
            || !profile.EmploymentId.HasValue) return false;
        if (!(await new V170AccessControl(db)
            .EvaluateLoginAsync(userId, account.IsActive, ct)).IsAllowed)
            return false;

        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(
            x => x.TeamId == teamId, ct);
        var teamEffective = team is not null
            && V180B3LocationTeamRules.IsEffectiveForOrganization(
                organizationId, team.OrganizationId, team.IsActive,
                team.EffectiveFrom, team.EffectiveTo, today);
        if (!teamEffective) return false;

        var teamAssigned = await db.UserTeamScopes.AsNoTracking().AnyAsync(
                x => x.UserId == userId && x.TeamId == teamId && x.IsActive, ct)
            && await db.UserTeamAssignments.AsNoTracking().AnyAsync(
                x => x.UserId == userId && x.TeamId == teamId
                    && x.EffectiveFrom <= today
                    && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        if (!teamAssigned) return false;

        var assigned = await (from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.RoleId
            where a.UserId == userId && r.IsActive && a.EffectiveFrom <= today
                && (!a.EffectiveTo.HasValue || a.EffectiveTo >= today)
            select r.RoleCode).ToListAsync(ct);
        var projected = await (from a in db.UserRoles.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.RoleId
            where a.UserId == userId && r.IsActive
            select r.RoleCode).ToListAsync(ct);
        var effectiveRoles = new HashSet<string>(
            assigned, StringComparer.OrdinalIgnoreCase);
        effectiveRoles.IntersectWith(projected);

        var visitorMember = effectiveRoles.Contains("visitor")
            && await db.TeamMemberships.AsNoTracking().AnyAsync(x =>
                x.EmploymentId == profile.EmploymentId.Value
                && x.TeamId == teamId && x.EffectiveFrom <= today
                && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        var directLeader = effectiveRoles.Contains("leader")
            && await db.TeamLeaderAssignments.AsNoTracking().AnyAsync(x =>
                x.EmploymentId == profile.EmploymentId.Value
                && x.TeamId == teamId && x.EffectiveFrom <= today
                && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        var delegatedLeader = effectiveRoles.Contains("leader")
            && await (from d in db.TeamLeaderDelegations.AsNoTracking()
                join a in db.TeamLeaderAssignments.AsNoTracking()
                    on d.TeamLeaderAssignmentId equals a.TeamLeaderAssignmentId
                where d.DelegateEmploymentId == profile.EmploymentId.Value
                    && a.TeamId == teamId && a.EffectiveFrom <= today
                    && (!a.EffectiveTo.HasValue || a.EffectiveTo >= today)
                    && d.EffectiveFrom <= today && d.EffectiveTo >= today
                select d.TeamLeaderDelegationId).AnyAsync(ct);
        return V180OptionCSubmitterReviewRules.IsEligible(
            true, account.OrganizationId == organizationId, teamEffective,
            teamAssigned, effectiveRoles.Contains("visitor"), visitorMember,
            effectiveRoles.Contains("leader"), directLeader || delegatedLeader);
    }
}
