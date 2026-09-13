using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

static class FaSchemaBootstrap
{
    public static async Task InitializeAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .ReplaceService<IModelCustomizer, FaPrerequisiteModelCustomizer>()
            .Options;
        await using var db = new AppDbContext(options);
        if (!await db.Database.EnsureCreatedAsync())
            throw new InvalidOperationException("FA disposable database was not empty before prerequisite setup.");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(50) NOT NULL PRIMARY KEY,
                Description nvarchar(500) NOT NULL,AppliedAt datetime2(3) NOT NULL,AppliedBy nvarchar(200) NULL);
            CREATE TABLE dbo.SchemaMigrationDataBaselines(MigrationVersion nvarchar(50) NOT NULL PRIMARY KEY,
                VisitTripSnapshotCount bigint NOT NULL);
            INSERT dbo.SchemaVersions VALUES(N'1.8.0-005',N'F-A prerequisite',SYSUTCDATETIME(),N'F-A');
            INSERT dbo.SchemaMigrationDataBaselines VALUES(N'1.8.0-001',0);
            """);
        var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "Schema");
        var session = await File.ReadAllTextAsync(Path.Combine(schemaDirectory, "session-options.sql"));
        await db.Database.ExecuteSqlRawAsync(session + "\n" + await File.ReadAllTextAsync(Path.Combine(schemaDirectory, "1800_006.Up.sql")));
        Console.WriteLine("FA_1800_006_APPLY=PASS");
        await db.Database.ExecuteSqlRawAsync(session + "\n" + await File.ReadAllTextAsync(Path.Combine(schemaDirectory, "1800_006.Verify.sql")));
        Console.WriteLine("FA_1800_006_VERIFY=PASS");
        await db.Database.ExecuteSqlRawAsync(session + "\n" + await File.ReadAllTextAsync(Path.Combine(schemaDirectory, "1800_007.Up.sql")));
        Console.WriteLine("FA_1800_007_APPLY=PASS");
        await db.Database.ExecuteSqlRawAsync(session + "\n" + await File.ReadAllTextAsync(Path.Combine(schemaDirectory, "1800_007.Verify.sql")));
        Console.WriteLine("FA_1800_007_VERIFY=PASS");
    }
}

sealed class FaPrerequisiteModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Ignore<GeocodingAttempt>();
        modelBuilder.Ignore<RouteCalculationAttempt>();
        modelBuilder.Ignore<MileageGovernanceEvent>();
        modelBuilder.Entity<Location>().Ignore(x => x.SelectedGeocodingAttemptId);
        modelBuilder.Entity<MileageCalculation>().Ignore(x => x.SelectedRouteCalculationAttemptId)
            .Ignore(x => x.ManualFallbackUsed).Ignore(x => x.DistanceDecisionGovernanceVersion)
            .Ignore(x => x.ApprovedDistanceSource).Ignore(x => x.ApprovalBasisCode)
            .Ignore(x => x.ApprovalBasisHash).Ignore(x => x.DistanceApprovedAt)
            .Ignore(x => x.DistanceApprovedByUserId).Ignore(x => x.InvalidatedAt)
            .Ignore(x => x.InvalidatedByUserId).Ignore(x => x.InvalidationReason);
        modelBuilder.Entity<VisitTripSnapshot>().Ignore(x => x.MileageRouteAttemptIdSnapshot)
            .Ignore(x => x.RouteTravelModeSnapshot).Ignore(x => x.RouteCalculatedAtSnapshot)
            .Ignore(x => x.RouteCalculationStatusSnapshot).Ignore(x => x.RouteErrorCodeSnapshot)
            .Ignore(x => x.RouteCorrelationIdSnapshot).Ignore(x => x.ApprovedDistanceSourceSnapshot)
            .Ignore(x => x.ApprovalBasisCodeSnapshot).Ignore(x => x.ApprovalBasisHashSnapshot)
            .Ignore(x => x.DistanceApprovedAtSnapshot);
    }
}
