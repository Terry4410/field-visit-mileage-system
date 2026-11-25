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

    // SQL migration 005 and effective trigger 008 require uppercase storage.
    // Keep the application/route-attempt representation independent.
    [Theory]
    [InlineData("Car", "CAR")]
    [InlineData("cAr", "CAR")]
    [InlineData("Motorcycle", "MOTORCYCLE")]
    [InlineData("motorcycle", "MOTORCYCLE")]
    public void Mileage_rate_storage_and_query_use_schema_canonical_case(string input, string expected)
    {
        using var db = CreateSqlServerModelContext();
        var property = db.Model.FindEntityType(typeof(MileageRateRule))!
            .FindProperty(nameof(MileageRateRule.VehicleType))!;
        var converter = property.GetTypeMapping().Converter;
        Assert.NotNull(converter);
        Assert.Equal(expected, converter.ConvertToProvider(input));
        Assert.Equal(expected, converter.ConvertFromProvider(expected));
        var sql = db.MileageRateRules.Where(x => x.VehicleType == input).ToQueryString();
        Assert.Contains($"'{expected}'", sql);
    }

    [Theory]
    [InlineData(typeof(Location))]
    [InlineData(typeof(MileageCalculation))]
    [InlineData(typeof(MileageRateRule))]
    [InlineData(typeof(VisitTripSnapshot))]
    [InlineData(typeof(RouteCalculationAttempt))]
    [InlineData(typeof(TeamCenterAssignment))]
    [InlineData(typeof(EmploymentStatusPeriod))]
    [InlineData(typeof(TeamMembership))]
    [InlineData(typeof(DeploymentSite))]
    [InlineData(typeof(DeploymentSiteLocationAssignment))]
    [InlineData(typeof(TeamDeploymentSiteAssignment))]
    [InlineData(typeof(EmploymentDeploymentSiteAssignment))]
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
