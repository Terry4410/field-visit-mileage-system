using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180TripContextModelMappingTests
{
    private static AppDbContext CreateSqlServerModelContext()
    {
        var options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(
                    "Server=localhost;Database=FieldVisitModelOnly;Integrated Security=True;TrustServerCertificate=True")
                .Options;

        return new AppDbContext(options);
    }

    private static IEntityType Entity<TEntity>(
        AppDbContext db)
        where TEntity : class =>
        db.Model.FindEntityType(typeof(TEntity))
        ?? throw new InvalidOperationException(
            $"EF entity {typeof(TEntity).Name} was not found.");

    private static IProperty Property(
        IEntityType entity,
        string name) =>
        entity.FindProperty(name)
        ?? throw new InvalidOperationException(
            $"Property {entity.ClrType.Name}.{name} was not found.");

    private static bool IsSqlOutputClauseUsed(
        AppDbContext db,
        Type entityClrType)
    {
        var entityType =
            db.Model.FindEntityType(entityClrType)
            ?? throw new InvalidOperationException(
                $"EF entity type {entityClrType.Name} was not found.");

        var tableName =
            entityType.GetTableName()
            ?? throw new InvalidOperationException(
                $"EF entity type {entityClrType.Name} has no table mapping.");

        var storeObject =
            StoreObjectIdentifier.Table(
                tableName,
                entityType.GetSchema());

        return entityType.IsSqlOutputClauseUsed(
            storeObject);
    }

    [Fact]
    public void VisitTrip_maps_only_required_P2A0_scalar_context_columns()
    {
        using var db = CreateSqlServerModelContext();
        var entity = Entity<VisitTrip>(db);

        Assert.NotNull(Property(entity, nameof(VisitTrip.EmploymentId)));
        Assert.NotNull(Property(entity, nameof(VisitTrip.StartDeploymentSiteId)));
        Assert.NotNull(Property(entity, nameof(VisitTrip.EndDeploymentSiteId)));

        Assert.DoesNotContain(
            entity.GetForeignKeys(),
            fk => fk.Properties.Any(
                p => p.Name == nameof(VisitTrip.EmploymentId)));
        Assert.DoesNotContain(
            entity.GetForeignKeys(),
            fk => fk.Properties.Any(
                p => p.Name == nameof(VisitTrip.StartDeploymentSiteId)));
        Assert.DoesNotContain(
            entity.GetForeignKeys(),
            fk => fk.Properties.Any(
                p => p.Name == nameof(VisitTrip.EndDeploymentSiteId)));
    }

    [Fact]
    public void VisitTripSnapshot_maps_minimum_canonical_route_basis_fields()
    {
        using var db = CreateSqlServerModelContext();
        var entity = Entity<VisitTripSnapshot>(db);

        Assert.Equal(
            50,
            Property(
                entity,
                nameof(VisitTripSnapshot.StartDeploymentSiteCodeSnapshot))
                .GetMaxLength());
        Assert.Equal(
            500,
            Property(
                entity,
                nameof(VisitTripSnapshot.StartDeploymentAddressSnapshot))
                .GetMaxLength());
        Assert.Equal(
            50,
            Property(
                entity,
                nameof(VisitTripSnapshot.EndDeploymentSiteCodeSnapshot))
                .GetMaxLength());
        Assert.Equal(
            500,
            Property(
                entity,
                nameof(VisitTripSnapshot.EndDeploymentAddressSnapshot))
                .GetMaxLength());
    }

    [Fact]
    public void Package1_Stage007_model_mappings_remain_present()
    {
        using var db = CreateSqlServerModelContext();

        Assert.Equal(
            "GeocodingAttempts",
            Entity<GeocodingAttempt>(db).GetTableName());
        Assert.Equal(
            "RouteCalculationAttempts",
            Entity<RouteCalculationAttempt>(db).GetTableName());
        Assert.Equal(
            "MileageGovernanceEvents",
            Entity<MileageGovernanceEvent>(db).GetTableName());

        Assert.False(IsSqlOutputClauseUsed(db, typeof(Location)));
        Assert.False(IsSqlOutputClauseUsed(db, typeof(MileageCalculation)));
        Assert.False(IsSqlOutputClauseUsed(db, typeof(MileageRateRule)));
        Assert.False(IsSqlOutputClauseUsed(db, typeof(VisitTripSnapshot)));
        Assert.False(IsSqlOutputClauseUsed(db, typeof(RouteCalculationAttempt)));
        Assert.True(IsSqlOutputClauseUsed(db, typeof(GeocodingAttempt)));
    }

    [Fact]
    public async Task GetLatestAsync_returns_latest_exact_snapshot_type_and_includes_stops()
    {
        var options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(
                    $"p2a0-snapshot-{Guid.NewGuid()}")
                .Options;

        await using var db =
            new AppDbContext(options);

        db.VisitTripSnapshots.AddRange(
            Snapshot(1, 42, 1, "Approved", "Approved v1"),
            Snapshot(2, 42, 2, "Approved", "Approved v2"),
            Snapshot(3, 42, 9, "Submitted", "Submitted v9"),
            Snapshot(4, 99, 8, "Approved", "Other trip"));

        await db.SaveChangesAsync();

        var repository =
            new TripSnapshotRepository(db);

        var result =
            await repository.GetLatestAsync(
                42,
                "Approved",
                CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.SnapshotVersion);
        Assert.Equal("Approved", result.SnapshotType);
        Assert.Single(result.Stops);
        Assert.Equal(
            "Approved v2",
            result.Stops[0].LocationNameSnapshot);
    }

    private static VisitTripSnapshot Snapshot(
        long id,
        long tripId,
        int version,
        string type,
        string stopName) =>
        new()
        {
            VisitTripSnapshotId = id,
            VisitTripId = tripId,
            SnapshotVersion = version,
            SnapshotType = type,
            TripNo = $"TRIP-{tripId}",
            UserId = 1,
            EmployeeNoSnapshot = "E001",
            DisplayNameSnapshot = "Tester",
            OrganizationId = 1,
            OrganizationNameSnapshot = "Org",
            VisitDate = new DateOnly(2026, 9, 29),
            StatusSnapshot = type,
            CreatedAt = DateTime.UtcNow,
            Stops =
            [
                new VisitTripSnapshotStop
                {
                    VisitTripSnapshotStopId = id * 10,
                    StopSequence = 1,
                    LocationNameSnapshot = stopName,
                    CreatedAt = DateTime.UtcNow
                }
            ]
        };
}
