using FieldVisit.Application;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Nondeploy Option C leader mutation gate. JWT roles and team ids are hints,
/// never a substitute for current HR, role, effective assignment and a dated
/// leader appointment in the target organization.
/// </summary>
public static class V180LeaderDraftLiveScope
{
    public static async Task RequireAsync(
        AppDbContext db, CurrentUserDto actor, int? teamId, CancellationToken ct)
    {
        var tokenAdmin = actor.Roles.Any(r => r.Equals("admin", StringComparison.OrdinalIgnoreCase));
        var tokenLeader = actor.Roles.Any(r => r.Equals("leader", StringComparison.OrdinalIgnoreCase));
        if (!tokenLeader || tokenAdmin) return;
        if (!actor.OrganizationId.HasValue || !teamId.HasValue
            || !actor.TeamIds.Contains(teamId.Value))
            throw new UnauthorizedAccessException("OPTIONC_LEADER_TEAM_NOT_CURRENT");

        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == actor.UserId && x.OrganizationId == actor.OrganizationId, ct);
        var identity = await db.UserIdentityProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == actor.UserId, ct);
        if (account is null || !account.IsActive || identity?.EmploymentId is null
            || identity.UserType != FieldVisit.Domain.Entities.UserTypes.Internal
            || !(await new V170AccessControl(db).EvaluateLoginAsync(actor.UserId, account.IsActive, ct)).IsAllowed
            || !await db.Employments.AsNoTracking().AnyAsync(x => x.EmploymentId == identity.EmploymentId && x.OrganizationId == actor.OrganizationId, ct))
            throw new UnauthorizedAccessException("LEADER_DRAFT_HR_OR_ORGANIZATION_DENIED");
        var today = BusinessTime.Today;
        var currentLeaderRole = await (
            from assignment in db.UserRoleAssignments.AsNoTracking()
            join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.RoleId
            where assignment.UserId == actor.UserId && role.IsActive
                && role.RoleCode.ToUpper() == "LEADER"
                && assignment.EffectiveFrom <= today
                && (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo >= today)
            select assignment.UserRoleAssignmentId).AnyAsync(ct);
        var projectedLeaderRole = await (
            from assignment in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.RoleId
            where assignment.UserId == actor.UserId && role.IsActive
                && role.RoleCode.ToUpper() == "LEADER"
            select assignment.UserRoleId).AnyAsync(ct);
        if (!currentLeaderRole || !projectedLeaderRole)
            throw new UnauthorizedAccessException("OPTIONC_LEADER_ROLE_NOT_CURRENT");

        var teamValid = await db.Teams.AsNoTracking().AnyAsync(x =>
            x.TeamId == teamId.Value && x.OrganizationId == actor.OrganizationId
            && x.IsActive
            && (!x.EffectiveFrom.HasValue || x.EffectiveFrom <= today)
            && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        var assignmentValid = await db.UserTeamScopes.AsNoTracking().AnyAsync(x =>
                x.UserId == actor.UserId && x.TeamId == teamId.Value && x.IsActive, ct)
            && await db.UserTeamAssignments.AsNoTracking().AnyAsync(x =>
                x.UserId == actor.UserId && x.TeamId == teamId.Value
                && x.EffectiveFrom <= today
                && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        var employmentId = await db.UserIdentityProfiles.AsNoTracking()
            .Where(x => x.UserId == actor.UserId)
            .Select(x => x.EmploymentId).FirstOrDefaultAsync(ct);
        var directAppointment = employmentId.HasValue
            && await db.TeamLeaderAssignments.AsNoTracking().AnyAsync(x =>
                x.EmploymentId == employmentId.Value && x.TeamId == teamId.Value
                && x.EffectiveFrom <= today
                && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        // A delegated leader must still satisfy the same live HR, role,
        // explicit token scope, active team and effective team assignment
        // gates. A delegation alone never expands team authorization.
        var delegatedAppointment = employmentId.HasValue
            && await (from d in db.TeamLeaderDelegations.AsNoTracking()
                      join a in db.TeamLeaderAssignments.AsNoTracking()
                        on d.TeamLeaderAssignmentId equals a.TeamLeaderAssignmentId
                      where d.DelegateEmploymentId == employmentId.Value
                        && a.TeamId == teamId.Value
                        && a.EffectiveFrom <= today
                        && (!a.EffectiveTo.HasValue || a.EffectiveTo >= today)
                        && d.EffectiveFrom <= today && d.EffectiveTo >= today
                      select d.TeamLeaderDelegationId).AnyAsync(ct);
        if (!V180OptionCLeaderDelegationRules.IsAuthorized(
                currentLeaderRole && projectedLeaderRole,
                actor.TeamIds.Contains(teamId.Value),
                teamValid, assignmentValid,
                directAppointment || delegatedAppointment,
                actor.OrganizationId.HasValue))
            throw new UnauthorizedAccessException("OPTIONC_LEADER_APPOINTMENT_NOT_CURRENT");
    }
}
