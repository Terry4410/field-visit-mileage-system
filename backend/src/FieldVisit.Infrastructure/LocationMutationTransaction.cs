using FieldVisit.Application;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class EfLocationMutationBoundary(AppDbContext db) : ILocationMutationBoundary
{
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
        LocationMutationTransaction.ExecuteAsync(db, () => action(ct), ct);
}

/// <summary>Narrow Location item recovery: rollback also removes first-flush EF state
/// before the existing per-item failure path or SQL execution strategy continues.</summary>
internal static class LocationMutationTransaction
{
    public static Task<T> ExecuteAsync<T>(AppDbContext db, Func<Task<T>> mutation, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.DetectChanges();
            var before = db.ChangeTracker.Entries().Select(entry =>
                (Entry: entry, Current: entry.CurrentValues.Clone(), Original: entry.OriginalValues.Clone(), State: entry.State,
                    Temporary: entry.Properties.Where(x => x.IsTemporary).Select(x => x.Metadata.Name).ToList())).ToList();
            var existing = before.Select(x => x.Entry.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await mutation();
                await tx.CommitAsync(ct);
                return result;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                foreach (var entry in db.ChangeTracker.Entries().ToList())
                    if (!existing.Contains(entry.Entity)) entry.State = EntityState.Detached;
                foreach (var saved in before)
                {
                    if (saved.State == EntityState.Added) saved.Entry.State = EntityState.Detached;
                    saved.Entry.CurrentValues.SetValues(saved.Current);
                    saved.Entry.OriginalValues.SetValues(saved.Original);
                    saved.Entry.State = saved.State;
                    foreach (var property in saved.Temporary) saved.Entry.Property(property).IsTemporary = true;
                }
                throw;
            }
        });
}
