using FieldVisit.Application;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Nondeploy Option C leader mutation gate. JWT roles and team ids are hints,
/// never a substitute for current HR, role, effective assignment and a dated
/// leader appointment in the target organization.
/// </summary>
public static class V180OptionCLiveLeaderScope
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

        await V180B3ActorEligibility.RequireAsync(db, actor, ct);
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
        var assignmentValid = await db.UserTeamAssignments.AsNoTracking().AnyAsync(x =>
            x.UserId == actor.UserId && x.TeamId == teamId.Value
            && x.EffectiveFrom <= today
            && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        var employmentId = await db.UserIdentityProfiles.AsNoTracking()
            .Where(x => x.UserId == actor.UserId)
            .Select(x => x.EmploymentId).FirstOrDefaultAsync(ct);
        var leaderAppointment = employmentId.HasValue
            && await db.TeamLeaderAssignments.AsNoTracking().AnyAsync(x =>
                x.EmploymentId == employmentId.Value && x.TeamId == teamId.Value
                && x.EffectiveFrom <= today
                && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        if (!teamValid || !assignmentValid || !leaderAppointment)
            throw new UnauthorizedAccessException("OPTIONC_LEADER_APPOINTMENT_NOT_CURRENT");
    }
}
