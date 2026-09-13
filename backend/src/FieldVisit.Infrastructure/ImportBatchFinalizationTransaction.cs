using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Narrow import terminal-state transaction. Per-item commits intentionally happen before this boundary.
/// Rollback also restores the shared DbContext so finalization failures cannot leave tracked outbox/audit state.
/// </summary>
internal static class ImportBatchFinalizationTransaction
{
    public static Task<T> ExecuteAsync<T>(AppDbContext db, Func<Task<T>> finalization, CancellationToken ct) =>
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
                var result = await finalization();
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
