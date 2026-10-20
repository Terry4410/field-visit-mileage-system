using System.Data;
using FieldVisit.Application;
using Microsoft.EntityFrameworkCore;
namespace FieldVisit.Infrastructure;

public sealed partial class V160FinalRepository
{
    private static bool IsVisitorOnlyActor(CurrentUserDto actor) => V180VisitorLocationRules.IsVisitor(actor) && !IsLeaderDraftActor(actor) && !HasRole(actor, "admin");

    private static bool IsLeaderDraftActor(CurrentUserDto actor) =>
        actor.Roles.Any(r => r.Equals("leader", StringComparison.OrdinalIgnoreCase))
        && !actor.Roles.Any(r => r.Equals("admin", StringComparison.OrdinalIgnoreCase));

    private async Task<IQueryable<FieldVisit.Domain.Entities.Location>> LeaderDraftReadScopeAsync(
        CurrentUserDto actor,
        IQueryable<FieldVisit.Domain.Entities.Location> baseQuery,
        CancellationToken ct)
    {
        var query = baseQuery;
        if (IsVisitorOnlyActor(actor))
        {
            var teams = await EnsureVisitorCurrentAsync(actor, ct);
            return query.Where(x => x.OrganizationId == actor.OrganizationId && (x.TeamId == null || teams.Contains(x.TeamId.Value)));
        }
        if (!IsLeaderDraftActor(actor)) return ApplyLocationScope(query, actor);
        var allowed = new List<int>();
        foreach (var teamId in actor.TeamIds)
        {
            try { await V180LeaderDraftLiveScope.RequireAsync(db, actor, teamId, ct); allowed.Add(teamId); }
            catch (UnauthorizedAccessException) { }
        }
        if (allowed.Count == 0) throw new UnauthorizedAccessException("LEADER_DRAFT_NO_CURRENT_SCOPE");
        return query.Where(x => x.OrganizationId == actor.OrganizationId && x.TeamId.HasValue && allowed.Contains(x.TeamId.Value));
    }


}
