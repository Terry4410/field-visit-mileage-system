using System.Reflection;
using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("DB_WORK2_CR09_SQL_CONNECTION")
    ?? throw new InvalidOperationException("DB_WORK2_CR09_SQL_CONNECTION is required.");

AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
const int baseVersion = 7;
const int resultVersion = baseVersion + 1;
long tripId;
long baseSnapshotId;
long correctionId;
byte[] originalRowVersion;
string originalBaseFingerprint;

await using (var schemaDb = NewDb())
{
    if (!await schemaDb.Database.EnsureCreatedAsync())
        throw new InvalidOperationException("CR-09 disposable database was not empty before EnsureCreated.");
}

var visitDate = new DateOnly(2026, 9, 10);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

await using (var seedDb = NewDb())
{
    var trip = new VisitTrip
    {
        TripNo = "CR09-TRIP-001",
        UserId = 7001,
        OrganizationId = 1,
        TeamId = 10,
        VisitDate = visitDate,
        StartTime = new TimeOnly(9, 0),
        EndTime = new TimeOnly(10, 0),
        Status = TripStatuses.Approved,
        VehicleType = "Motorcycle",
        Purpose = "CR-09 atomic rollback proof",
        Notes = "base trip",
        ApprovedAt = DateTime.UtcNow.AddDays(-1),
        CreatedAt = DateTime.UtcNow.AddDays(-2),
        CreatedByUserId = 7001
    };
    seedDb.VisitTrips.Add(trip);
    await seedDb.SaveChangesAsync();
    tripId = trip.VisitTripId;

    var baseSnapshot = new VisitTripSnapshot
    {
        VisitTripId = tripId,
        SnapshotVersion = baseVersion,
        SnapshotType = "Approved",
        TripNo = trip.TripNo,
        UserId = trip.UserId,
        EmployeeNoSnapshot = "CR09-U001",
        DisplayNameSnapshot = "CR09 Visitor",
        OrganizationId = 1,
        OrganizationNameSnapshot = "CR09 Organization",
        TeamId = 10,
        TeamCodeSnapshot = "CR09-T10",
        TeamNameSnapshot = "CR09 Team",
        VisitDate = visitDate,
        StartTime = trip.StartTime,
        EndTime = trip.EndTime,
        StatusSnapshot = TripStatuses.Approved,
        VehicleTypeSnapshot = "Motorcycle",
        ClaimedDistanceKmSnapshot = 10m,
        SystemDistanceKmSnapshot = 10m,
        ApprovedDistanceKmSnapshot = 10m,
        RatePerKmSnapshot = 3m,
        SubsidyAmountSnapshot = 30m,
        RouteProviderSnapshot = "CR09",
        ApprovedAtSnapshot = trip.ApprovedAt,
        ApproverUserId = 7002,
        ApproverNameSnapshot = "CR09 Leader",
        NotesSnapshot = "base snapshot",
        CreatedAt = DateTime.UtcNow.AddDays(-1),
        CreatedByUserId = 7002
    };
    baseSnapshot.Stops.Add(new VisitTripSnapshotStop
    {
        StopSequence = 1,
        LocationCodeSnapshot = "CR09-L001",
        LocationNameSnapshot = "CR09 Client",
        AddressSnapshot = "CR09 Address",
        ProjectCodeSnapshot = "CR09-P001",
        ProjectNameSnapshot = "CR09 Project",
        VisitTypeCodeSnapshot = "VISIT",
        VisitTypeNameSnapshot = "Visit",
        VisitPurposeSnapshot = "Base purpose",
        NotesSnapshot = "base stop",
        CreatedAt = DateTime.UtcNow.AddDays(-1)
    });
    seedDb.VisitTripSnapshots.Add(baseSnapshot);
    await seedDb.SaveChangesAsync();
    baseSnapshotId = baseSnapshot.VisitTripSnapshotId;

    var proposal = new CorrectionProposal(
        visitDate,
        new TimeOnly(9, 0),
        new TimeOnly(10, 0),
        "corrected notes",
        12m,
        12m,
        3m,
        36m,
        [new CorrectionStopProposal(
            1,
            "CR09-L001",
            "CR09 Client",
            "CR09 Address",
            "CR09-P001",
            "CR09 Project",
            "VISIT",
            "Visit",
            "Corrected purpose",
            "corrected stop")]);

    var correction = new CorrectionRequest
    {
        VisitTripId = tripId,
        BaseSnapshotId = baseSnapshotId,
        ResultSnapshotId = null,
        Status = "PendingAdminClose",
        Reason = "CR-09 rollback injection",
        ProposedChangesJson = JsonSerializer.Serialize(proposal, jsonOptions),
        RequestedByUserId = 7001,
        RequestedAt = DateTime.UtcNow.AddHours(-2),
        LeaderReviewedByUserId = 7002,
        LeaderReviewedAt = DateTime.UtcNow.AddHours(-1),
        LeaderComments = "approved for admin close"
    };
    seedDb.CorrectionRequests.Add(correction);
    await seedDb.SaveChangesAsync();
    correctionId = correction.CorrectionRequestId;

    seedDb.CorrectionRequestChanges.Add(new CorrectionRequestChange
    {
        CorrectionRequestId = correctionId,
        FieldName = "ApprovedDistanceKm",
        OldValue = "10",
        NewValue = "12",
        CreatedAt = DateTime.UtcNow.AddHours(-1)
    });
    await seedDb.SaveChangesAsync();
}

await using (var beforeDb = NewDb())
{
    var before = await beforeDb.CorrectionRequests.AsNoTracking().SingleAsync(x => x.CorrectionRequestId == correctionId);
    if (before.Status != "PendingAdminClose" || before.ResultSnapshotId is not null)
        throw new InvalidOperationException("CR-09 fixture must start PendingAdminClose with null ResultSnapshotId.");
    if (before.RowVersion is not { Length: 8 })
        throw new InvalidOperationException("CR-09 fixture did not obtain SQL Server RowVersion.");
    originalRowVersion = before.RowVersion.ToArray();
    originalBaseFingerprint = await SnapshotFingerprintAsync(beforeDb, baseSnapshotId);
    if (await beforeDb.VisitTripSnapshots.AsNoTracking().CountAsync(x => x.VisitTripId == tripId) != 1)
        throw new InvalidOperationException("CR-09 fixture must start with exactly base Snapshot N.");
}

await using (var injectionDb = NewDb())
{
    await injectionDb.Database.ExecuteSqlRawAsync("""
CREATE OR ALTER FUNCTION dbo.CR09_AuditAllowed(@Action nvarchar(200), @EntityId nvarchar(200))
RETURNS bit
AS
BEGIN
    DECLARE @Allowed bit = 1;
    IF @Action = N'CorrectionAdminClosed'
       AND EXISTS (
           SELECT 1
           FROM dbo.CorrectionRequests c
           JOIN dbo.VisitTripSnapshots b ON b.VisitTripSnapshotId = c.BaseSnapshotId
           JOIN dbo.VisitTripSnapshots n ON n.VisitTripId = c.VisitTripId
           WHERE c.CorrectionRequestId = TRY_CONVERT(bigint, @EntityId)
             AND n.SnapshotType = N'Correction'
             AND n.SnapshotVersion = b.SnapshotVersion + 1
       )
       SET @Allowed = 0;
    RETURN @Allowed;
END;
""");
    await injectionDb.Database.ExecuteSqlRawAsync("""
ALTER TABLE dbo.AuditLogs WITH CHECK
ADD CONSTRAINT CK_CR09_AuditFailure
CHECK (dbo.CR09_AuditAllowed([Action], [EntityId]) = 1);
""");
}

var guardBefore = await ReadGuardAsync(connectionString, correctionId);
if (guardBefore != 1)
    throw new InvalidOperationException("CR-09 guard must allow CorrectionAdminClosed before Snapshot N+1 exists.");
Console.WriteLine("DBW2_CR09_GUARD_BEFORE_SNAPSHOT=ALLOW");

Exception? injectedFailure = null;
await using (var actionDb = NewDb())
{
    var admin = new CurrentUserDto(7003, "CR09-ADMIN", "CR09 Admin", null, 1, null, null, ["admin"]);
    var repository = new V160FinalRepository(actionDb, null!, null!, null!);
    ITransactionBoundary transactionBoundary = new EfTransactionBoundary(actionDb);
    var service = new V160FinalService(
        new FixedCurrentUser(admin),
        repository,
        null!,
        null!,
        null!,
        null!,
        null!,
        null!,
        null!,
        transactionBoundary);

    try
    {
        await service.CloseCorrectionAsync(
            correctionId,
            new CloseCorrectionRequest(true, "CR-09 deterministic failure", Convert.ToBase64String(originalRowVersion)),
            default);
    }
    catch (Exception ex)
    {
        injectedFailure = ex;
    }
}

if (injectedFailure is null)
    throw new InvalidOperationException("CR-09 expected deterministic audit-stage failure, but close committed successfully.");
var failureText = injectedFailure.ToString();
if (!failureText.Contains("CK_CR09_AuditFailure", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException($"CR-09 failed at the wrong point: {injectedFailure.GetType().Name}: {injectedFailure.Message}");
Console.WriteLine("DBW2_CR09_FAILURE_INJECTION=CK_CR09_AuditFailure");
Console.WriteLine("DBW2_CR09_SNAPSHOT_FLUSH_BEFORE_FAILURE=PASS");
Console.WriteLine("DBW2_CR09_PRODUCTION_TRANSACTION_BOUNDARY=EfTransactionBoundary");

await using (var verifyDb = NewDb())
{
    var after = await verifyDb.CorrectionRequests.AsNoTracking().SingleAsync(x => x.CorrectionRequestId == correctionId);
    var totalSnapshots = await verifyDb.VisitTripSnapshots.AsNoTracking().CountAsync(x => x.VisitTripId == tripId);
    var correctionNPlusOne = await verifyDb.VisitTripSnapshots.AsNoTracking().CountAsync(x =>
        x.VisitTripId == tripId && x.SnapshotType == "Correction" && x.SnapshotVersion == resultVersion);
    var successAuditCount = await verifyDb.AuditLogs.AsNoTracking().CountAsync(x =>
        x.EntityType == "CorrectionRequest"
        && x.EntityId == correctionId.ToString()
        && x.Action == "CorrectionAdminClosed");
    var afterBaseFingerprint = await SnapshotFingerprintAsync(verifyDb, baseSnapshotId);
    var guardAfter = await ReadGuardAsync(connectionString, correctionId);

    Require(totalSnapshots == 1, $"expected only base Snapshot N after rollback, actual={totalSnapshots}");
    Require(correctionNPlusOne == 0, $"Correction Snapshot N+1 persisted after rollback, count={correctionNPlusOne}");
    Require(after.ResultSnapshotId is null, $"ResultSnapshotId partially persisted: {after.ResultSnapshotId}");
    Require(after.Status == "PendingAdminClose", $"correction status partially advanced: {after.Status}");
    Require(after.AdminClosedByUserId is null && after.AdminClosedAt is null && after.AdminComments is null,
        "admin close metadata partially persisted.");
    Require(after.RowVersion.SequenceEqual(originalRowVersion), "CorrectionRequest RowVersion partially advanced.");
    Require(successAuditCount == 0, $"success audit persisted after rollback, count={successAuditCount}");
    Require(afterBaseFingerprint == originalBaseFingerprint, "base Snapshot N or its stops changed during failed close.");
    Require(guardAfter == 1, "CR-09 guard still sees Snapshot N+1 after rollback.");

    Console.WriteLine($"DBW2_CR09_SNAPSHOT_TOTAL_AFTER={totalSnapshots}");
    Console.WriteLine($"DBW2_CR09_CORRECTION_N_PLUS_1_COUNT={correctionNPlusOne}");
    Console.WriteLine("DBW2_CR09_RESULT_SNAPSHOT_ID=NULL");
    Console.WriteLine($"DBW2_CR09_STATUS={after.Status}");
    Console.WriteLine($"DBW2_CR09_SUCCESS_AUDIT_COUNT={successAuditCount}");
    Console.WriteLine("DBW2_CR09_BASE_SNAPSHOT_UNCHANGED=YES");
    Console.WriteLine("DBW2_CR09_ROWVERSION_UNCHANGED=YES");
    Console.WriteLine("DBW2_CR09_ADMIN_CLOSE_STATE_UNCHANGED=YES");
    Console.WriteLine("DBW2_CR09_GUARD_AFTER_ROLLBACK=ALLOW");
    Console.WriteLine("DBW2_CR09_ATOMIC_ROLLBACK=PASS");
    Console.WriteLine("DBW2_CR09=1/1");
}

static async Task<int> ReadGuardAsync(string connectionString, long correctionId)
{
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
    return await db.Database.SqlQueryRaw<int>(
            $"SELECT CONVERT(int, dbo.CR09_AuditAllowed(N'CorrectionAdminClosed', N'{correctionId}')) AS [Value]")
        .SingleAsync();
}

static async Task<string> SnapshotFingerprintAsync(AppDbContext db, long snapshotId)
{
    var snapshot = await db.VisitTripSnapshots.AsNoTracking().Include(x => x.Stops)
        .SingleAsync(x => x.VisitTripSnapshotId == snapshotId);
    var scalar = ObjectFingerprint(snapshot, nameof(VisitTripSnapshot.Stops));
    var stops = snapshot.Stops.OrderBy(x => x.StopSequence)
        .Select(x => ObjectFingerprint(x, nameof(VisitTripSnapshotStop.Snapshot)))
        .ToArray();
    return JsonSerializer.Serialize(new { scalar, stops });
}

static string ObjectFingerprint(object value, params string[] excludedProperties)
{
    var excluded = excludedProperties.ToHashSet(StringComparer.Ordinal);
    var payload = value.GetType()
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(x => x.CanRead && !excluded.Contains(x.Name))
        .OrderBy(x => x.Name, StringComparer.Ordinal)
        .ToDictionary(x => x.Name, x => (object?)x.GetValue(value), StringComparer.Ordinal);
    return JsonSerializer.Serialize(payload);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException($"CR-09 rollback assertion failed: {message}");
}

sealed class FixedCurrentUser(CurrentUserDto user) : ICurrentUserService
{
    public CurrentUserDto GetRequired() => user;
}
