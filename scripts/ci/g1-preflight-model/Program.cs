using System.Data;
using System.Text.Json;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

// Source/model inspection only. Never Open/EnsureCreated/Migrate or issue SQL.
try
{
    using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer("Server=127.0.0.1;Database=G1ModelOnly;Encrypt=False").Options);
    if (context.Database.GetDbConnection().State != ConnectionState.Closed) throw new Exception();
    var model = context.GetService<IDesignTimeModel>().Model;
    IEntityType Table(string name) => model.GetEntityTypes().Single(e => e.GetTableName() == name);
    void Require(bool value) { if (!value) throw new Exception(); }
    bool Unique(IEntityType e, params string[] keys) => e.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(keys));
    var snapshots = Table("VisitTripSnapshots");
    var stops = Table("VisitTripSnapshotStops");
    Require(snapshots.FindPrimaryKey()!.Properties.Single().Name == "VisitTripSnapshotId");
    Require(stops.FindPrimaryKey()!.Properties.Single().Name == "VisitTripSnapshotStopId");
    Require(Unique(snapshots, "VisitTripId", "SnapshotVersion"));
    Require(Unique(stops, "VisitTripSnapshotId", "StopSequence"));
    Require(stops.GetForeignKeys().Any(f => f.PrincipalEntityType == snapshots && f.DeleteBehavior == DeleteBehavior.Cascade));
    foreach (var name in new[] { "ClaimedDistanceKmSnapshot", "SystemDistanceKmSnapshot", "ApprovedDistanceKmSnapshot", "RatePerKmSnapshot" })
        Require(snapshots.FindProperty(name)!.GetPrecision() == 10 && snapshots.FindProperty(name)!.GetScale() == 2);
    Require(snapshots.FindProperty("SubsidyAmountSnapshot")!.GetPrecision() == 12);
    Require(snapshots.FindProperty("ApprovalBasisHashSnapshot")!.GetColumnType() == "varbinary(32)");
    Require(Table("Locations").FindProperty("RowVersion")!.IsConcurrencyToken);
    // Produce a scoped reproducible EXPECTED model reference privately, not a baseline.
    var required = JsonDocument.Parse(File.ReadAllText("docs/release/g1-minimal-preflight/requirements.json"));
    var scope = required.RootElement.EnumerateArray().Select(r => r.GetProperty("table").GetString()).ToHashSet();
    var reference = model.GetEntityTypes().Where(e => scope.Contains(e.GetTableName())).Select(e => {
        var table = StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema());
        return new {
            table=e.GetTableName(),
            columns=e.GetProperties().Select(p => new { name=p.GetColumnName(table), type=p.GetColumnType(table), nullable=p.IsColumnNullable(table), defaultSql=p.GetDefaultValueSql(table), defaultValue=p.GetDefaultValue(table), computed=p.GetComputedColumnSql(table) }).ToArray(),
            primaryKey=e.FindPrimaryKey()?.Properties.Select(p => p.GetColumnName(table)).ToArray(),
            indexes=e.GetIndexes().Select(i => new { name=i.GetDatabaseName(), unique=i.IsUnique, keys=i.Properties.Select(p => p.GetColumnName(table)).ToArray(), filter=i.GetFilter() }).ToArray(),
            foreignKeys=e.GetForeignKeys().Select(f => new { columns=f.Properties.Select(p => p.GetColumnName(table)).ToArray(), principal=f.PrincipalEntityType.GetTableName(), principalKeys=f.PrincipalKey.Properties.Select(p => p.Name).ToArray(), delete=f.DeleteBehavior.ToString() }).ToArray()
        };
    }).ToArray();
    File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!, "g1-ef-required-private.json"), JsonSerializer.Serialize(reference));
    Require(context.Database.GetDbConnection().State == ConnectionState.Closed);
    Directory.CreateDirectory("artifacts");
    File.WriteAllText("artifacts/preflight-model.json", JsonSerializer.Serialize(new {
        source=Environment.GetEnvironmentVariable("GITHUB_SHA"), result="PASS", modelRequirements="PASS", actualSqlCompatibility="UNKNOWN", originalBaseline="UNKNOWN", databaseConnectionOpened=false, liveSql=false,
        scope="FROZEN_EF_REQUIREMENTS_ONLY_NOT_CREATE_BASELINE"
    }));
    Console.WriteLine("PreflightModel=PASS; ConnectionOpened=FALSE; ActualSchema=UNKNOWN;");
    return 0;
}
catch { Console.WriteLine("PreflightModel=STOP; Details=SUPPRESSED;"); return 1; }
