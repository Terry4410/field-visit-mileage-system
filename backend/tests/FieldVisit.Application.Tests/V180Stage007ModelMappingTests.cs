using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180Stage007ModelMappingTests
{
    private static AppDbContext CreateSqlServerModelContext()
    {
        var options =
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(
                    "Server=localhost;Database=FieldVisitModelOnly;User Id=model;Password=model;TrustServerCertificate=True")
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

    private static IForeignKey ForeignKey(
        IEntityType entity,
        string dependentProperty) =>
        entity.GetForeignKeys().Single(
            fk => fk.Properties.Any(
                property => property.Name == dependentProperty));

    private static IIndex Index(
        IEntityType entity,
        string databaseName) =>
        entity.GetIndexes().Single(
            index => index.GetDatabaseName() == databaseName);

    [Fact]
    public void Stage007_entities_are_mapped_to_expected_tables()
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
    }

    [Fact]
    public void Location_selected_geocoding_attempt_has_expected_index_and_fk()
    {
        using var db = CreateSqlServerModelContext();
        var entity = Entity<Location>(db);

        Assert.NotNull(
            Property(
                entity,
                nameof(Location.SelectedGeocodingAttemptId)));

        var index =
            Index(
                entity,
                "IX_Locations_SelectedGeocodingAttempt");

        Assert.Equal(
            new[] { nameof(Location.SelectedGeocodingAttemptId) },
            index.Properties.Select(x => x.Name));
        Assert.Equal(
            "[SelectedGeocodingAttemptId] IS NOT NULL",
            index.GetFilter());

        var fk =
            ForeignKey(
                entity,
                nameof(Location.SelectedGeocodingAttemptId));

        Assert.Equal(
            typeof(GeocodingAttempt),
            fk.PrincipalEntityType.ClrType);
        Assert.Equal(
            DeleteBehavior.NoAction,
            fk.DeleteBehavior);
    }

    [Fact]
    public void Mileage_calculation_stage007_fields_match_schema()
    {
        using var db = CreateSqlServerModelContext();
        var entity = Entity<MileageCalculation>(db);

        foreach (var name in new[]
        {
            nameof(MileageCalculation.SelectedRouteCalculationAttemptId),
            nameof(MileageCalculation.ManualFallbackUsed),
            nameof(MileageCalculation.DistanceDecisionGovernanceVersion),
            nameof(MileageCalculation.ApprovedDistanceSource),
            nameof(MileageCalculation.ApprovalBasisCode),
            nameof(MileageCalculation.ApprovalBasisHash),
            nameof(MileageCalculation.DistanceApprovedAt),
            nameof(MileageCalculation.DistanceApprovedByUserId),
            nameof(MileageCalculation.InvalidatedAt),
            nameof(MileageCalculation.InvalidatedByUserId),
            nameof(MileageCalculation.InvalidationReason)
        })
        {
            Assert.NotNull(
                Property(
                    entity,
                    name));
        }

        Assert.Equal(
            "varbinary(32)",
            Property(
                entity,
                nameof(MileageCalculation.ApprovalBasisHash))
                .GetColumnType());
        Assert.Equal(
            20,
            Property(
                entity,
                nameof(MileageCalculation.DistanceDecisionGovernanceVersion))
                .GetMaxLength());
        Assert.Equal(
            30,
            Property(
                entity,
                nameof(MileageCalculation.ApprovedDistanceSource))
                .GetMaxLength());
        Assert.Equal(
            80,
            Property(
                entity,
                nameof(MileageCalculation.ApprovalBasisCode))
                .GetMaxLength());
        Assert.Equal(
            100,
            Property(
                entity,
                nameof(MileageCalculation.InvalidationReason))
                .GetMaxLength());
        Assert.Equal(
            3,
            Property(
                entity,
                nameof(MileageCalculation.DistanceApprovedAt))
                .GetPrecision());
        Assert.Equal(
            3,
            Property(
                entity,
                nameof(MileageCalculation.InvalidatedAt))
                .GetPrecision());

        AssertForeignKey(
            entity,
            nameof(MileageCalculation.SelectedRouteCalculationAttemptId),
            typeof(RouteCalculationAttempt));
        AssertForeignKey(
            entity,
            nameof(MileageCalculation.DistanceApprovedByUserId),
            typeof(User));
        AssertForeignKey(
            entity,
            nameof(MileageCalculation.InvalidatedByUserId),
            typeof(User));
    }

    [Fact]
    public void Snapshot_stage007_fields_and_route_fk_match_schema()
    {
        using var db = CreateSqlServerModelContext();
        var entity = Entity<VisitTripSnapshot>(db);

        foreach (var name in new[]
        {
            nameof(VisitTripSnapshot.MileageRouteAttemptIdSnapshot),
            nameof(VisitTripSnapshot.RouteTravelModeSnapshot),
            nameof(VisitTripSnapshot.RouteCalculatedAtSnapshot),
            nameof(VisitTripSnapshot.RouteCalculationStatusSnapshot),
            nameof(VisitTripSnapshot.RouteErrorCodeSnapshot),
            nameof(VisitTripSnapshot.RouteCorrelationIdSnapshot),
            nameof(VisitTripSnapshot.ApprovedDistanceSourceSnapshot),
            nameof(VisitTripSnapshot.ApprovalBasisCodeSnapshot),
            nameof(VisitTripSnapshot.ApprovalBasisHashSnapshot),
            nameof(VisitTripSnapshot.DistanceApprovedAtSnapshot)
        })
        {
            Assert.NotNull(
                Property(
                    entity,
                    name));
        }

        Assert.Equal(
            20,
            Property(
                entity,
                nameof(VisitTripSnapshot.RouteTravelModeSnapshot))
                .GetMaxLength());
        Assert.Equal(
            20,
            Property(
                entity,
                nameof(VisitTripSnapshot.RouteCalculationStatusSnapshot))
                .GetMaxLength());
        Assert.Equal(
            100,
            Property(
                entity,
                nameof(VisitTripSnapshot.RouteErrorCodeSnapshot))
                .GetMaxLength());
        Assert.Equal(
            30,
            Property(
                entity,
                nameof(VisitTripSnapshot.ApprovedDistanceSourceSnapshot))
                .GetMaxLength());
        Assert.Equal(
            80,
            Property(
                entity,
                nameof(VisitTripSnapshot.ApprovalBasisCodeSnapshot))
                .GetMaxLength());
        Assert.Equal(
            "varbinary(32)",
            Property(
                entity,
                nameof(VisitTripSnapshot.ApprovalBasisHashSnapshot))
                .GetColumnType());
        Assert.Equal(
            3,
            Property(
                entity,
                nameof(VisitTripSnapshot.RouteCalculatedAtSnapshot))
                .GetPrecision());
        Assert.Equal(
            3,
            Property(
                entity,
                nameof(VisitTripSnapshot.DistanceApprovedAtSnapshot))
                .GetPrecision());

        AssertForeignKey(
            entity,
            nameof(VisitTripSnapshot.MileageRouteAttemptIdSnapshot),
            typeof(RouteCalculationAttempt));
    }

    [Fact]
    public void Attempt_and_governance_relationships_and_indexes_match_schema()
    {
        using var db = CreateSqlServerModelContext();

        var geocoding =
            Entity<GeocodingAttempt>(db);
        Assert.Equal(
            "varbinary(32)",
            Property(
                geocoding,
                nameof(GeocodingAttempt.AddressBasisHash))
                .GetColumnType());
        Assert.Equal(
            80,
            Property(
                geocoding,
                nameof(GeocodingAttempt.Provider))
                .GetMaxLength());
        Assert.Equal(
            3,
            Property(
                geocoding,
                nameof(GeocodingAttempt.RequestedAt))
                .GetPrecision());
        Assert.True(
            Index(
                geocoding,
                "UQ_GeocodingAttempts_Correlation")
                .IsUnique);
        Assert.Equal(
            new[]
            {
                nameof(GeocodingAttempt.LocationId),
                nameof(GeocodingAttempt.RequestedAt)
            },
            Index(
                geocoding,
                "IX_GeocodingAttempts_Location_Requested")
                .Properties
                .Select(x => x.Name));
        AssertForeignKey(
            geocoding,
            nameof(GeocodingAttempt.LocationId),
            typeof(Location));
        AssertForeignKey(
            geocoding,
            nameof(GeocodingAttempt.RequestedByUserId),
            typeof(User));

        var route =
            Entity<RouteCalculationAttempt>(db);
        Assert.Equal(
            "varbinary(32)",
            Property(
                route,
                nameof(RouteCalculationAttempt.RequestBasisHash))
                .GetColumnType());
        Assert.Equal(
            30,
            Property(
                route,
                nameof(RouteCalculationAttempt.BasisType))
                .GetMaxLength());
        Assert.Equal(
            30,
            Property(
                route,
                nameof(RouteCalculationAttempt.CalculationReason))
                .GetMaxLength());
        Assert.Equal(
            20,
            Property(
                route,
                nameof(RouteCalculationAttempt.RequestedVehicleType))
                .GetMaxLength());
        Assert.Equal(
            20,
            Property(
                route,
                nameof(RouteCalculationAttempt.TravelMode))
                .GetMaxLength());
        Assert.Equal(
            80,
            Property(
                route,
                nameof(RouteCalculationAttempt.Provider))
                .GetMaxLength());
        Assert.True(
            Index(
                route,
                "UQ_RouteCalculationAttempts_Correlation")
                .IsUnique);
        Assert.Equal(
            new[]
            {
                nameof(RouteCalculationAttempt.VisitTripId),
                nameof(RouteCalculationAttempt.RequestedAt)
            },
            Index(
                route,
                "IX_RouteCalculationAttempts_Trip_Requested")
                .Properties
                .Select(x => x.Name));
        Assert.Equal(
            new[]
            {
                nameof(RouteCalculationAttempt.BasisVisitTripSnapshotId)
            },
            Index(
                route,
                "IX_RouteCalculationAttempts_BasisSnapshot")
                .Properties
                .Select(x => x.Name));
        AssertForeignKey(
            route,
            nameof(RouteCalculationAttempt.VisitTripId),
            typeof(VisitTrip));
        AssertForeignKey(
            route,
            nameof(RouteCalculationAttempt.BasisVisitTripSnapshotId),
            typeof(VisitTripSnapshot));
        AssertForeignKey(
            route,
            nameof(RouteCalculationAttempt.RequestedByUserId),
            typeof(User));

        var governance =
            Entity<MileageGovernanceEvent>(db);
        Assert.Equal(
            new[]
            {
                nameof(MileageGovernanceEvent.VisitTripId),
                nameof(MileageGovernanceEvent.OccurredAt)
            },
            Index(
                governance,
                "IX_MileageGovernanceEvents_Trip_Occurred")
                .Properties
                .Select(x => x.Name));
        Assert.Equal(
            new[]
            {
                nameof(MileageGovernanceEvent.CorrelationId)
            },
            Index(
                governance,
                "IX_MileageGovernanceEvents_Correlation")
                .Properties
                .Select(x => x.Name));
        AssertForeignKey(
            governance,
            nameof(MileageGovernanceEvent.VisitTripId),
            typeof(VisitTrip));
        AssertForeignKey(
            governance,
            nameof(MileageGovernanceEvent.VisitTripSnapshotId),
            typeof(VisitTripSnapshot));
        AssertForeignKey(
            governance,
            nameof(MileageGovernanceEvent.RouteCalculationAttemptId),
            typeof(RouteCalculationAttempt));
        AssertForeignKey(
            governance,
            nameof(MileageGovernanceEvent.ActorUserId),
            typeof(User));
    }

    private static void AssertForeignKey(
        IEntityType entity,
        string dependentProperty,
        Type principalClrType)
    {
        var fk =
            ForeignKey(
                entity,
                dependentProperty);

        Assert.Equal(
            principalClrType,
            fk.PrincipalEntityType.ClrType);
        Assert.Equal(
            DeleteBehavior.NoAction,
            fk.DeleteBehavior);
    }
}
