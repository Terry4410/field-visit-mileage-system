using System.Data;
using FieldVisit.Application;
using Microsoft.EntityFrameworkCore;
namespace FieldVisit.Infrastructure;

public sealed partial class V160FinalRepository
{
    private static bool IsLeaderDraftActor(CurrentUserDto actor) =>
        actor.Roles.Any(r => r.Equals("leader", StringComparison.OrdinalIgnoreCase))
        && !actor.Roles.Any(r => r.Equals("admin", StringComparison.OrdinalIgnoreCase));

    private async Task<IQueryable<FieldVisit.Domain.Entities.Location>> LeaderDraftReadScopeAsync(CurrentUserDto actor, CancellationToken ct)
    {
        var query = db.Locations.AsNoTracking().AsQueryable();
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

    public Task<ManagedLocationDto> CreateManagedLocationAsync(CurrentUserDto actor, SaveManagedLocationRequest request, CancellationToken ct) =>
        LeaderDraftTransactionAsync(actor, () => CreateLeaderDraftCoreAsync(actor, request, ct), ct);
    public Task<ManagedLocationDto> UpdateManagedLocationAsync(CurrentUserDto actor, int locationId, SaveManagedLocationRequest request, CancellationToken ct) =>
        LeaderDraftTransactionAsync(actor, () => UpdateLeaderDraftCoreAsync(actor, locationId, request, ct), ct);

    private async Task<ManagedLocationDto> LeaderDraftTransactionAsync(CurrentUserDto actor, Func<Task<ManagedLocationDto>> operation, CancellationToken ct)
    {
        if (!IsLeaderDraftActor(actor)) return await operation();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            ManagedLocationDto result;
            try { result = await operation(); }
            catch (Microsoft.Data.SqlClient.SqlException) { throw new InvalidOperationException("LEADER_DRAFT_SQL_FAILURE_REFRESH_REQUIRED"); }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException) { throw new InvalidOperationException("LEADER_DRAFT_WRITE_FAILURE_REFRESH_REQUIRED"); }
            try { await tx.CommitAsync(ct); }
            catch { throw new InvalidOperationException("LEADER_DRAFT_COMMIT_UNKNOWN_REFRESH_DO_NOT_RESUBMIT"); }
            return result;
        });
    }
}
