using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180DbWork2MileageRepositoryTests
{
    [Fact]
    public async Task Resolver_NormalizesLegacyMotorcycle_AndPrefersOrganizationOverGlobal()
    {
        await using var db = CreateDb();
        db.MileageRateRules.AddRange(
            Rate(null, "MOTORCYCLE", 2.00m, new DateOnly(2026, 1, 1)),
            Rate(1, "MOTORCYCLE", 3.00m, new DateOnly(2026, 1, 1)));
        await db.SaveChangesAsync();

        var sut = new DbWork2MileageRepositoryDecorator(new MileageRepository(db));
        var resolved = await sut.GetEffectiveRateAsync(
            1, "Motorcycle", new DateOnly(2026, 9, 1), CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(1, resolved!.OrganizationId);
        Assert.Equal(3.00m, resolved.RatePerKm);
        Assert.Equal("MOTORCYCLE", resolved.VehicleType);
    }

    [Fact]
    public async Task Resolver_FallsBackToGlobal_WhenOrganizationRateDoesNotExist()
    {
        await using var db = CreateDb();
        db.MileageRateRules.Add(Rate(null, "CAR", 4.00m, new DateOnly(2026, 1, 1)));
        await db.SaveChangesAsync();

        var sut = new DbWork2MileageRepositoryDecorator(new MileageRepository(db));
        var resolved = await sut.GetEffectiveRateAsync(
            1, "Car", new DateOnly(2026, 9, 1), CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Null(resolved!.OrganizationId);
        Assert.Equal(4.00m, resolved.RatePerKm);
        Assert.Equal("CAR", resolved.VehicleType);
    }

    [Fact]
    public async Task Resolver_ReturnsNull_WhenNoApplicableRateExists()
    {
        await using var db = CreateDb();
        db.MileageRateRules.Add(Rate(null, "MOTORCYCLE", 2.00m, new DateOnly(2027, 1, 1)));
        await db.SaveChangesAsync();

        var sut = new DbWork2MileageRepositoryDecorator(new MileageRepository(db));
        var resolved = await sut.GetEffectiveRateAsync(
            1, "Motorcycle", new DateOnly(2026, 9, 1), CancellationToken.None);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task HistoricalImpact_UsesLatestFinalizedSnapshot_NotLiveTripOrSubmittedSnapshot()
    {
        await using var db = CreateDb();
        db.VisitTrips.Add(new VisitTrip
        {
            VisitTripId = 10,
            TripNo = "T-10",
            UserId = 1,
            OrganizationId = 1,
            VisitDate = new DateOnly(2030, 1, 1),
            VehicleType = "Car",
            Status = "Approved",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = 1
        });
        db.VisitTripSnapshots.AddRange(
            Snapshot(10, 1, "Approved", new DateOnly(2026, 1, 15), "Motorcycle", 2.50m),
            Snapshot(10, 2, "Correction", new DateOnly(2026, 3, 20), "Motorcycle", 2.50m),
            Snapshot(10, 3, "Submitted", new DateOnly(2026, 8, 1), "Car", null));
        await db.SaveChangesAsync();

        var sut = new FinalizedMileageRateImpactReader(db);
        var impact = await sut.GetImpactAsync(
            1, "MOTORCYCLE", new DateOnly(2026, 2, 1), CancellationToken.None);

        Assert.Equal(1, impact.Count);
        Assert.Equal(new DateOnly(2026, 3, 20), impact.FirstVisitDate);
        Assert.Equal(new DateOnly(2026, 3, 20), impact.LastVisitDate);
    }

    [Fact]
    public async Task HistoricalImpact_DoesNotCountOlderFinalizedSnapshot_WhenLatestCorrectionFallsBeforeBoundary()
    {
        await using var db = CreateDb();
        db.VisitTripSnapshots.AddRange(
            Snapshot(20, 1, "Approved", new DateOnly(2026, 7, 1), "MOTORCYCLE", 2.50m),
            Snapshot(20, 2, "Correction", new DateOnly(2026, 1, 1), "MOTORCYCLE", 2.50m));
        await db.SaveChangesAsync();

        var sut = new FinalizedMileageRateImpactReader(db);
        var impact = await sut.GetImpactAsync(
            1, "Motorcycle", new DateOnly(2026, 6, 1), CancellationToken.None);

        Assert.Equal(0, impact.Count);
        Assert.Null(impact.FirstVisitDate);
        Assert.Null(impact.LastVisitDate);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"dbwork2-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static MileageRateRule Rate(
        int? organizationId,
        string vehicleType,
        decimal rate,
        DateOnly effectiveFrom) => new()
        {
            OrganizationId = organizationId,
            RuleName = $"{vehicleType}-{rate}",
            VehicleType = vehicleType,
            RatePerKm = rate,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = null,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

    private static VisitTripSnapshot Snapshot(
        long tripId,
        int version,
        string type,
        DateOnly visitDate,
        string? vehicleType,
        decimal? rate) => new()
        {
            VisitTripId = tripId,
            SnapshotVersion = version,
            SnapshotType = type,
            TripNo = $"T-{tripId}",
            UserId = 1,
            EmployeeNoSnapshot = "A001",
            DisplayNameSnapshot = "User",
            OrganizationId = 1,
            OrganizationNameSnapshot = "Org",
            VisitDate = visitDate,
            StatusSnapshot = type == "Submitted" ? "Submitted" : "Approved",
            VehicleTypeSnapshot = vehicleType,
            RatePerKmSnapshot = rate,
            CreatedAt = DateTime.UtcNow
        };
}
