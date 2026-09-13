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
                VisitTripCount bigint NOT NULL,
                VisitTripSnapshotCount bigint NOT NULL,
                VisitTripSnapshotStopCount bigint NOT NULL);
            INSERT dbo.SchemaVersions VALUES(N'1.8.0-005',N'F-A prerequisite',SYSUTCDATETIME(),N'F-A');
            INSERT dbo.SchemaMigrationDataBaselines VALUES(N'1.8.0-001',0,0,0);
            """);
        var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "Schema");
        var session = await File.ReadAllTextAsync(Path.Combine(schemaDirectory, "session-options.sql"));
        await db.Database.ExecuteSqlRawAsync(session + "\n" + await File.ReadAllTextAsync(Path.Combine(schemaDirectory, "1800_006.Up.sql")));
        Console.WriteLine("FA_1800_006_APPLY=PASS");
        await db.Database.ExecuteSqlRawAsync(session + "\n" + await File.ReadAllTextAsync(Path.Combine(schemaDirectory, "1800_006.Verify.sql")));
        Console.WriteLine("FA_1800_006_VERIFY=PASS");
        await Assert1800007PrerequisiteCleanAsync(db);
        await db.Database.ExecuteSqlRawAsync(session + "\n" + await File.ReadAllTextAsync(Path.Combine(schemaDirectory, "1800_007.Up.sql")));
        Console.WriteLine("FA_1800_007_APPLY=PASS");
        await db.Database.ExecuteSqlRawAsync(session + "\n" + await File.ReadAllTextAsync(Path.Combine(schemaDirectory, "1800_007.Verify.sql")));
        Console.WriteLine("FA_1800_007_VERIFY=PASS");
    }

    private static async Task Assert1800007PrerequisiteCleanAsync(AppDbContext db)
    {
        const string sql = """
            SELECT COUNT_BIG(*)
            FROM
            (
                SELECT N'table:' + t.name AS Artifact
                FROM sys.tables t
                WHERE SCHEMA_NAME(t.schema_id) = N'dbo'
                  AND t.name IN (N'GeocodingAttempts', N'RouteCalculationAttempts', N'MileageGovernanceEvents')

                UNION ALL

                SELECT N'column:' + t.name + N'.' + c.name
                FROM sys.columns c
                JOIN sys.tables t ON t.object_id = c.object_id
                WHERE SCHEMA_NAME(t.schema_id) = N'dbo'
                  AND
                  (
                      (t.name = N'Locations' AND c.name IN
                          (N'SelectedGeocodingAttemptId'))
                      OR
                      (t.name = N'MileageCalculations' AND c.name IN
                          (N'SelectedRouteCalculationAttemptId', N'ManualFallbackUsed',
                           N'DistanceDecisionGovernanceVersion', N'ApprovedDistanceSource',
                           N'ApprovalBasisCode', N'ApprovalBasisHash', N'DistanceApprovedAt',
                           N'DistanceApprovedByUserId', N'InvalidatedAt', N'InvalidatedByUserId',
                           N'InvalidationReason'))
                      OR
                      (t.name = N'VisitTripSnapshots' AND c.name IN
                          (N'MileageRouteAttemptIdSnapshot', N'RouteTravelModeSnapshot',
                           N'RouteCalculatedAtSnapshot', N'RouteCalculationStatusSnapshot',
                           N'RouteErrorCodeSnapshot', N'RouteCorrelationIdSnapshot',
                           N'ApprovedDistanceSourceSnapshot', N'ApprovalBasisCodeSnapshot',
                           N'ApprovalBasisHashSnapshot', N'DistanceApprovedAtSnapshot'))
                  )

                UNION ALL

                SELECT N'object:' + o.name
                FROM sys.objects o
                WHERE SCHEMA_NAME(o.schema_id) = N'dbo'
                  AND o.name IN
                  (
                      N'PK_GeocodingAttempts',
                      N'FK_GeocodingAttempts_Locations', N'FK_GeocodingAttempts_RequestedByUser',
                      N'CK_GeocodingAttempts_Status', N'CK_GeocodingAttempts_Result',
                      N'UQ_GeocodingAttempts_Correlation',
                      N'PK_RouteCalculationAttempts',
                      N'FK_RouteCalculationAttempts_Trips', N'FK_RouteCalculationAttempts_BasisSnapshot',
                      N'FK_RouteCalculationAttempts_RequestedByUser',
                      N'CK_RouteCalculationAttempts_Basis', N'CK_RouteCalculationAttempts_Reason',
                      N'CK_RouteCalculationAttempts_LeaderBasis', N'CK_RouteCalculationAttempts_Vehicle',
                      N'CK_RouteCalculationAttempts_Mode', N'CK_RouteCalculationAttempts_VehicleMode',
                      N'CK_RouteCalculationAttempts_StopCount', N'CK_RouteCalculationAttempts_Status',
                      N'CK_RouteCalculationAttempts_Result', N'UQ_RouteCalculationAttempts_Correlation',
                      N'TR_RouteCalculationAttempts_BasisTrip',
                      N'PK_MileageGovernanceEvents',
                      N'DF_MileageGovernanceEvents_OccurredAt',
                      N'FK_MileageGovernanceEvents_Trips', N'FK_MileageGovernanceEvents_Snapshot',
                      N'FK_MileageGovernanceEvents_RouteAttempt', N'FK_MileageGovernanceEvents_ActorUser',
                      N'CK_MileageGovernanceEvents_Type',
                      N'FK_Locations_SelectedGeocodingAttempt', N'TR_Locations_SelectedGeocodingAttempt',
                      N'DF_MileageCalculations_ManualFallbackUsed',
                      N'FK_MileageCalculations_SelectedRouteAttempt',
                      N'FK_MileageCalculations_DistanceApprovedByUser',
                      N'FK_MileageCalculations_InvalidatedByUser',
                      N'CK_MileageCalculations_ApprovedDistanceSource',
                      N'CK_MileageCalculations_ApprovalEvidence',
                      N'TR_MileageCalculations_SelectedRouteTrip', N'TR_MileageCalculations_DecisionEvidence',
                      N'FK_VisitTripSnapshots_RouteAttempt', N'CK_VisitTripSnapshots_RouteMode',
                      N'CK_VisitTripSnapshots_RouteStatus', N'CK_VisitTripSnapshots_ApprovedDistanceSource',
                      N'CK_VisitTripSnapshots_ApprovalBasis', N'TR_VisitTripSnapshots_RouteAttemptTrip'
                  )

                UNION ALL

                SELECT N'index:' + i.name
                FROM sys.indexes i
                JOIN sys.tables t ON t.object_id = i.object_id
                WHERE SCHEMA_NAME(t.schema_id) = N'dbo'
                  AND i.name IN
                  (
                      N'IX_GeocodingAttempts_Location_Requested',
                      N'IX_RouteCalculationAttempts_Trip_Requested',
                      N'IX_RouteCalculationAttempts_BasisSnapshot',
                      N'IX_MileageGovernanceEvents_Trip_Occurred',
                      N'IX_MileageGovernanceEvents_Correlation',
                      N'IX_Locations_SelectedGeocodingAttempt'
                  )

                UNION ALL

                SELECT N'schema-version:1.8.0-007'
                WHERE OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NOT NULL
                  AND EXISTS
                  (
                      SELECT 1
                      FROM dbo.SchemaVersions
                      WHERE VersionNumber = N'1.8.0-007'
                  )
            ) contamination;
            """;

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();
        var value = await command.ExecuteScalarAsync();
        var contaminationCount = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        if (contaminationCount != 0)
            throw new InvalidOperationException($"FA prerequisite contains {contaminationCount} 1800_007-owned schema artifact(s) before authoritative 1800_007.");
        Console.WriteLine("FA_1800_007_PREREQUISITE_CLEAN_ROOM=PASS");
    }
}

sealed class FaPrerequisiteModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        var mileageCalculation = modelBuilder.Model.FindEntityType(typeof(MileageCalculation))
            ?? throw new InvalidOperationException("MileageCalculation metadata is missing from the F-A prerequisite model.");
        var prerequisiteOnlyUserForeignKeys = mileageCalculation.GetForeignKeys()
            .Where(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(User)
                && foreignKey.Properties.Count == 1
                && (foreignKey.Properties[0].Name == nameof(MileageCalculation.DistanceApprovedByUserId)
                    || foreignKey.Properties[0].Name == nameof(MileageCalculation.InvalidatedByUserId)))
            .ToList();
        foreach (var foreignKey in prerequisiteOnlyUserForeignKeys)
            mileageCalculation.RemoveForeignKey(foreignKey);

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
