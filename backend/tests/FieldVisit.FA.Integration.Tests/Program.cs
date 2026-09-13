using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

var connectionString = Environment.GetEnvironmentVariable("FA_SQL_CONNECTION")
    ?? throw new InvalidOperationException("FA_SQL_CONNECTION is required.");

await FaSchemaBootstrap.InitializeAsync(connectionString);
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(connectionString).Options);

var seedUser = new FieldVisit.Domain.Entities.User
{
    DisplayName = "F-A schema probe", IsActive = true, CreatedAt = DateTime.UtcNow
};
var seedLocation = new FieldVisit.Domain.Entities.Location
{
    LocationName = "F-A schema probe", LocationType = "Official", ApprovalStatus = "Pending",
    GeocodingStatus = "Pending", IsActive = true, CreatedAt = DateTime.UtcNow,
    CreatedByUserId = null
};
db.Users.Add(seedUser);
await db.SaveChangesAsync();
seedLocation.CreatedByUserId = seedUser.UserId;
db.Locations.Add(seedLocation);
await db.SaveChangesAsync();
var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes("F-A raw hash persistence"));
db.GeocodingAttempts.Add(new FieldVisit.Domain.Entities.GeocodingAttempt
{
    LocationId = seedLocation.LocationId, Provider = "F-A", AddressBasisHash = expectedHash,
    Status = "Pending", CorrelationId = Guid.NewGuid(), RequestedAt = DateTime.UtcNow,
    RequestedByUserId = seedUser.UserId
});
await db.SaveChangesAsync();
var persistedHash = await db.GeocodingAttempts.AsNoTracking().Select(x => x.AddressBasisHash).SingleAsync();
if (!persistedHash.SequenceEqual(expectedHash)) throw new InvalidOperationException("FA hash persistence mismatch.");

var expectedHashColumns = new[]
{
    (Table: "GeocodingAttempts", Column: "AddressBasisHash"),
    (Table: "RouteCalculationAttempts", Column: "RequestBasisHash"),
    (Table: "MileageCalculations", Column: "ApprovalBasisHash"),
    (Table: "VisitTripSnapshots", Column: "ApprovalBasisHashSnapshot")
};
foreach (var (table, column) in expectedHashColumns)
{
    var size = await ScalarAsync<int>(db, $"SELECT max_length FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.{table}') AND name=N'{column}'");
    if (size != 32) throw new InvalidOperationException($"FA hash column {table}.{column} is {size}, expected 32.");
}
var datetimeColumns = new[]
{
    (Table: "GeocodingAttempts", Column: "RequestedAt"),
    (Table: "RouteCalculationAttempts", Column: "RequestedAt"),
    (Table: "MileageGovernanceEvents", Column: "OccurredAt"),
    (Table: "VisitTripSnapshots", Column: "RouteCalculatedAtSnapshot")
};
foreach (var (table, column) in datetimeColumns)
{
    var scale = await ScalarAsync<byte>(db, $"SELECT scale FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.{table}') AND name=N'{column}'");
    if (scale != 3) throw new InvalidOperationException($"FA datetime column {table}.{column} scale {scale}, expected 3.");
}
Console.WriteLine("FA_REAL_SQL_HASH_STORAGE=PASS");
Console.WriteLine("FA_REAL_SQL_SCHEMA_MAPPING=PASS");
Console.WriteLine("FA_REAL_SQL_REGRESSION=PASS");

static async Task<T> ScalarAsync<T>(AppDbContext db, string sql)
{
    await using var command = db.Database.GetDbConnection().CreateCommand();
    command.CommandText = sql;
    if (command.Connection!.State != System.Data.ConnectionState.Open)
        await command.Connection.OpenAsync();
    var value = await command.ExecuteScalarAsync();
    return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
}
