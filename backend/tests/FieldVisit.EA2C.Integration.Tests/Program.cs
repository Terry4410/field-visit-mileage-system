using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

var connection = Environment.GetEnvironmentVariable("EA2C_SQL_CONNECTION")
    ?? throw new InvalidOperationException("EA2C_SQL_CONNECTION is required.");
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(connection, sql => sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null))
    .ReplaceService<IModelCustomizer, NotificationModelCustomizer>().Options;
AppDbContext NewDb() => new(options);
await Ea2aSchemaBootstrap.InitializeAsync(connection);
var fx = await Fixture.SeedAsync(NewDb);
Console.WriteLine("EA2C_REAL_SQL_DISPOSABLE=PASS");
await RunGenericConfirmedAsync(fx, NewDb);
await RunGenericPartialAsync(fx, NewDb);
await RunGenericRollbackAsync(fx, NewDb);
await RunGenericCollisionAsync(fx, NewDb);
await RunPeopleConfirmedAsync(fx, NewDb);
await RunPeoplePartialAsync(fx, NewDb);
await RunPeopleRollbackAsync(fx, NewDb);
await RunDormantAsync(NewDb);
Console.WriteLine("EA2C_REAL_SQL_REGRESSION=PASS");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("EA2C assertion failed: " + message);
}
static void Pass(string marker) => Console.WriteLine(marker + "=PASS");
static async Task ExpectFailureAsync(Func<Task> action, string message)
{
    try { await action(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("EA2C expected failure: " + message);
}
static async Task<DateTime> SqlMillisecondsAsync(AppDbContext db, DateTime time)
{
    var parameter = new SqlParameter("@time", System.Data.SqlDbType.DateTime2) { Scale = 7, Value = time };
    return await db.Database.SqlQueryRaw<DateTime>("SELECT CAST(@time AS datetime2(3)) AS [Value]", parameter).SingleAsync();
}
static Task<List<MailOutbox>> EventsAsync(AppDbContext db, Guid id) =>
    db.Set<MailOutbox>().AsNoTracking().Where(x => x.AggregateType == "ImportBatch"
        && x.AggregateId == id.ToString("D") && x.EventCode == NotificationEventCodes.ImportCompleted).ToListAsync();

static async Task RequireCompletedAsync(AppDbContext db, Fixture fx, Guid id, string status)
{
    var batch = await db.ImportBatches.AsNoTracking().SingleAsync(x => x.ImportBatchId == id);
    var queued = (await EventsAsync(db, id)).Single();
    Require(batch.Status == status && batch.ConfirmedAt.HasValue, "terminal batch state");
    Require(queued.BusinessEventKey == $"IMPORT:{id:D}:COMPLETED", "canonical business key");
    Require(queued.EventOccurredAt == await SqlMillisecondsAsync(db, batch.ConfirmedAt.Value), "authoritative ConfirmedAt");
    Require(queued.RecipientKey == $"EMP:{fx.InitiatorEmploymentId}"
        && queued.RecipientUserId == fx.Initiator.UserId
        && queued.RecipientEmail == fx.InitialEmail, "event-time Initiator identity");
}

static async Task RunGenericConfirmedAsync(Fixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    var batch = await fx.BatchAsync(db, "projects", ("Project", "NoChange", "{}"));
    await fx.Generic(db).ConfirmAsync(fx.Initiator, batch.ImportBatchId, default);
    await RequireCompletedAsync(db, fx, batch.ImportBatchId, "Confirmed");
    var employment = await db.Employments.SingleAsync(x => x.EmploymentId == fx.InitiatorEmploymentId);
    var user = await db.Users.SingleAsync(x => x.UserId == fx.Initiator.UserId);
    employment.Email = "changed-after-import@example.invalid";
    user.Email = "changed-after-import@example.invalid";
    await db.SaveChangesAsync();
    Require((await EventsAsync(db, batch.ImportBatchId)).Single().RecipientEmail == fx.InitialEmail,
        "recipient identity remains frozen after enqueue");
    employment.Email = fx.InitialEmail;
    user.Email = fx.InitialEmail;
    await db.SaveChangesAsync();
    await ExpectFailureAsync(() => fx.Generic(db).ConfirmAsync(fx.Initiator, batch.ImportBatchId, default), "terminal retry rejected");
    Require((await EventsAsync(db, batch.ImportBatchId)).Count == 1, "retry creates no duplicate");
    Pass("EA2C-01_GENERIC_CONFIRMED_EXACTLY_ONCE");
}

static async Task RunGenericPartialAsync(Fixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    var batch = await fx.BatchAsync(db, "projects", ("Project", "NoChange", "{}"), ("Unsupported", "Create", "{}"));
    var result = await fx.Generic(db).ConfirmAsync(fx.Initiator, batch.ImportBatchId, default);
    await RequireCompletedAsync(db, fx, batch.ImportBatchId, "PartiallyFailed");
    var items = await db.ImportBatchItems.AsNoTracking().Where(x => x.ImportBatchId == batch.ImportBatchId).OrderBy(x => x.RowNumber).ToListAsync();
    Require(result.Unchanged == 1 && result.Failed == 1 && items[0].Status == "Applied" && items[1].Status == "Failed",
        "prior item commit survives later failure");
    Pass("EA2C-02_GENERIC_PARTIALLY_FAILED_PRIOR_ITEM_COMMIT");
}

static async Task RunGenericRollbackAsync(Fixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    var batch = await fx.BatchAsync(db, "projects", ("Project", "NoChange", "{}"), ("Unsupported", "Create", "{}"));
    var failing = new FailingWriter(fx.Writer(db));
    await ExpectFailureAsync(() => fx.Generic(db, failing).ConfirmAsync(fx.Initiator, batch.ImportBatchId, default), "generic terminal notification failure");
    await using var check = newDb();
    var persisted = await check.ImportBatches.AsNoTracking().SingleAsync(x => x.ImportBatchId == batch.ImportBatchId);
    var items = await check.ImportBatchItems.AsNoTracking().Where(x => x.ImportBatchId == batch.ImportBatchId).OrderBy(x => x.RowNumber).ToListAsync();
    Require(persisted.Status == "Previewed" && persisted.ConfirmedAt == null, "generic terminal state rolled back");
    Require(items[0].Status == "Applied" && items[1].Status == "Failed", "generic per-item commits remain independent");
    Require(!await check.Set<MailOutbox>().AnyAsync(x => x.BusinessEventKey == failing.Attempt!.BusinessEventKey), "generic outbox rolled back");
    Require(!await check.AuditLogs.AnyAsync(x => x.EntityType == "ImportBatch"
        && x.EntityId == batch.ImportBatchId.ToString() && x.Action == "ImportConfirm"), "generic terminal audit rolled back");
    Pass("EA2C-03_GENERIC_FINALIZATION_OUTBOX_ROLLBACK");
}

static async Task RunGenericCollisionAsync(Fixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    var batch = await fx.BatchAsync(db, "projects", ("Project", "NoChange", "{}"));
    var competing = new CompetingWriter(fx.Writer(db), newDb, fx.Writer);
    await fx.Generic(db, competing).ConfirmAsync(fx.Initiator, batch.ImportBatchId, default);
    await using var check = newDb();
    await RequireCompletedAsync(check, fx, batch.ImportBatchId, "Confirmed");
    Require((await EventsAsync(check, batch.ImportBatchId)).Count == 1, "equivalent SQL collision remains exactly once");
    Require(await check.ImportBatchItems.AnyAsync(x => x.ImportBatchId == batch.ImportBatchId && x.Status == "Applied"),
        "collision does not corrupt prior item commit");
    Pass("EA2C-04_GENERIC_EQUIVALENT_COLLISION_COMMITS_TERMINAL_STATE");
}

static async Task RunPeopleConfirmedAsync(Fixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    var batch = await fx.BatchAsync(db, "people-authorization", ("InternalAuthorization", "NoChange", "{}"));
    await fx.People(db).ConfirmAsync(fx.Initiator, batch.ImportBatchId, new V170PeopleBulkConfirmRequest(), default);
    await RequireCompletedAsync(db, fx, batch.ImportBatchId, "Confirmed");
    await ExpectFailureAsync(() => fx.People(db).ConfirmAsync(fx.Initiator, batch.ImportBatchId, new V170PeopleBulkConfirmRequest(), default),
        "people terminal retry rejected");
    Require((await EventsAsync(db, batch.ImportBatchId)).Count == 1, "people retry creates no duplicate");
    Pass("EA2C-05_PEOPLE_CONFIRMED_EXACTLY_ONCE");
}

static async Task RunPeoplePartialAsync(Fixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    var batch = await fx.BatchAsync(db, "people-authorization",
        ("InternalAuthorization", "NoChange", "{}"), ("InternalAuthorization", "Update", MissingPersonJson(2)));
    var result = await fx.People(db).ConfirmAsync(fx.Initiator, batch.ImportBatchId, new V170PeopleBulkConfirmRequest(), default);
    await RequireCompletedAsync(db, fx, batch.ImportBatchId, "PartiallyFailed");
    var items = await db.ImportBatchItems.AsNoTracking().Where(x => x.ImportBatchId == batch.ImportBatchId).OrderBy(x => x.RowNumber).ToListAsync();
    Require(result.Unchanged == 1 && result.Failed == 1 && items[0].Status == "Applied" && items[1].Status == "Failed",
        "people prior item commit survives later failure");
    Pass("EA2C-06_PEOPLE_PARTIALLY_FAILED_PRIOR_ITEM_COMMIT");
}

static async Task RunPeopleRollbackAsync(Fixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    var batch = await fx.BatchAsync(db, "people-authorization",
        ("InternalAuthorization", "NoChange", "{}"), ("InternalAuthorization", "Update", MissingPersonJson(2)));
    var failing = new FailingWriter(fx.Writer(db));
    await ExpectFailureAsync(() => fx.People(db, failing).ConfirmAsync(fx.Initiator, batch.ImportBatchId,
        new V170PeopleBulkConfirmRequest(), default), "people terminal notification failure");
    await using var check = newDb();
    var persisted = await check.ImportBatches.AsNoTracking().SingleAsync(x => x.ImportBatchId == batch.ImportBatchId);
    var items = await check.ImportBatchItems.AsNoTracking().Where(x => x.ImportBatchId == batch.ImportBatchId).OrderBy(x => x.RowNumber).ToListAsync();
    Require(persisted.Status == "Confirming" && persisted.ConfirmedAt == null, "people terminal state rolled back to claimed state");
    Require(items[0].Status == "Applied" && items[1].Status == "Failed", "people per-item commits remain independent");
    Require(!await check.Set<MailOutbox>().AnyAsync(x => x.BusinessEventKey == failing.Attempt!.BusinessEventKey), "people outbox rolled back");
    var audits = await check.AuditLogs.AsNoTracking().Where(x => x.EntityType == "PeopleBulk" && x.Action == "PeopleBulkConfirm").ToListAsync();
    Require(!audits.Any(x => x.NewValues?.Contains(batch.ImportBatchId.ToString(), StringComparison.OrdinalIgnoreCase) == true),
        "people terminal audit rolled back");
    Pass("EA2C-07_PEOPLE_FINALIZATION_OUTBOX_ROLLBACK");
}

static async Task RunDormantAsync(Func<AppDbContext> newDb)
{
    await using var db = newDb();
    Require(!await db.Set<MailOutbox>().AnyAsync(x => x.EventCode == NotificationEventCodes.ImportFailed), "ImportFailed remains dormant");
    Pass("EA2C-08_IMPORT_FAILED_DORMANT_NO_WHOLE_BATCH_TRANSACTION");
}

static string MissingPersonJson(int rowNumber) => JsonSerializer.Serialize(
    new V170InternalAuthorizationRow(rowNumber, "EA2C-MISSING", null, null, null, null, false,
        ["visitor"], [], null, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), "Google", null, null));

sealed class Fixture
{
    public required CurrentUserDto Initiator { get; init; }
    public required long InitiatorEmploymentId { get; init; }
    public required string InitialEmail { get; init; }

    public static async Task<Fixture> SeedAsync(Func<AppDbContext> newDb)
    {
        await using var db = newDb();
        var now = DateTime.UtcNow;
        var org = new Organization { OrganizationCode = "EA2C", OrganizationName = "EA2C", IsActive = true, CreatedAt = now };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        const string email = "ea2c.initiator@example.invalid";
        var user = new User { OrganizationId = org.OrganizationId, EmployeeNo = "EA2C-I", DisplayName = "Import Initiator",
            Email = email, IsActive = true, CreatedAt = now };
        var person = new Person { DisplayName = user.DisplayName, CreatedAt = now };
        db.Users.Add(user); db.Persons.Add(person);
        await db.SaveChangesAsync();
        var employment = new Employment { PersonId = person.PersonId, OrganizationId = org.OrganizationId,
            EmployeeNo = user.EmployeeNo, Email = email, LegacyUserId = user.UserId, SourceType = "EA2C",
            HireDate = new DateOnly(2020, 1, 1), OptionalEmailNotificationEnabled = false, CreatedAt = now };
        db.Employments.Add(employment);
        await db.SaveChangesAsync();
        return new Fixture
        {
            Initiator = new CurrentUserDto(user.UserId, user.EmployeeNo, user.DisplayName, email,
                org.OrganizationId, null, null, ["admin"]),
            InitiatorEmploymentId = employment.EmploymentId,
            InitialEmail = email
        };
    }

    public INotificationOutboxWriter Writer(AppDbContext db) => new EfNotificationOutboxWriter(db,
        new EfNotificationRecipientResolver(db), new NotificationRuntimeEnvironment("UAT"), TimeProvider.System);
    public IImportNotificationEvents Events(AppDbContext db, INotificationOutboxWriter? writer = null) =>
        new EfImportNotificationEvents(writer ?? Writer(db));
    public WorkbookImportService Generic(AppDbContext db, INotificationOutboxWriter? writer = null) =>
        new(db, null, new EfNotificationCollisionTranslator(db), Events(db, writer));
    public V170PeopleBulkWorkbookService People(AppDbContext db, INotificationOutboxWriter? writer = null) =>
        new(db, new NoopPeopleWriter(), Events(db, writer), new EfNotificationCollisionTranslator(db));

    public async Task<ImportBatch> BatchAsync(AppDbContext db, string importType,
        params (string EntityType, string Action, string DataJson)[] rows)
    {
        var batch = new ImportBatch
        {
            ImportBatchId = Guid.NewGuid(), ImportType = importType,
            OrganizationId = Initiator.OrganizationId!.Value, RequestedByUserId = Initiator.UserId,
            TotalCount = rows.Length, ValidCount = rows.Length, CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        db.ImportBatches.Add(batch);
        for (var index = 0; index < rows.Length; index++)
            db.ImportBatchItems.Add(new ImportBatchItem
            {
                ImportBatchId = batch.ImportBatchId, RowNumber = index + 1, EntityType = rows[index].EntityType,
                Action = rows[index].Action, Status = "Valid", DisplayKey = $"EA2C-{index + 1}",
                DataJson = rows[index].DataJson, CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        return batch;
    }
}

sealed class NoopPeopleWriter : IV170PeopleAdminWriter
{
    public Task<int> CreateExternalSupervisorAsync(CurrentUserDto admin, SaveExternalSupervisorRequest request, CancellationToken ct) =>
        throw new InvalidOperationException("EA2C fixture does not invoke people writes.");
    public Task UpdateExternalSupervisorAsync(CurrentUserDto admin, int userId, UpdateExternalSupervisorRequest request, CancellationToken ct) =>
        throw new InvalidOperationException("EA2C fixture does not invoke people writes.");
    public Task UpdateInternalUserAccessAsync(CurrentUserDto admin, int userId, UpdateInternalUserAccessRequest request, CancellationToken ct) =>
        throw new InvalidOperationException("EA2C fixture does not invoke people writes.");
}

sealed class FailingWriter(INotificationOutboxWriter inner) : INotificationOutboxWriter
{
    public NotificationEventContext? Attempt { get; private set; }
    public async Task<NotificationQueueResult> QueueAsync(NotificationEventContext context, CancellationToken ct)
    {
        var result = await inner.QueueAsync(context, ct);
        if (context.EventCode == NotificationEventCodes.ImportCompleted)
        {
            Attempt = context;
            throw new InvalidOperationException("EA2C_INJECT_AFTER_TERMINAL_MUTATION_AND_OUTBOX_ADD");
        }
        return result;
    }
}

sealed class CompetingWriter(
    INotificationOutboxWriter inner,
    Func<AppDbContext> newDb,
    Func<AppDbContext, INotificationOutboxWriter> writerFactory) : INotificationOutboxWriter
{
    public async Task<NotificationQueueResult> QueueAsync(NotificationEventContext context, CancellationToken ct)
    {
        var result = await inner.QueueAsync(context, ct);
        await using var competitor = newDb();
        await writerFactory(competitor).QueueAsync(context, ct);
        await competitor.SaveChangesAsync(ct);
        return result;
    }
}
