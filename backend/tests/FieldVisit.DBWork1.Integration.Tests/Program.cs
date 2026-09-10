using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("DB_WORK1_SQL_CONNECTION")
    ?? throw new InvalidOperationException("DB_WORK1_SQL_CONNECTION is required.");

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(connectionString)
    .Options;

await using var db = new AppDbContext(options);

var first = new MileageRateRule
{
    OrganizationId = 501,
    RuleName = "DBW1 EF first",
    VehicleType = "MOTORCYCLE",
    RatePerKm = 2.50m,
    EffectiveFrom = new DateOnly(2026, 1, 1),
    EffectiveTo = null,
    IsActive = true,
    CreatedAt = DateTime.UtcNow
};

db.MileageRateRules.Add(first);
await db.SaveChangesAsync();
if (first.MileageRateRuleId <= 0 || first.RowVersion is not { Length: 8 })
    throw new InvalidOperationException("EF INSERT did not retrieve identity/RowVersion with trigger enabled.");
Console.WriteLine("DBW1_EF_TRIGGER_INSERT=PASS");

var firstVersion = first.RowVersion.ToArray();
var second = new MileageRateRule
{
    OrganizationId = 501,
    RuleName = "DBW1 EF second",
    VehicleType = "MOTORCYCLE",
    RatePerKm = 2.75m,
    EffectiveFrom = new DateOnly(2026, 7, 1),
    EffectiveTo = null,
    IsActive = true,
    CreatedAt = DateTime.UtcNow
};
db.MileageRateRules.Add(second);
await db.SaveChangesAsync();
if (second.RowVersion is not { Length: 8 })
    throw new InvalidOperationException("EF second INSERT did not retrieve RowVersion.");

await db.Entry(first).ReloadAsync();
if (first.EffectiveTo != new DateOnly(2026, 6, 30))
    throw new InvalidOperationException($"DB EffectiveTo derivation after INSERT is wrong: {first.EffectiveTo}.");

var secondVersion = second.RowVersion.ToArray();
second.EffectiveFrom = new DateOnly(2026, 9, 1);
second.EffectiveTo = null;
second.UpdatedAt = DateTime.UtcNow;
await db.SaveChangesAsync();
if (second.RowVersion is not { Length: 8 } || second.RowVersion.SequenceEqual(secondVersion))
    throw new InvalidOperationException("EF UPDATE did not retrieve a fresh RowVersion with trigger enabled.");
Console.WriteLine("DBW1_EF_TRIGGER_UPDATE=PASS");
Console.WriteLine("DBW1_EF_ROWVERSION=PASS");

await db.Entry(first).ReloadAsync();
await db.Entry(second).ReloadAsync();
if (first.EffectiveTo != new DateOnly(2026, 8, 31) || second.EffectiveTo is not null)
    throw new InvalidOperationException($"DB-derived EffectiveTo lost authority after EF UPDATE: first={first.EffectiveTo}, second={second.EffectiveTo}.");
if (first.RowVersion.SequenceEqual(firstVersion))
    throw new InvalidOperationException("Trigger-derived first-row EffectiveTo update did not advance RowVersion.");
Console.WriteLine("DBW1_EF_DERIVED_EFFECTIVETO=PASS");
