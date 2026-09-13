global using NotificationModelCustomizer = Ea2aRuntimeModelCustomizer;

using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

static class Ea2aSchemaBootstrap
{
    public static async Task InitializeAsync(string connectionString)
    {
        // EF creates only disposable prerequisite business tables, never the notification schema.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .ReplaceService<IModelCustomizer, Ea2aPrerequisiteModelCustomizer>()
            .Options;
        await using var db = new AppDbContext(options);
        if (!await db.Database.EnsureCreatedAsync())
            throw new InvalidOperationException("EA2A disposable database was not empty before prerequisite setup.");

        // Same prerequisite/version and snapshot-baseline setup used by the E-A0/E-A1 harnesses.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.SchemaVersions(VersionNumber nvarchar(50) NOT NULL PRIMARY KEY,
                Description nvarchar(500) NOT NULL,AppliedAt datetime2(3) NOT NULL,AppliedBy nvarchar(200) NULL);
            CREATE TABLE dbo.SchemaMigrationDataBaselines(MigrationVersion nvarchar(50) NOT NULL PRIMARY KEY,
                VisitTripSnapshotCount bigint NOT NULL);
            INSERT dbo.SchemaVersions VALUES(N'1.8.0-005',N'EA2A prerequisite',SYSUTCDATETIME(),N'EA2A');
            INSERT dbo.SchemaMigrationDataBaselines VALUES(N'1.8.0-001',0);
            """);

        var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "Schema");
        var sessionOptions = await File.ReadAllTextAsync(Path.Combine(schemaDirectory, "session-options.sql"));
        // Execute the unchanged authoritative scripts, including their own transaction and safety checks.
        await db.Database.ExecuteSqlRawAsync(sessionOptions + "\n" + await File.ReadAllTextAsync(Path.Combine(schemaDirectory, "Up.sql")));
        Console.WriteLine("EA2A_1800_006_APPLY=PASS");
        await db.Database.ExecuteSqlRawAsync(sessionOptions + "\n" + await File.ReadAllTextAsync(Path.Combine(schemaDirectory, "Verify.sql")));
        Console.WriteLine("EA2A_1800_006_VERIFY=PASS");
    }
}

static class Ea2aLegacyModelIsolation
{
    public static void Ignore1800007(ModelBuilder modelBuilder)
    {
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

sealed class Ea2aPrerequisiteModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        // Added by authoritative 1800_006, not by EF conventions during prerequisite creation.
        modelBuilder.Entity<Employment>().Ignore(x => x.OptionalEmailNotificationEnabled);
        // 1800_007 is applied separately by the F-A harness. Keep the older
        // E-A0/E-A1/E-A2 harnesses on their frozen 1800_006 prerequisite model.
        Ea2aLegacyModelIsolation.Ignore1800007(modelBuilder);
    }
}

sealed class Ea2aRuntimeModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        // Preserve the complete authoritative 1800_006 notification mapping exactly once,
        // then remove only the later 1800_007 model additions from these legacy E-A harnesses.
        new FieldVisit.Infrastructure.NotificationModelCustomizer(dependencies).Customize(modelBuilder, context);
        Ea2aLegacyModelIsolation.Ignore1800007(modelBuilder);
    }
}
