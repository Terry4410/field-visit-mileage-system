using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180SqlServerTriggerCompatibilityTests
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

        return entityType.IsSqlOutputClauseUsed(storeObject);
    }

    [Theory]
    [InlineData(typeof(Location))]
    [InlineData(typeof(MileageCalculation))]
    [InlineData(typeof(MileageRateRule))]
    [InlineData(typeof(VisitTripSnapshot))]
    [InlineData(typeof(RouteCalculationAttempt))]
    public void Trigger_backed_tables_disable_sql_output(
        Type entityClrType)
    {
        using var db = CreateSqlServerModelContext();

        Assert.False(
            IsSqlOutputClauseUsed(
                db,
                entityClrType));
    }

    [Theory]
    [InlineData(typeof(Project))]
    [InlineData(typeof(GeocodingAttempt))]
    public void Non_trigger_controls_keep_default_sql_output(
        Type entityClrType)
    {
        using var db = CreateSqlServerModelContext();

        Assert.True(
            IsSqlOutputClauseUsed(
                db,
                entityClrType));
    }
}
