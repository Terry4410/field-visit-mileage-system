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
    public async Task<int> RequireIndependentAdminAsync(
        int actorUserId, DateOnly today, CancellationToken ct)
    {
        RequireSerializableTransaction();
        var actor = await ReadInternalActorAsync(actorUserId, 0, today, ct,
            allowOrganizationLookup: true);
        var roles = await EffectiveRolesAsync(actorUserId, today, ct);
        if (!roles.Contains("admin"))
            throw new UnauthorizedAccessException("OPTIONC_REVIEWER_ADMIN_REVOKED");
        return actor.OrganizationId;
    }

    public async Task<V180OptionCLiveSubmissionEvidence> AuthorizeSubmissionAsync(
        int actorUserId, int locationId, DateOnly today, CancellationToken ct)
    {
        RequireSerializableTransaction();
        var actor = await ReadInternalActorAsync(actorUserId, 0, today, ct,
            allowOrganizationLookup: true);
        var location = await db.Locations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.LocationId == locationId, ct)
            ?? throw new KeyNotFoundException("OPTIONC_LOCATION_NOT_FOUND");
        if (location.OrganizationId is not int organizationId || organizationId <= 0
            || location.CreatedByUserId != actorUserId
            || location.TeamId is not int teamId || teamId <= 0
            || !string.Equals(location.LocationType, "Customer", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(location.ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase)
            || !location.IsActive || actor.OrganizationId != organizationId)
            throw new UnauthorizedAccessException("OPTIONC_SUBMISSION_SCOPE_DENIED");
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TeamId == teamId && x.OrganizationId == organizationId && x.IsActive
            && (!x.EffectiveFrom.HasValue || x.EffectiveFrom <= today)
            && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        if (team is null) throw new UnauthorizedAccessException("OPTIONC_TEAM_NOT_CURRENT");
        var roles = await EffectiveRolesAsync(actorUserId, today, ct);
        var scoped = await HasCurrentTeamScopeAsync(actorUserId, teamId, today, ct);
        var visitor = roles.Contains("visitor") && await db.TeamMemberships.AsNoTracking().AnyAsync(x =>
            x.EmploymentId == actor.EmploymentId && x.TeamId == teamId
            && x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);
        var leader = roles.Contains("leader") && (await db.TeamLeaderAssignments.AsNoTracking().AnyAsync(x =>
            x.EmploymentId == actor.EmploymentId && x.TeamId == teamId
            && x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct)
            || await (from d in db.TeamLeaderDelegations.AsNoTracking()
                      join a in db.TeamLeaderAssignments.AsNoTracking()
                        on d.TeamLeaderAssignmentId equals a.TeamLeaderAssignmentId
                      where d.DelegateEmploymentId == actor.EmploymentId && a.TeamId == teamId
                        && a.EffectiveFrom <= today && (!a.EffectiveTo.HasValue || a.EffectiveTo >= today)
                        && d.EffectiveFrom <= today && d.EffectiveTo >= today
                      select d.TeamLeaderDelegationId).AnyAsync(ct));
        if (!scoped || (!visitor && !leader))
            throw new UnauthorizedAccessException("OPTIONC_SUBMISSION_ACTOR_NOT_ELIGIBLE");
        return new(actorUserId, organizationId, teamId, visitor, leader);
    }

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

    public async Task<V180OptionCLiveLeaderReviewEvidence> AuthorizeLeaderReviewAsync(
        int requesterUserId, int reviewerUserId, int organizationId, int teamId,
        DateOnly today, CancellationToken ct)
    {
        RequireSerializableTransaction();
        if (requesterUserId <= 0 || reviewerUserId <= 0 || organizationId <= 0
            || teamId <= 0 || requesterUserId == reviewerUserId)
            throw new UnauthorizedAccessException("OPTIONC_LEADER_SELF_REVIEW_DENIED");
        var reviewer = await ReadInternalActorAsync(reviewerUserId, organizationId, today, ct);
        var requester = await ReadInternalActorAsync(requesterUserId, organizationId, today, ct);
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TeamId == teamId && x.OrganizationId == organizationId && x.IsActive
            && (!x.EffectiveFrom.HasValue || x.EffectiveFrom <= today)
            && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct)
            ?? throw new UnauthorizedAccessException("OPTIONC_TEAM_NOT_CURRENT");
        if (!await IsRequesterEligibleAsync(requester, organizationId, teamId, today, ct))
            throw new UnauthorizedAccessException("OPTIONC_REQUESTER_NOT_ELIGIBLE");
        var roles = await EffectiveRolesAsync(reviewerUserId, today, ct);
        if (!roles.Contains("leader"))
            throw new UnauthorizedAccessException("OPTIONC_LEADER_ROLE_REVOKED");
        var scoped = await HasCurrentTeamScopeAsync(reviewerUserId, teamId, today, ct);
        var appointed = await db.TeamLeaderAssignments.AsNoTracking().AnyAsync(x =>
                x.EmploymentId == reviewer.EmploymentId && x.TeamId == teamId
                && x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct)
            || await (from delegation in db.TeamLeaderDelegations.AsNoTracking()
                      join assignment in db.TeamLeaderAssignments.AsNoTracking()
                        on delegation.TeamLeaderAssignmentId equals assignment.TeamLeaderAssignmentId
                      where delegation.DelegateEmploymentId == reviewer.EmploymentId
                        && assignment.TeamId == teamId
                        && assignment.EffectiveFrom <= today
                        && (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo >= today)
                        && delegation.EffectiveFrom <= today && delegation.EffectiveTo >= today
                      select delegation.TeamLeaderDelegationId).AnyAsync(ct);
        if (!scoped || !appointed)
            throw new UnauthorizedAccessException("OPTIONC_LEADER_APPOINTMENT_REQUIRED");
        return new(requesterUserId, reviewerUserId, organizationId, teamId,
            RequesterEligible: true, ReviewerLeaderEligible: true,
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
        int userId, int organizationId, DateOnly today, CancellationToken ct,
        bool allowOrganizationLookup = false)
    {
        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(x =>
            x.UserId == userId && x.IsActive
            && (allowOrganizationLookup || x.OrganizationId == organizationId), ct);
        var profile = await db.UserIdentityProfiles.AsNoTracking().SingleOrDefaultAsync(
            x => x.UserId == userId, ct);
        // Unlike legacy login compatibility, approval requires complete HR
        // linkage. Missing profile/employment evidence is never grandfathered.
        if (account is null || profile is null || profile.UserType != UserTypes.Internal
            || profile.EmploymentId is null || account.OrganizationId is null)
            throw new UnauthorizedAccessException("OPTIONC_HR_EVIDENCE_MISSING");
        if (!allowOrganizationLookup && account.OrganizationId != organizationId)
            throw new UnauthorizedAccessException("OPTIONC_HR_EVIDENCE_MISSING");
        organizationId = account.OrganizationId.Value;

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

    private async Task<bool> HasCurrentTeamScopeAsync(int userId, int teamId,
        DateOnly today, CancellationToken ct) =>
        await db.UserTeamScopes.AsNoTracking().AnyAsync(x =>
            x.UserId == userId && x.TeamId == teamId && x.IsActive, ct)
        && await db.UserTeamAssignments.AsNoTracking().AnyAsync(x =>
            x.UserId == userId && x.TeamId == teamId
            && x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo >= today), ct);

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

public sealed record V180OptionCLiveSubmissionEvidence(
    int ActorUserId, int OrganizationId, int TeamId, bool VisitorEligible,
    bool LeaderEligible);

public sealed record V180OptionCLiveLeaderReviewEvidence(
    int RequesterUserId, int ReviewerUserId, int OrganizationId, int TeamId,
    bool RequesterEligible, bool ReviewerLeaderEligible,
    bool OrganizationMatches, bool TeamMatches);
