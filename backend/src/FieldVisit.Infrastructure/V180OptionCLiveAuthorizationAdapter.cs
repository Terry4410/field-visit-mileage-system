using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Reads Option C actor authority from the live HR, role and team projections.
/// It deliberately treats JWT identity as an actor identifier only; claims do
/// not participate in any permission decision. Every read must be enlisted in
/// the caller's serializable decision transaction so concurrent revocation is
/// either observed before authorization or waits until the decision commits.
/// </summary>
public sealed class V180OptionCLiveAuthorizationAdapter(AppDbContext db)
{
    public async Task<V180OptionCLiveDecisionEvidence> AuthorizeDecisionAsync(
        int requesterUserId, int reviewerUserId, int organizationId, int teamId,
        DateOnly today, CancellationToken ct)
    {
        RequireSerializableTransaction();
        if (requesterUserId <= 0 || reviewerUserId <= 0 || organizationId <= 0
            || teamId <= 0 || requesterUserId == reviewerUserId)
            throw new UnauthorizedAccessException("OPTIONC_ACTOR_SCOPE_DENIED");

        var reviewer = await ReadInternalActorAsync(reviewerUserId, organizationId, today, ct);
        var requester = await ReadInternalActorAsync(requesterUserId, organizationId, today, ct);
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TeamId == teamId && x.OrganizationId == organizationId && x.IsActive
            && (!x.EffectiveFrom.HasValue || x.EffectiveFrom <= today)
            && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        if (team is null)
            throw new UnauthorizedAccessException("OPTIONC_TEAM_NOT_CURRENT");

        var requesterEligible = await IsRequesterEligibleAsync(
            requester, organizationId, teamId, today, ct);
        if (!requesterEligible)
            throw new UnauthorizedAccessException("OPTIONC_REQUESTER_NOT_ELIGIBLE");

        var reviewerRoles = await EffectiveRolesAsync(reviewerUserId, today, ct);
        if (!reviewerRoles.Contains("admin"))
            throw new UnauthorizedAccessException("OPTIONC_REVIEWER_ADMIN_REVOKED");
        var reviewerTeamScope = await db.UserTeamScopes.AsNoTracking().AnyAsync(x =>
                x.UserId == reviewerUserId && x.TeamId == teamId && x.IsActive, ct)
            && await db.UserTeamAssignments.AsNoTracking().AnyAsync(x =>
                x.UserId == reviewerUserId && x.TeamId == teamId
                && x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        if (!reviewerTeamScope)
            throw new UnauthorizedAccessException("OPTIONC_REVIEWER_TEAM_SCOPE_DENIED");

        // Reviewer is authorized at organization scope. The request and its
        // source Location are independently locked/checked against this exact
        // team by the publication transaction before any write.
        return new(requesterUserId, reviewerUserId, organizationId, teamId,
            RequesterEligible: true, IndependentAdminEligible: true,
            OrganizationMatches: requester.OrganizationId == organizationId
                && reviewer.OrganizationId == organizationId,
            TeamMatches: team.TeamId == teamId);
    }

    public async Task<int> ResolveLiveOrganizationAsync(int actorUserId, CancellationToken ct)
    {
        RequireSerializableTransaction();
        var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            x => x.UserId == actorUserId && x.IsActive && x.OrganizationId != null, ct);
        if (actor is null || actor.OrganizationId is null or <= 0)
            throw new UnauthorizedAccessException("OPTIONC_ACTOR_ACCOUNT_INVALID");
        _ = await ReadInternalActorAsync(actorUserId, actor.OrganizationId.Value,
            BusinessTime.Today, ct);
        return actor.OrganizationId.Value;
    }

    private async Task<LiveActor> ReadInternalActorAsync(
        int userId, int organizationId, DateOnly today, CancellationToken ct)
    {
        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(x =>
            x.UserId == userId && x.OrganizationId == organizationId && x.IsActive, ct);
        var profile = await db.UserIdentityProfiles.AsNoTracking().SingleOrDefaultAsync(
            x => x.UserId == userId, ct);
        // Unlike legacy login compatibility, approval requires complete HR
        // linkage. Missing profile/employment evidence is never grandfathered.
        if (account is null || profile is null || profile.UserType != UserTypes.Internal
            || profile.EmploymentId is null)
            throw new UnauthorizedAccessException("OPTIONC_HR_EVIDENCE_MISSING");

        var access = await new V170AccessControl(db).EvaluateLoginAsync(
            userId, account.IsActive, ct);
        if (!access.IsAllowed || string.IsNullOrWhiteSpace(access.EmploymentStatus))
            throw new UnauthorizedAccessException("OPTIONC_HR_STATUS_DENIED");
        var employment = await db.Employments.AsNoTracking().SingleOrDefaultAsync(x =>
            x.EmploymentId == profile.EmploymentId.Value
            && x.OrganizationId == organizationId
            && x.LegacyUserId == userId
            && (!x.HireDate.HasValue || x.HireDate <= today)
            && (!x.TerminationDate.HasValue || x.TerminationDate > today), ct);
        if (employment is null)
            throw new UnauthorizedAccessException("OPTIONC_EMPLOYMENT_EVIDENCE_INCONSISTENT");
        return new(userId, organizationId, profile.EmploymentId.Value);
    }

    private async Task<bool> IsRequesterEligibleAsync(
        LiveActor requester, int organizationId, int teamId, DateOnly today,
        CancellationToken ct)
    {
        var roles = await EffectiveRolesAsync(requester.UserId, today, ct);
        var member = roles.Contains("visitor")
            && await db.TeamMemberships.AsNoTracking().AnyAsync(x =>
                x.EmploymentId == requester.EmploymentId && x.TeamId == teamId
                && x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        var leader = roles.Contains("leader")
            && (await db.TeamLeaderAssignments.AsNoTracking().AnyAsync(x =>
                    x.EmploymentId == requester.EmploymentId && x.TeamId == teamId
                    && x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct)
                || await (from delegation in db.TeamLeaderDelegations.AsNoTracking()
                          join assignment in db.TeamLeaderAssignments.AsNoTracking()
                            on delegation.TeamLeaderAssignmentId equals assignment.TeamLeaderAssignmentId
                          where delegation.DelegateEmploymentId == requester.EmploymentId
                            && assignment.TeamId == teamId
                            && assignment.EffectiveFrom <= today
                            && (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo >= today)
                            && delegation.EffectiveFrom <= today && delegation.EffectiveTo >= today
                          select delegation.TeamLeaderDelegationId).AnyAsync(ct));
        var scoped = await db.UserTeamScopes.AsNoTracking().AnyAsync(x =>
                x.UserId == requester.UserId && x.TeamId == teamId && x.IsActive, ct)
            && await db.UserTeamAssignments.AsNoTracking().AnyAsync(x =>
                x.UserId == requester.UserId && x.TeamId == teamId
                && x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        return requester.OrganizationId == organizationId && scoped && (member || leader);
    }

    private async Task<HashSet<string>> EffectiveRolesAsync(
        int userId, DateOnly today, CancellationToken ct)
    {
        var assigned = await (from assignment in db.UserRoleAssignments.AsNoTracking()
            join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.RoleId
            where assignment.UserId == userId && role.IsActive
                && assignment.EffectiveFrom <= today
                && (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo >= today)
            select role.RoleCode).ToListAsync(ct);
        var projected = await (from assignment in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.RoleId
            where assignment.UserId == userId && role.IsActive
            select role.RoleCode).ToListAsync(ct);
        var effective = new HashSet<string>(assigned, StringComparer.OrdinalIgnoreCase);
        effective.IntersectWith(projected);
        return effective;
    }

    private void RequireSerializableTransaction()
    {
        var tx = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("OPTIONC_SERIALIZABLE_TRANSACTION_REQUIRED");
        if (tx.GetDbTransaction().IsolationLevel != System.Data.IsolationLevel.Serializable
            || tx.GetDbTransaction().Connection != db.Database.GetDbConnection())
            throw new InvalidOperationException("OPTIONC_SERIALIZABLE_TRANSACTION_REQUIRED");
    }

    private sealed record LiveActor(int UserId, int OrganizationId, long EmploymentId);
}

public sealed record V180OptionCLiveDecisionEvidence(
    int RequesterUserId, int ReviewerUserId, int OrganizationId, int TeamId,
    bool RequesterEligible, bool IndependentAdminEligible,
    bool OrganizationMatches, bool TeamMatches);
