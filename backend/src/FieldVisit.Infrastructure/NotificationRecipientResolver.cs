using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class EfNotificationRecipientResolver(AppDbContext db) : INotificationRecipientResolver
{
    public async Task<IReadOnlyList<NotificationRecipient>> ResolveAsync(
        NotificationEventContext context,
        IReadOnlyCollection<string> recipientRuleCodes,
        CancellationToken ct)
    {
        var asOf = DateOnly.FromDateTime(context.EventOccurredAt);
        var result = new List<NotificationRecipient>();

        foreach (var rule in recipientRuleCodes.Distinct(StringComparer.Ordinal))
        {
            switch (rule)
            {
                case NotificationRecipientRuleCodes.TripOwner:
                    if (context.TripOwnerEmploymentId is long ownerEmploymentId)
                        await AddEmploymentAsync(result, rule, ownerEmploymentId, asOf, ct);
                    break;

                case NotificationRecipientRuleCodes.AffectedEmployment:
                    if (context.AffectedEmploymentId is long affectedEmploymentId)
                        await AddEmploymentAsync(result, rule, affectedEmploymentId, asOf, ct);
                    break;

                case NotificationRecipientRuleCodes.Initiator:
                    if (context.InitiatorUserId is int initiatorUserId)
                        await AddUserAsync(result, rule, initiatorUserId, asOf, ct);
                    break;

                case NotificationRecipientRuleCodes.TeamLeader:
                    if (context.TeamId is int teamId)
                    {
                        var employmentIds = await EffectiveLeaderAssignments(teamId, asOf)
                            .Select(x => x.EmploymentId)
                            .Distinct()
                            .ToListAsync(ct);
                        foreach (var employmentId in employmentIds)
                            await AddEmploymentAsync(result, rule, employmentId, asOf, ct);
                    }
                    break;

                case NotificationRecipientRuleCodes.DelegatedLeader:
                    if (context.TeamId is int delegatedTeamId)
                    {
                        var assignmentIds = EffectiveLeaderAssignments(delegatedTeamId, asOf).Select(x => x.TeamLeaderAssignmentId);
                        var delegateEmploymentIds = await db.TeamLeaderDelegations.AsNoTracking()
                            .Where(x => assignmentIds.Contains(x.TeamLeaderAssignmentId)
                                        && x.EffectiveFrom <= asOf
                                        && x.EffectiveTo >= asOf)
                            .Select(x => x.DelegateEmploymentId)
                            .Distinct()
                            .ToListAsync(ct);
                        foreach (var employmentId in delegateEmploymentIds)
                            await AddEmploymentAsync(result, rule, employmentId, asOf, ct);
                    }
                    break;

                case NotificationRecipientRuleCodes.Administrator:
                    if (context.OrganizationId is int organizationId)
                    {
                        var adminEmploymentIds = await (
                            from assignment in db.EmploymentRoleAssignments.AsNoTracking()
                            join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.RoleId
                            join employment in db.Employments.AsNoTracking() on assignment.EmploymentId equals employment.EmploymentId
                            where role.RoleCode == "admin"
                                  && role.IsActive
                                  && employment.OrganizationId == organizationId
                                  && assignment.EffectiveFrom <= asOf
                                  && (assignment.EffectiveTo == null || assignment.EffectiveTo >= asOf)
                                  && (employment.HireDate == null || employment.HireDate <= asOf)
                                  && (employment.TerminationDate == null || employment.TerminationDate >= asOf)
                            select employment.EmploymentId)
                            .Distinct()
                            .ToListAsync(ct);
                        foreach (var employmentId in adminEmploymentIds)
                            await AddEmploymentAsync(result, rule, employmentId, asOf, ct);
                    }
                    break;

                case NotificationRecipientRuleCodes.ProjectManager:
                    throw new InvalidOperationException("ProjectManager recipient authority is dormant in v1.8.0 and must not be activated by runtime code.");

                default:
                    throw new InvalidOperationException($"Unsupported notification recipient rule: {rule}");
            }
        }

        return result;
    }

    private IQueryable<TeamLeaderAssignment> EffectiveLeaderAssignments(int teamId, DateOnly asOf)
        => db.TeamLeaderAssignments.AsNoTracking()
            .Where(x => x.TeamId == teamId
                        && x.EffectiveFrom <= asOf
                        && (x.EffectiveTo == null || x.EffectiveTo >= asOf));

    private async Task AddEmploymentAsync(
        List<NotificationRecipient> result,
        string rule,
        long employmentId,
        DateOnly asOf,
        CancellationToken ct)
    {
        var employment = await db.Employments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.EmploymentId == employmentId, ct);
        if (employment is null
            || (employment.HireDate is DateOnly hire && hire > asOf)
            || (employment.TerminationDate is DateOnly termination && termination < asOf))
            return;

        string? email = NotificationBusinessKeyAuthority.NormalizeEmail(employment.Email);
        int? userId = employment.LegacyUserId;
        if (email is null && userId is int legacyUserId)
        {
            email = NotificationBusinessKeyAuthority.NormalizeEmail(
                await db.Users.AsNoTracking()
                    .Where(x => x.UserId == legacyUserId && x.IsActive)
                    .Select(x => x.Email)
                    .SingleOrDefaultAsync(ct));
        }

        result.Add(new NotificationRecipient(
            rule,
            NotificationBusinessKeyAuthority.ForEmployment(employment.EmploymentId),
            employment.EmploymentId,
            userId,
            email,
            employment.OptionalEmailNotificationEnabled));
    }

    private async Task AddUserAsync(
        List<NotificationRecipient> result,
        string rule,
        int userId,
        DateOnly asOf,
        CancellationToken ct)
    {
        var employmentId = await db.UserIdentityProfiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.EmploymentId)
            .SingleOrDefaultAsync(ct);

        // R2: Employment is canonical when either the identity bridge or legacy user link
        // proves that USER and EMP represent the same logical person.
        if (employmentId is not long)
        {
            employmentId = await db.Employments.AsNoTracking()
                .Where(x => x.LegacyUserId == userId
                            && (x.HireDate == null || x.HireDate <= asOf)
                            && (x.TerminationDate == null || x.TerminationDate >= asOf))
                .Select(x => (long?)x.EmploymentId)
                .SingleOrDefaultAsync(ct);
        }

        if (employmentId is long linkedEmploymentId)
        {
            await AddEmploymentAsync(result, rule, linkedEmploymentId, asOf, ct);
            return;
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.IsActive, ct);
        if (user is null) return;
        result.Add(new NotificationRecipient(
            rule,
            NotificationBusinessKeyAuthority.ForUser(user.UserId),
            null,
            user.UserId,
            NotificationBusinessKeyAuthority.NormalizeEmail(user.Email),
            true));
    }
}
