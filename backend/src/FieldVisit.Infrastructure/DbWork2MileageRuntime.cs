using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class DbWork2MileageRepositoryDecorator(MileageRepository inner) : IMileageRepository
{
    public Task<MileageCalculation?> GetByTripAsync(long tripId, bool tracking, CancellationToken ct) =>
        inner.GetByTripAsync(tripId, tracking, ct);

    public Task AddAsync(MileageCalculation row, CancellationToken ct) => inner.AddAsync(row, ct);

    public Task<MileageRateRule?> GetEffectiveRateAsync(
        int organizationId,
        string vehicleType,
        DateOnly date,
        CancellationToken ct) =>
        inner.GetEffectiveRateAsync(
            organizationId,
            DbWork2MileageRateRules.NormalizeVehicleType(vehicleType),
            date,
            ct);

    public Task<List<MileageRateRule>> GetRatesAsync(CurrentUserDto user, CancellationToken ct) =>
        inner.GetRatesAsync(user, ct);

    public Task<MileageRateRule?> GetRateAsync(int mileageRateRuleId, bool tracking, CancellationToken ct) =>
        inner.GetRateAsync(mileageRateRuleId, tracking, ct);

    public Task AddRateAsync(MileageRateRule rule, CancellationToken ct) => inner.AddRateAsync(rule, ct);

    public Task<List<MileageRateRule>> GetRateSeriesAsync(
        int? organizationId,
        string vehicleType,
        bool tracking,
        CancellationToken ct) =>
        inner.GetRateSeriesAsync(
            organizationId,
            DbWork2MileageRateRules.NormalizeVehicleType(vehicleType),
            tracking,
            ct);

    public Task<(int Count, DateOnly? FirstVisitDate, DateOnly? LastVisitDate)> GetApprovedRateImpactAsync(
        int? organizationId,
        string vehicleType,
        DateOnly effectiveFrom,
        CancellationToken ct) =>
        inner.GetApprovedRateImpactAsync(
            organizationId,
            DbWork2MileageRateRules.NormalizeVehicleType(vehicleType),
            effectiveFrom,
            ct);
}

public sealed class FinalizedMileageRateImpactReader(AppDbContext db) : IFinalizedMileageRateImpactReader
{
    public async Task<(int Count, DateOnly? FirstVisitDate, DateOnly? LastVisitDate)> GetImpactAsync(
        int? organizationId,
        string vehicleType,
        DateOnly effectiveFrom,
        CancellationToken ct)
    {
        var canonicalVehicle = DbWork2MileageRateRules.NormalizeVehicleType(vehicleType);

        var q = db.VisitTripSnapshots
            .AsNoTracking()
            .Where(snapshot =>
                (snapshot.SnapshotType == "Approved" || snapshot.SnapshotType == "Correction")
                && snapshot.RatePerKmSnapshot != null
                && snapshot.VisitDate >= effectiveFrom
                && !db.VisitTripSnapshots.Any(newer =>
                    newer.VisitTripId == snapshot.VisitTripId
                    && (newer.SnapshotType == "Approved" || newer.SnapshotType == "Correction")
                    && newer.SnapshotVersion > snapshot.SnapshotVersion));

        if (organizationId.HasValue)
            q = q.Where(snapshot => snapshot.OrganizationId == organizationId.Value);

        q = canonicalVehicle == "MOTORCYCLE"
            ? q.Where(snapshot =>
                snapshot.VehicleTypeSnapshot == null
                || snapshot.VehicleTypeSnapshot.ToUpper() == "MOTORCYCLE")
            : q.Where(snapshot =>
                snapshot.VehicleTypeSnapshot != null
                && snapshot.VehicleTypeSnapshot.ToUpper() == "CAR");

        var dates = await q.Select(snapshot => snapshot.VisitDate).ToListAsync(ct);
        if (dates.Count == 0) return (0, null, null);
        return (dates.Count, dates.Min(), dates.Max());
    }
}

public sealed class EfTransactionBoundary(AppDbContext db) : ITransactionBoundary
{
    public Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await action(ct);
                await tx.CommitAsync(ct);
                return result;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });
    }
}
