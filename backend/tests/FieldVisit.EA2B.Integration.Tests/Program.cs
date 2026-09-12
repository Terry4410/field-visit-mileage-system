using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

var connection = Environment.GetEnvironmentVariable("EA2B_SQL_CONNECTION")
    ?? throw new InvalidOperationException("EA2B_SQL_CONNECTION is required.");
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(connection, sql => sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null))
    .ReplaceService<IModelCustomizer, NotificationModelCustomizer>().Options;
AppDbContext NewDb() => new(options);
await Ea2aSchemaBootstrap.InitializeAsync(connection); // Unchanged, authoritative E-A0/E-A1/E-A2A schema path.
var fx = await Fixture.SeedAsync(NewDb);
Console.WriteLine("EA2B_REAL_SQL_DISPOSABLE=PASS");
await RunCreationAsync(fx, NewDb);
await RunImportAsync(fx, NewDb);
await RunImportRollbackAsync(fx, NewDb);
await RunApprovalAsync(fx, NewDb);
await RunNegativeCyclesAsync(fx, NewDb);
await RunRollbackAsync(fx, NewDb);
await RunSettingsAndIdentityAsync(fx, NewDb);
Console.WriteLine("EA2B_REAL_SQL_REGRESSION=PASS");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("EA2B assertion failed: " + message);
}
static void Pass(string marker) => Console.WriteLine(marker + "=PASS");
static async Task ExpectFailureAsync(Func<Task> action, string message)
{
    try { await action(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("EA2B expected failure: " + message);
}
static async Task<DateTime> SqlMillisecondsAsync(AppDbContext db, DateTime time)
{
    var parameter = new SqlParameter("@time", System.Data.SqlDbType.DateTime2) { Scale = 7, Value = time };
    return await db.Database.SqlQueryRaw<DateTime>("SELECT CAST(@time AS datetime2(3)) AS [Value]", parameter).SingleAsync();
}
static Task<List<MailOutbox>> EventsAsync(AppDbContext db, int id, string code) =>
    db.Set<MailOutbox>().AsNoTracking().Where(x => x.AggregateType == "Location"
        && x.AggregateId == id.ToString() && x.EventCode == code).ToListAsync();
static async Task RequireMarkerAsync(AppDbContext db, int id)
{
    var marker = await db.AuditLogs.SingleAsync(x => x.EntityType == "Location" && x.EntityId == id.ToString()
        && x.Action == LocationReviewCycleContract.InitialAuditAction);
    using var payload = JsonDocument.Parse(marker.NewValues!);
    Require(payload.RootElement.GetProperty("markerVersion").GetInt32() == 1
        && payload.RootElement.GetProperty("marker").GetString() == "INITIAL_REVIEW_CYCLE"
        && payload.RootElement.GetProperty("locationId").GetInt32() == id, "exact permanent initial marker contract");
}

static async Task RunCreationAsync(Fixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    var managed = await fx.Managed(db).CreateManagedLocationAsync(fx.Request("EA2B-managed"), default);
    await RequireMarkerAsync(db, managed.LocationId);
    var row = await db.Locations.AsNoTracking().SingleAsync(x => x.LocationId == managed.LocationId);
    var queued = (await EventsAsync(db, row.LocationId, NotificationEventCodes.LocationReviewRequested)).Single();
    Require(queued.BusinessEventKey == $"LOCATION:{row.LocationId}:REVIEW_REQUESTED:CREATE", "managed key");
    Require(queued.EventOccurredAt == await SqlMillisecondsAsync(db, row.CreatedAt), "creation authoritative time");
    Require(!await db.LocationApprovalHistories.AnyAsync(x => x.LocationId == row.LocationId), "no history at Pending entry");
    Pass("EA2B-01_MANAGED_CREATE_PENDING");

    var trip = await fx.Trips(db).CreateAsync(fx.TripRequest("EA2B-temp-create", new DateOnly(2026, 8, 1)), default);
    var temp = await db.Locations.AsNoTracking().SingleAsync(x => x.LocationName == "EA2B-temp-create");
    await RequireMarkerAsync(db, temp.LocationId);
    Require((await EventsAsync(db, temp.LocationId, NotificationEventCodes.LocationReviewRequested)).Single().BusinessEventKey
        == $"LOCATION:{temp.LocationId}:REVIEW_REQUESTED:TEMP_CREATE", "temporary key");
    Require((await EventsAsync(db, temp.LocationId, NotificationEventCodes.LocationReviewRequested)).Single().EventOccurredAt
        == await SqlMillisecondsAsync(db, temp.CreatedAt), "temporary authoritative time");
    Pass("EA2B-02_TRIP_TEMP_CREATE_PENDING");
    await fx.Trips(db).UpdateAsync(trip.VisitTripId,
        fx.TripRequest("EA2B-temp-update", new DateOnly(2026, 8, 2)), trip.RowVersion, default);
    var updateTemp = await db.Locations.AsNoTracking().SingleAsync(x => x.LocationName == "EA2B-temp-update");
    await RequireMarkerAsync(db, updateTemp.LocationId);
    Require((await EventsAsync(db, updateTemp.LocationId, NotificationEventCodes.LocationReviewRequested)).Count == 1, "update temporary event");
    Pass("EA2B-03_TRIP_TEMP_UPDATE_PENDING");
}

static async Task RunImportAsync(Fixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    var source = fx.Source("EA2B-import-create");
    var item = await fx.ItemAsync(db, "Create", source);
    await fx.Imports(db).ConfirmAsync(fx.Creator, item.ImportBatchId, default);
    using var applied = JsonDocument.Parse(item.DataJson);
    var root = applied.RootElement;
    var mutation = root.GetProperty("appliedMutation");
    var id = mutation.GetProperty("locationId").GetInt32();
    Require(root.GetProperty("envelopeVersion").GetInt32() == 1, "envelope version");
    Require(root.GetProperty("source").GetRawText() == source, "complete immutable source, including unknown fields/formatting");
    Require(mutation.GetProperty("transitionKind").GetString() == "CREATE"
        && mutation.GetProperty("priorApprovalStatus").ValueKind == JsonValueKind.Null
        && mutation.GetProperty("enteredPending").GetBoolean(), "CREATE fact");
    var firstKey = $"LOCATION:{id}:REVIEW_REQUESTED:IMPORT_ITEM:{item.ImportBatchItemId}:CREATE";
    Require((await EventsAsync(db, id, NotificationEventCodes.LocationReviewRequested)).Single().BusinessEventKey == firstKey, "import create key");
    var location = await db.Locations.SingleAsync(x => x.LocationId == id);
    Require((await EventsAsync(db, id, NotificationEventCodes.LocationReviewRequested)).Single().EventOccurredAt
        == await SqlMillisecondsAsync(db, location.CreatedAt), "import authoritative time");
    await fx.Events(db).QueueReviewAsync(location, firstKey, location.CreatedAt, default);
    await db.SaveChangesAsync();
    Require((await EventsAsync(db, id, NotificationEventCodes.LocationReviewRequested)).Count == 1, "same review occurrence writer retry");
    var frozen = item.DataJson;
    await ExpectFailureAsync(() => fx.Imports(db).ConfirmAsync(fx.Creator, item.ImportBatchId, default), "same applied retry");
    Require(item.DataJson == frozen && (await EventsAsync(db, id, NotificationEventCodes.LocationReviewRequested)).Count == 1, "retry unchanged");
    item.DataJson = "{}";
    await ExpectFailureAsync(async () => { await db.SaveChangesAsync(); }, "Applied envelope immutable");
    item.DataJson = frozen;
    db.Entry(item).State = EntityState.Unchanged;
    item.Status = "Valid";
    await ExpectFailureAsync(async () => { await db.SaveChangesAsync(); }, "Applied status cannot remove evidence");
    item.Status = "Applied"; db.Entry(item).State = EntityState.Unchanged;
    Pass("EA2B-04_IMPORT_CREATE_ENVELOPE_RETRY_IMMUTABILITY");

    var pendingItem = await fx.ItemAsync(db, "Update", fx.Source("EA2B-import-pending-edit", location.LocationCode));
    await fx.Imports(db).ConfirmAsync(fx.Creator, pendingItem.ImportBatchId, default);
    using var pending = JsonDocument.Parse(pendingItem.DataJson);
    var noReview = pending.RootElement.GetProperty("appliedMutation");
    Require(noReview.GetProperty("transitionKind").GetString() == "NO_NEW_REVIEW"
        && noReview.GetProperty("priorApprovalStatus").GetString() == "Pending"
        && !noReview.GetProperty("enteredPending").GetBoolean(), "Pending to Pending fact");
    Require((await EventsAsync(db, id, NotificationEventCodes.LocationReviewRequested)).Count == 1, "Pending to Pending no event");
    Pass("EA2B-05_PENDING_TO_PENDING_NO_EVENT");

    await fx.Master(db).BatchPublishAsync(new BatchPublishLocationsRequest([id]), default);
    Require((await EventsAsync(db, id, NotificationEventCodes.LocationApproved)).Count == 1, "CREATE envelope positive proof");
    // Two legitimate re-review items, separated by actual approval, are distinct.
    for (var cycle = 1; cycle <= 2; cycle++)
    {
        var rereview = await fx.ItemAsync(db, "Update", fx.Source("EA2B-rereview-" + cycle, location.LocationCode));
        await fx.Imports(db).ConfirmAsync(fx.Creator, rereview.ImportBatchId, default);
        using var envelope = JsonDocument.Parse(rereview.DataJson);
        var fact = envelope.RootElement.GetProperty("appliedMutation");
        Require(fact.GetProperty("locationId").GetInt32() == id && fact.GetProperty("transitionKind").GetString() == "REREVIEW"
            && fact.GetProperty("priorApprovalStatus").GetString() == "Approved" && fact.GetProperty("enteredPending").GetBoolean(), "REREVIEW fact");
        Require(await db.Set<MailOutbox>().AnyAsync(x => x.BusinessEventKey == $"LOCATION:{id}:REVIEW_REQUESTED:IMPORT_ITEM:{rereview.ImportBatchItemId}:REREVIEW"), "distinct re-review key");
        var review = await db.Set<MailOutbox>().SingleAsync(x => x.BusinessEventKey == $"LOCATION:{id}:REVIEW_REQUESTED:IMPORT_ITEM:{rereview.ImportBatchItemId}:REREVIEW");
        Require(review.EventOccurredAt == await SqlMillisecondsAsync(db, location.UpdatedAt!.Value), "re-review authoritative transition time");
        await fx.Master(db).BatchPublishAsync(new BatchPublishLocationsRequest([id]), default);
        Require((await EventsAsync(db, id, NotificationEventCodes.LocationApproved)).Count == 1, "monotonic later-cycle suppression");
    }
    Require((await EventsAsync(db, id, NotificationEventCodes.LocationReviewRequested)).Count == 3, "distinct durable items");
    Pass("EA2B-06_REREVIEW_DISTINCT_ITEMS_MONOTONIC_SUPPRESSION");
}

static async Task RunImportRollbackAsync(Fixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    var first = await fx.ItemAsync(db, "Create", fx.Source("EA2B-partial-success"));
    var second = new ImportBatchItem { ImportBatchId = first.ImportBatchId, RowNumber = 2, EntityType = "Location", Action = "Create",
        DataJson = fx.Source("EA2B-partial-failure"), DisplayKey = "failure", CreatedAt = DateTime.UtcNow };
    db.ImportBatchItems.Add(second);
    await db.SaveChangesAsync();
    var throwing = new FailingWriter(fx.Writer(db), "EA2B-partial-failure");
    var result = await fx.Imports(db, throwing).ConfirmAsync(fx.Creator, first.ImportBatchId, default);
    await using var verify = newDb();
    Require(result.Created == 1 && result.Failed == 1 && result.Updated == 0, "partial counts");
    Require(await verify.Locations.AnyAsync(x => x.LocationName == "EA2B-partial-success")
        && !await verify.Locations.AnyAsync(x => x.LocationName == "EA2B-partial-failure"), "independent item commits");
    var failed = await verify.ImportBatchItems.SingleAsync(x => x.ImportBatchItemId == second.ImportBatchItemId);
    Require(failed.Status == "Failed" && failed.DataJson == fx.Source("EA2B-partial-failure")
        && !LocationImportMutation.IsAppliedEnvelope(failed.DataJson), "failed item no applied fact");
    Require(!await verify.Set<MailOutbox>().AnyAsync(x => x.BusinessEventKey == throwing.Attempt!.BusinessEventKey), "failed item outbox rollback");
    Require(await verify.ImportBatchItems.AnyAsync(x => x.ImportBatchItemId == first.ImportBatchItemId && x.Status == "Applied"), "prior item evidence survives");
    Pass("EA2B-07_IMPORT_ITEM_ROLLBACK_PARTIAL_SUCCESS");

    var existing = await fx.Managed(db).CreateManagedLocationAsync(fx.Request("EA2B-update-rollback-original"), default);
    await fx.Master(db).BatchPublishAsync(new BatchPublishLocationsRequest([existing.LocationId]), default);
    var target = await db.Locations.SingleAsync(x => x.LocationId == existing.LocationId);
    var update = await fx.ItemAsync(db, "Update", fx.Source("EA2B-update-rollback-failure", target.LocationCode));
    var failingUpdate = new FailingWriter(fx.Writer(db), "EA2B-update-rollback-failure");
    await fx.Imports(db, failingUpdate).ConfirmAsync(fx.Creator, update.ImportBatchId, default);
    await using var check = newDb();
    var surviving = await check.Locations.SingleAsync(x => x.LocationId == existing.LocationId);
    Require(surviving.ApprovalStatus == "Approved" && surviving.LocationName == "EA2B-update-rollback-original", "update mutation rollback");
    Require(!LocationImportMutation.IsAppliedEnvelope((await check.ImportBatchItems.SingleAsync(x => x.ImportBatchItemId == update.ImportBatchItemId)).DataJson), "REREVIEW envelope rollback");
    Require(!await check.Set<MailOutbox>().AnyAsync(x => x.BusinessEventKey == failingUpdate.Attempt!.BusinessEventKey), "REREVIEW outbox rollback");
    Pass("EA2B-08_IMPORT_REREVIEW_ROLLBACK");
}

static async Task RunApprovalAsync(Fixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    foreach (var name in new[] { "EA2B-managed", "EA2B-temp-create", "EA2B-temp-update" })
    {
        var row = await db.Locations.SingleAsync(x => x.LocationName == name);
        await fx.Master(db).BatchPublishAsync(new BatchPublishLocationsRequest([row.LocationId]), default);
        var history = await db.LocationApprovalHistories.AsNoTracking().SingleAsync(x => x.LocationId == row.LocationId && x.Action == "Approved");
        var notification = (await EventsAsync(db, row.LocationId, NotificationEventCodes.LocationApproved)).Single();
        Require(notification.BusinessEventKey == $"LOCATION:{row.LocationId}:APPROVED:HISTORY:{history.LocationApprovalHistoryId}", "history occurrence key");
        Require(notification.EventOccurredAt == await SqlMillisecondsAsync(db, history.ActionAt), "approval authoritative time");
        Require(notification.RecipientUserId == fx.Creator.UserId && notification.RecipientKey == $"EMP:{fx.CreatorEmploymentId}"
            && notification.RecipientUserId != fx.Reviewer.UserId, "initiator not reviewer; employment canonical key");
        await fx.Events(db).QueueApprovedAsync(row, history, true, default);
        await db.SaveChangesAsync();
        Require((await EventsAsync(db, row.LocationId, NotificationEventCodes.LocationApproved)).Count == 1, "same approval occurrence retry");
        await fx.Master(db).BatchPublishAsync(new BatchPublishLocationsRequest([row.LocationId]), default);
        Require((await EventsAsync(db, row.LocationId, NotificationEventCodes.LocationApproved)).Count == 1, "already Approved manual no event");
    }
    Pass("EA2B-09_MANUAL_APPROVAL_INITIAL_PROOF_HISTORY_ID_INITIATOR_RETRY");

    var background = await fx.Managed(db).CreateManagedLocationAsync(fx.Request("EA2B-background"), default);
    (await db.Locations.SingleAsync(x => x.LocationId == background.LocationId)).GeocodingStatus = "Pending";
    await db.SaveChangesAsync();
    await fx.RunBackgroundAsync(db, background.LocationId);
    var approved = (await EventsAsync(db, background.LocationId, NotificationEventCodes.LocationApproved)).Single();
    Require(approved.RecipientUserId == fx.Creator.UserId && approved.RecipientUserId != fx.Reviewer.UserId, "not background requester");
    var action = await db.LocationApprovalHistories.AsNoTracking().SingleAsync(x => x.LocationId == background.LocationId);
    Require(approved.EventOccurredAt == await SqlMillisecondsAsync(db, action.ActionAt), "background action time");
    (await db.Locations.SingleAsync(x => x.LocationId == background.LocationId)).GeocodingStatus = "Pending";
    await db.SaveChangesAsync();
    await fx.RunBackgroundAsync(db, background.LocationId);
    Require(await db.LocationApprovalHistories.CountAsync(x => x.LocationId == background.LocationId) == 2, "actual already-Approved reprocessing occurred");
    Require((await EventsAsync(db, background.LocationId, NotificationEventCodes.LocationApproved)).Count == 1, "already Approved background reprocess no event");
    Pass("EA2B-10_BACKGROUND_INITIAL_APPROVAL_REPROCESS");

    var imported = await fx.ItemAsync(db, "Create", fx.Source("EA2B-import-background"));
    await fx.Imports(db).ConfirmAsync(fx.Creator, imported.ImportBatchId, default);
    using var applied = JsonDocument.Parse(imported.DataJson);
    var importedId = applied.RootElement.GetProperty("appliedMutation").GetProperty("locationId").GetInt32();
    await fx.RunBackgroundAsync(db, importedId);
    Require((await EventsAsync(db, importedId, NotificationEventCodes.LocationApproved)).Single().RecipientUserId == fx.Creator.UserId,
        "import CREATE proof also supports background approval");
    Pass("EA2B-14_IMPORT_CREATED_BACKGROUND_INITIAL_APPROVAL");
}

static async Task RunNegativeCyclesAsync(Fixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    var ambiguous = await fx.LegacyLocationAsync(db, "EA2B-legacy-ambiguous", "Pending");
    await fx.Master(db).BatchPublishAsync(new BatchPublishLocationsRequest([ambiguous.LocationId]), default);
    Require((await EventsAsync(db, ambiguous.LocationId, NotificationEventCodes.LocationApproved)).Count == 0, "absence of history is not initial proof");
    var legacy = await fx.LegacyLocationAsync(db, "EA2B-legacy-rereview", "Approved");
    var item = await fx.ItemAsync(db, "Update", fx.Source("EA2B-legacy-rereview-applied", legacy.LocationCode));
    await fx.Imports(db).ConfirmAsync(fx.Creator, item.ImportBatchId, default);
    await fx.RunBackgroundAsync(db, legacy.LocationId);
    Require((await EventsAsync(db, legacy.LocationId, NotificationEventCodes.LocationApproved)).Count == 0, "legacy re-review suppressed");
    // New proof and suppression are monotonic, not chosen by timestamps, audit order or source LocationCode.
    var marked = await fx.Managed(db).CreateManagedLocationAsync(fx.Request("EA2B-negative-authority"), default);
    var target = await db.Locations.SingleAsync(x => x.LocationId == marked.LocationId);
    target.ApprovalStatus = "Approved"; await db.SaveChangesAsync();
    var suppression = await fx.ItemAsync(db, "Update", fx.Source("EA2B-negative-authority-rereview", target.LocationCode));
    (await db.ImportBatches.SingleAsync(x => x.ImportBatchId == suppression.ImportBatchId)).RequestedByUserId = fx.Reviewer.UserId;
    await db.SaveChangesAsync();
    await fx.Imports(db).ConfirmAsync(fx.Reviewer, suppression.ImportBatchId, default);
    Require(target.CreatedByUserId == fx.Creator.UserId, "later importer does not replace original CreatedBy authority");
    suppression.CreatedAt = new DateTime(2000, 1, 1); // Suppression is not chosen by a latest timestamp.
    target.LocationCode = "EA2B-code-changed";
    await fx.Events(db).MarkInitialCycleAsync(target, default); // Even an additional positive marker cannot override suppression.
    await db.SaveChangesAsync();
    await fx.Master(db).BatchPublishAsync(new BatchPublishLocationsRequest([target.LocationId]), default);
    Require((await EventsAsync(db, target.LocationId, NotificationEventCodes.LocationApproved)).Count == 0, "explicit LocationId suppression wins over markers/source code");
    // Ordinary geocoding/address edits enter no approval review cycle.
    var geo = await fx.LegacyLocationAsync(db, "EA2B-geocoding-only", "Approved");
    await fx.Master(db).UpdateLocationAsync(geo.LocationId,
        new UpdateLocationRequest(geo.LocationName, null, null, "changed address", null, Convert.ToBase64String(geo.RowVersion)), default);
    Require((await EventsAsync(db, geo.LocationId, NotificationEventCodes.LocationReviewRequested)).Count == 0, "geocoding only no ReviewRequested");
    await fx.RunBackgroundAsync(db, geo.LocationId);
    Require((await EventsAsync(db, geo.LocationId, NotificationEventCodes.LocationApproved)).Count == 0, "geocoding reprocess not approval cycle");
    Require(!await db.Set<MailOutbox>().AnyAsync(x => x.EventCode == NotificationEventCodes.LocationReturned), "Returned dormant");
    Pass("EA2B-11_LEGACY_AMBIGUOUS_REREVIEW_NEGATIVE_AUTHORITY_DORMANT");
}

static async Task RunRollbackAsync(Fixture fx, Func<AppDbContext> newDb)
{
    await using (var db = newDb())
    {
        var writer = new FailingWriter(fx.Writer(db), "EA2B-managed-rollback");
        await ExpectFailureAsync(() => fx.Managed(db, writer).CreateManagedLocationAsync(fx.Request("EA2B-managed-rollback"), default), "managed rollback");
        await using var check = newDb();
        Require(!await check.Locations.AnyAsync(x => x.LocationName == "EA2B-managed-rollback"), "managed Location rollback");
        Require(!await check.AuditLogs.AnyAsync(x => x.EntityType == "Location" && x.EntityId == writer.Attempt!.AggregateId), "marker rollback");
        Require(!await check.Set<MailOutbox>().AnyAsync(x => x.BusinessEventKey == writer.Attempt!.BusinessEventKey), "managed outbox rollback");
    }
    await using (var db = newDb())
    {
        var writer = new FailingWriter(fx.Writer(db), "EA2B-trip-rollback");
        await ExpectFailureAsync(() => fx.Trips(db, writer).CreateAsync(fx.TripRequest("EA2B-trip-rollback", new DateOnly(2026, 8, 3)), default), "Trip temp rollback");
        await using var check = newDb();
        Require(!await check.Locations.AnyAsync(x => x.LocationName == "EA2B-trip-rollback")
            && !await check.VisitTrips.AnyAsync(x => x.VisitDate == new DateOnly(2026, 8, 3)), "Trip and temp rollback");
        Require(!await check.Set<MailOutbox>().AnyAsync(x => x.BusinessEventKey == writer.Attempt!.BusinessEventKey), "Trip outbox rollback");
    }
    foreach (var background in new[] { false, true })
    {
        await using var db = newDb();
        var name = background ? "EA2B-background-rollback" : "EA2B-approval-rollback";
        var created = await fx.Managed(db).CreateManagedLocationAsync(fx.Request(name), default);
        var writer = new FailingWriter(fx.Writer(db), name);
        if (background) await fx.RunBackgroundAsync(db, created.LocationId, writer);
        else await ExpectFailureAsync(() => fx.Master(db, writer).BatchPublishAsync(new BatchPublishLocationsRequest([created.LocationId]), default), "approval rollback");
        await using var check = newDb();
        Require((await check.Locations.SingleAsync(x => x.LocationId == created.LocationId)).ApprovalStatus == "Pending", "approval status rollback");
        Require(!await check.LocationApprovalHistories.AnyAsync(x => x.LocationId == created.LocationId), "approval history rollback");
        Require((await EventsAsync(check, created.LocationId, NotificationEventCodes.LocationApproved)).Count == 0, "approval outbox rollback");
        if (background) Require(await check.BackgroundJobItems.AnyAsync(x => x.EntityId == created.LocationId.ToString() && x.Status == "Failed"), "background failure evidence only");
    }
    Pass("EA2B-12_CREATE_AND_APPROVAL_ROLLBACK_ATOMICITY");
}

static async Task RunSettingsAndIdentityAsync(Fixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    var setting = await db.Set<NotificationSetting>().SingleAsync(x => x.EventCode == NotificationEventCodes.LocationReviewRequested);
    setting.IsEnabled = false; await db.SaveChangesAsync();
    var managed = await fx.Managed(db).CreateManagedLocationAsync(fx.Request("EA2B-disabled-marker"), default);
    await RequireMarkerAsync(db, managed.LocationId);
    var item = await fx.ItemAsync(db, "Create", fx.Source("EA2B-disabled-import"));
    await fx.Imports(db).ConfirmAsync(fx.Creator, item.ImportBatchId, default);
    using var envelope = JsonDocument.Parse(item.DataJson);
    var id = envelope.RootElement.GetProperty("appliedMutation").GetProperty("locationId").GetInt32();
    Require((await EventsAsync(db, id, NotificationEventCodes.LocationReviewRequested)).Count == 0
        && (await EventsAsync(db, managed.LocationId, NotificationEventCodes.LocationReviewRequested)).Count == 0, "disabled no Outbox");
    await fx.Master(db).BatchPublishAsync(new BatchPublishLocationsRequest([id, managed.LocationId]), default);
    Require((await EventsAsync(db, id, NotificationEventCodes.LocationApproved)).Count == 1
        && (await EventsAsync(db, managed.LocationId, NotificationEventCodes.LocationApproved)).Count == 1, "positive proof independent of Outbox");
    setting.IsEnabled = true; await db.SaveChangesAsync();
    var approvedSetting = await db.Set<NotificationSetting>().SingleAsync(x => x.EventCode == NotificationEventCodes.LocationApproved);
    approvedSetting.IsEnabled = false; await db.SaveChangesAsync();
    var disabledApproval = await fx.Managed(db).CreateManagedLocationAsync(fx.Request("EA2B-disabled-approval"), default);
    await fx.Master(db).BatchPublishAsync(new BatchPublishLocationsRequest([disabledApproval.LocationId]), default);
    Require((await EventsAsync(db, disabledApproval.LocationId, NotificationEventCodes.LocationApproved)).Count == 0
        && await db.Locations.AnyAsync(x => x.LocationId == disabledApproval.LocationId && x.ApprovalStatus == "Approved"), "disabled approval commits business only");
    approvedSetting.IsEnabled = true; await db.SaveChangesAsync();
    var unusable = await fx.Managed(db).CreateManagedLocationAsync(fx.Request("EA2B-unusable"), default);
    var employment = await db.Employments.SingleAsync(x => x.EmploymentId == fx.CreatorEmploymentId);
    var user = await db.Users.SingleAsync(x => x.UserId == fx.Creator.UserId);
    employment.Email = null; user.Email = ""; await db.SaveChangesAsync();
    await fx.Master(db).BatchPublishAsync(new BatchPublishLocationsRequest([unusable.LocationId]), default);
    var failed = (await EventsAsync(db, unusable.LocationId, NotificationEventCodes.LocationApproved)).Single();
    Require(failed.Status == "Failed" && failed.RecipientKey == $"EMP:{fx.CreatorEmploymentId}", "unusable recipient terminal evidence");
    Pass("EA2B-13_SETTINGS_OPTIONAL_PREFERENCE_UNUSABLE_EMAIL_EVENT_TIME");
}

sealed class Fixture
{
    public required int OrganizationId { get; init; }
    public required int TeamId { get; init; }
    public required long CreatorEmploymentId { get; init; }
    public required CurrentUserDto Creator { get; init; }
    public required CurrentUserDto Reviewer { get; init; }
    public static async Task<Fixture> SeedAsync(Func<AppDbContext> newDb)
    {
        await using var db = newDb();
        var now = DateTime.UtcNow;
        var org = new Organization { OrganizationCode = "EA2B", OrganizationName = "EA2B", IsActive = true, CreatedAt = now };
        db.Organizations.Add(org); await db.SaveChangesAsync();
        var team = new Team { OrganizationId = org.OrganizationId, TeamCode = "EA2B-T", TeamName = "EA2B", IsActive = true, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now };
        db.Teams.Add(team); await db.SaveChangesAsync();
        var creator = new User { OrganizationId = org.OrganizationId, TeamId = team.TeamId, EmployeeNo = "EA2B-C", DisplayName = "Creator", Email = "creator@example.invalid", IsActive = true, CreatedAt = now };
        var reviewer = new User { OrganizationId = org.OrganizationId, TeamId = team.TeamId, EmployeeNo = "EA2B-R", DisplayName = "Reviewer", Email = "reviewer@example.invalid", IsActive = true, CreatedAt = now };
        db.Users.AddRange(creator, reviewer);
        var p1 = new Person { DisplayName = "Creator", CreatedAt = now }; var p2 = new Person { DisplayName = "Reviewer", CreatedAt = now };
        db.Persons.AddRange(p1, p2); await db.SaveChangesAsync();
        var e1 = new Employment { PersonId = p1.PersonId, OrganizationId = org.OrganizationId, EmployeeNo = creator.EmployeeNo, Email = creator.Email, LegacyUserId = creator.UserId, SourceType = "EA2B", HireDate = new DateOnly(2020, 1, 1), OptionalEmailNotificationEnabled = false, CreatedAt = now };
        var e2 = new Employment { PersonId = p2.PersonId, OrganizationId = org.OrganizationId, EmployeeNo = reviewer.EmployeeNo, Email = reviewer.Email, LegacyUserId = reviewer.UserId, SourceType = "EA2B", HireDate = new DateOnly(2020, 1, 1), OptionalEmailNotificationEnabled = false, CreatedAt = now };
        db.Employments.AddRange(e1, e2); await db.SaveChangesAsync();
        db.TeamLeaderAssignments.Add(new TeamLeaderAssignment { TeamId = team.TeamId, EmploymentId = e2.EmploymentId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now });
        var visitorRole = new Role { RoleCode = "visitor", RoleName = "Visitor", IsActive = true, CreatedAt = now };
        var adminRole = new Role { RoleCode = "admin", RoleName = "Admin", IsActive = true, CreatedAt = now };
        db.Roles.AddRange(visitorRole, adminRole); await db.SaveChangesAsync();
        db.UserRoles.AddRange(new UserRole { UserId = creator.UserId, RoleId = visitorRole.RoleId, AssignedAt = now }, new UserRole { UserId = reviewer.UserId, RoleId = adminRole.RoleId, AssignedAt = now });
        await db.SaveChangesAsync();
        return new Fixture { OrganizationId = org.OrganizationId, TeamId = team.TeamId, CreatorEmploymentId = e1.EmploymentId,
            Creator = new(creator.UserId, creator.EmployeeNo, creator.DisplayName, creator.Email, org.OrganizationId, team.TeamId, team.TeamName, ["admin", "visitor"]),
            Reviewer = new(reviewer.UserId, reviewer.EmployeeNo, reviewer.DisplayName, reviewer.Email, org.OrganizationId, team.TeamId, team.TeamName, ["admin"]) };
    }
    public INotificationOutboxWriter Writer(AppDbContext db) => new EfNotificationOutboxWriter(db, new EfNotificationRecipientResolver(db), new NotificationRuntimeEnvironment("UAT"), TimeProvider.System);
    public ILocationNotificationEvents Events(AppDbContext db, INotificationOutboxWriter? writer = null) => new EfLocationNotificationEvents(db, writer ?? Writer(db));
    public V160FinalService Managed(AppDbContext db, INotificationOutboxWriter? writer = null) =>
        new(new FixedUser(Creator), new V160FinalRepository(db, null!, null!, null!, null, new EfNotificationCollisionTranslator(db), Events(db, writer)),
            null!, null!, null!, null!, null!, null!, new TripRepository(db), new EfTransactionBoundary(db), new EfLocationMutationBoundary(db));
    public MasterService Master(AppDbContext db, INotificationOutboxWriter? writer = null) =>
        new(new FixedUser(Reviewer), new MasterRepository(db), new MileageRepository(db), new SuccessfulGeocoding(), new WorkflowRepository(db), null!, db,
            new EfTransactionBoundary(db), Events(db, writer), new EfNotificationCollisionTranslator(db), new EfLocationMutationBoundary(db));
    public WorkbookImportService Imports(AppDbContext db, INotificationOutboxWriter? writer = null) => new(db, Events(db, writer), new EfNotificationCollisionTranslator(db));
    public TripService Trips(AppDbContext db, INotificationOutboxWriter? writer = null) =>
        new(new FixedUser(Creator), new UserRepository(db), new TripRepository(db), new MasterRepository(db), new MileageRepository(db), new WorkflowRepository(db), null!,
            new FixtureTripContext(TeamId, CreatorEmploymentId), new TripSnapshotRepository(db), db, new EfTransactionBoundary(db), null,
            new EfNotificationCollisionTranslator(db), Events(db, writer), new EfLocationMutationBoundary(db));
    public SaveManagedLocationRequest Request(string name) => new(TeamId, name, "Customer", null, null, "fixture address", null, false, null);
    public SaveTripRequest TripRequest(string name, DateOnly date) => new(date, new TimeOnly(9, 0), new TimeOnly(10, 0), 10m, "EA2B", null, false,
        [new TripStopInput(null, null, null, "Temporary", name, "fixture address", null, null)], TeamId);
    public string Source(string name, string? code = null) => JsonSerializer.Serialize(new
        { locationCode = code, teamCode = "EA2B-T", locationName = name, city = "City", district = "District", address = "fixture address", plusCode = (string?)null, status = "Active", unknownSourceField = new { value = 123 } }, new JsonSerializerOptions { WriteIndented = true });
    public async Task<ImportBatchItem> ItemAsync(AppDbContext db, string action, string source)
    {
        var batch = new ImportBatch { ImportBatchId = Guid.NewGuid(), OrganizationId = OrganizationId, RequestedByUserId = Creator.UserId, ImportType = "locations", TotalCount = 1, ValidCount = 1, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        var item = new ImportBatchItem { ImportBatchId = batch.ImportBatchId, RowNumber = 1, EntityType = "Location", Action = action, DataJson = source, DisplayKey = "EA2B", CreatedAt = DateTime.UtcNow };
        db.ImportBatches.Add(batch); db.ImportBatchItems.Add(item); await db.SaveChangesAsync(); return item;
    }
    public async Task<Location> LegacyLocationAsync(AppDbContext db, string name, string status)
    {
        var row = new Location { OrganizationId = OrganizationId, TeamId = TeamId, LocationCode = name, LocationName = name, Address = "fixture address",
            ApprovalStatus = status, GeocodingStatus = "Completed", CreatedByUserId = Creator.UserId, IsActive = status == "Approved", CreatedAt = DateTime.UtcNow };
        db.Locations.Add(row); await db.SaveChangesAsync(); return row;
    }
    public async Task RunBackgroundAsync(AppDbContext db, int id, INotificationOutboxWriter? writer = null)
    {
        var service = new BackgroundJobService(db, null!, new SuccessfulGeocoding(), Events(db, writer), new EfNotificationCollisionTranslator(db));
        await service.EnqueueGeocodingAsync(Reviewer, new CreateGeocodingJobRequest(LocationIds: [id]), default);
        if (!await service.ProcessNextAsync(default)) throw new InvalidOperationException("No queued geocoding job.");
    }
}
sealed class FixedUser(CurrentUserDto user) : ICurrentUserService { public CurrentUserDto GetRequired() => user; }
sealed class SuccessfulGeocoding : IGeocodingService
{
    public Task<GeocodingResult> ResolveAsync(string? address, string? plusCode, CancellationToken ct) => Task.FromResult(new GeocodingResult(true, 25m, 121m, null, null));
}
sealed class FixtureTripContext(int teamId, long employmentId) : IV180TripContextReader
{
    public Task<V180TripContextDto> ResolveAsync(CurrentUserDto user, DateOnly visitDate, int? requestedTeamId, CancellationToken ct) =>
        Task.FromResult(new V180TripContextDto(employmentId, visitDate, false, "FIXTURE", "draft without deployment sites", [], teamId, [], null, null, null));
}
sealed class FailingWriter(INotificationOutboxWriter inner, string reference) : INotificationOutboxWriter
{
    public NotificationEventContext? Attempt { get; private set; }
    public async Task<NotificationQueueResult> QueueAsync(NotificationEventContext context, CancellationToken ct)
    {
        var result = await inner.QueueAsync(context, ct);
        if (((NotificationTemplatePayloadV1)context.TemplatePayload).Reference == reference)
        {
            Attempt = context;
            throw new InvalidOperationException("EA2B_INJECT_AFTER_LOCATION_FLUSH_AND_OUTBOX_ADD");
        }
        return result;
    }
}
