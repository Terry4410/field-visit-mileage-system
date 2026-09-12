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

sealed class Ea2aPrerequisiteModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        // Added by authoritative 1800_006, not by EF conventions during prerequisite creation.
        modelBuilder.Entity<Employment>().Ignore(x => x.OptionalEmailNotificationEnabled);
    }
}
