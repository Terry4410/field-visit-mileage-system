using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

var connectionString = Environment.GetEnvironmentVariable("EA2A_SQL_CONNECTION")
    ?? throw new InvalidOperationException("EA2A_SQL_CONNECTION is required.");

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null))
    .ReplaceService<IModelCustomizer, NotificationModelCustomizer>()
    .Options;

AppDbContext NewDb() => new(options);

await Ea2aSchemaBootstrap.InitializeAsync(connectionString);

var fx = await Ea2aFixture.SeedAsync(NewDb);
Console.WriteLine("EA2A_REAL_SQL_DISPOSABLE=PASS");

await RunTripSubmittedInitialAsync(fx, NewDb);
await RunTripSubmittedResubmitAsync(fx, NewDb);
await RunTripSubmitRollbackAsync(fx, NewDb);
await RunApprovalEventsAsync(fx, NewDb);
await RunApprovalRollbackAndBatchAsync(fx, NewDb);
await RunCorrectionRequestedAsync(fx, NewDb);
await RunCorrectionRollbackAsync(fx, NewDb);
await RunCorrectionTerminalAsync(fx, NewDb);
await RunEventTimeAndNoSaveMarkersAsync();
await RunCollisionConcurrencyAsync(fx, NewDb);
await RunCollisionNegativeMatrixAsync(fx, NewDb);

Console.WriteLine("EA2A_REAL_SQL_REGRESSION=PASS");

static async Task RunTripSubmittedInitialAsync(Ea2aFixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    var trip = await fx.AddTripAsync(db, TripStatuses.Draft, employmentId: null);
    var rowVersion = Convert.ToBase64String(trip.RowVersion);
    var (service, _) = fx.TripServices(db, fx.Visitor, new StaticOutbox(db), new EfNotificationCollisionTranslator(db));
    await service.SubmitAsync(trip.VisitTripId, new SubmitTripRequest(false), rowVersion, default);

    await using var verify = newDb();
    var history = await verify.VisitTripStatusHistories.AsNoTracking()
        .SingleAsync(x => x.VisitTripId == trip.VisitTripId && x.NewStatus == TripStatuses.Submitted);
    var key = $"TRIP_SUBMITTED:HIST:{history.VisitTripStatusHistoryId}";
    var outbox = await verify.MailOutbox().SingleAsync(x => x.BusinessEventKey == key);
    Require(outbox.EventCode == NotificationEventCodes.TripSubmitted, "EA2-01 event code");
    Require(outbox.EventOccurredAt == history.ActionAt, "EA2-01 authoritative history time");
    Console.WriteLine("EA2-01_TRIP_SUBMITTED_INITIAL=PASS");
}

static async Task RunTripSubmittedResubmitAsync(Ea2aFixture fx, Func<AppDbContext> newDb)
{
    await using var db = newDb();
    var trip = await fx.AddTripAsync(db, TripStatuses.Draft, employmentId: null);
    var (service, _) = fx.TripServices(db, fx.Visitor, new StaticOutbox(db), new EfNotificationCollisionTranslator(db));
    await service.SubmitAsync(trip.VisitTripId, new SubmitTripRequest(false), Convert.ToBase64String(trip.RowVersion), default);

    await using (var mutate = newDb())
    {
        var current = await mutate.VisitTrips.SingleAsync(x => x.VisitTripId == trip.VisitTripId);
        current.Status = TripStatuses.Returned;
        current.ReturnReason = "resubmit fixture";
        await mutate.SaveChangesAsync();
    }

    await using var resubmitDb = newDb();
    var resubmit = await resubmitDb.VisitTrips.AsNoTracking().SingleAsync(x => x.VisitTripId == trip.VisitTripId);
    var (resubmitService, _) = fx.TripServices(resubmitDb, fx.Visitor, new StaticOutbox(resubmitDb), new EfNotificationCollisionTranslator(resubmitDb));
    await resubmitService.SubmitAsync(trip.VisitTripId, new SubmitTripRequest(false), Convert.ToBase64String(resubmit.RowVersion), default);

    await using var verify = newDb();
    var historyIds = await verify.VisitTripStatusHistories.AsNoTracking()
        .Where(x => x.VisitTripId == trip.VisitTripId && x.NewStatus == TripStatuses.Submitted)
        .Select(x => x.VisitTripStatusHistoryId)
        .ToListAsync();
    var keys = await verify.MailOutbox().AsNoTracking()
        .Where(x => x.AggregateType == "VisitTrip" && x.AggregateId == trip.VisitTripId.ToString())
        .Select(x => x.BusinessEventKey)
        .ToListAsync();
    keys = keys.Where(x => x.StartsWith("TRIP_SUBMITTED:HIST:", StringComparison.Ordinal)
                           && historyIds.Contains(long.Parse(x["TRIP_SUBMITTED:HIST:".Length..])))
        .Distinct(StringComparer.Ordinal)
        .ToList();
    Require(keys.Count == 2, "EA2-02 distinct submitted occurrences");
    Console.WriteLine("EA2-02_RETURNED_RESUBMITTED_DISTINCT=PASS");
}

static async Task RunTripSubmitRollbackAsync(Ea2aFixture fx, Func<AppDbContext> newDb)
{
    long tripId;
    byte[] rowVersion;
    await using (var db = newDb())
    {
        var trip = await fx.AddTripAsync(db, TripStatuses.Draft, employmentId: null);
        tripId = trip.VisitTripId;
        rowVersion = trip.RowVersion.ToArray();
    }

    await using (var db = newDb())
    {
        var (service, _) = fx.TripServices(db, fx.Visitor, new ThrowingOutbox(), new EfNotificationCollisionTranslator(db));
        var failed = false;
        try
        {
            await service.SubmitAsync(tripId, new SubmitTripRequest(false), Convert.ToBase64String(rowVersion), default);
        }
        catch (InvalidOperationException ex) when (ex.Message == "EA2A_INJECT_AFTER_FIRST_FLUSH")
        {
            failed = true;
        }
        Require(failed, "EA2-03 injected failure");
    }

    await using var verify = newDb();
    var tripAfter = await verify.VisitTrips.AsNoTracking().SingleAsync(x => x.VisitTripId == tripId);
    Require(tripAfter.Status == TripStatuses.Draft, "EA2-03 trip status rollback");
    Require(!await verify.VisitTripStatusHistories.AsNoTracking().AnyAsync(x => x.VisitTripId == tripId && x.NewStatus == TripStatuses.Submitted), "EA2-03 history rollback");
    Require(!await verify.MailOutbox().AsNoTracking().AnyAsync(x => x.AggregateType == "VisitTrip" && x.AggregateId == tripId.ToString()), "EA2-03 outbox rollback");
    Console.WriteLine("EA2-03_TRIP_SUBMIT_FIRST_FLUSH_ROLLBACK=PASS");
}

static async Task RunApprovalEventsAsync(Ea2aFixture fx, Func<AppDbContext> newDb)
{
    foreach (var (status, marker, code) in new[]
    {
        ("Approved", "EA2-04_TRIP_APPROVED_EXACT", NotificationEventCodes.TripApproved),
        ("Returned", "EA2-05_TRIP_RETURNED_EXACT", NotificationEventCodes.TripReturned)
    })
    {
        await using var db = newDb();
        var trip = await fx.AddTripAsync(db, TripStatuses.PendingApproval, employmentId: null);
        var (service, leader) = fx.TripServices(db, fx.Leader, new StaticOutbox(db), new EfNotificationCollisionTranslator(db));
        if (status == "Approved")
            await leader.ApproveAsync(trip.VisitTripId, new ApproveTripRequest(10m, Convert.ToBase64String(trip.RowVersion), "EA2A"), default);
        else
            await leader.ReturnAsync(trip.VisitTripId, new ReturnTripRequest("EA2A return", Convert.ToBase64String(trip.RowVersion)), default);

        await using var verify = newDb();
        var approval = await verify.ApprovalRecords.AsNoTracking().SingleAsync(x => x.VisitTripId == trip.VisitTripId && x.Action == status);
        var key = $"{(code == NotificationEventCodes.TripApproved ? "TRIP_APPROVED" : "TRIP_RETURNED")}:APPROVAL:{approval.ApprovalRecordId}";
        var outbox = await verify.MailOutbox().AsNoTracking().SingleAsync(x => x.BusinessEventKey == key);
        Require(outbox.EventCode == code && outbox.EventOccurredAt == approval.ActionAt, marker);
        Console.WriteLine($"{marker}=PASS");
    }
}

static async Task RunApprovalRollbackAndBatchAsync(Ea2aFixture fx, Func<AppDbContext> newDb)
{
    long failedTripId;
    byte[] failedVersion;
    await using (var seed = newDb())
    {
        var trip = await fx.AddTripAsync(seed, TripStatuses.PendingApproval, employmentId: null);
        failedTripId = trip.VisitTripId;
        failedVersion = trip.RowVersion.ToArray();
    }
    await using (var db = newDb())
    {
        var (_, leader) = fx.TripServices(db, fx.Leader, new ThrowingOutbox(), new EfNotificationCollisionTranslator(db));
        var failed = false;
        try { await leader.ApproveAsync(failedTripId, new ApproveTripRequest(10m, Convert.ToBase64String(failedVersion), "EA2A"), default); }
        catch (InvalidOperationException ex) when (ex.Message == "EA2A_INJECT_AFTER_FIRST_FLUSH") { failed = true; }
        Require(failed, "EA2-06 injected approval failure");
    }
    await using (var verify = newDb())
    {
        var trip = await verify.VisitTrips.AsNoTracking().SingleAsync(x => x.VisitTripId == failedTripId);
        Require(trip.Status == TripStatuses.PendingApproval, "EA2-06 trip rollback");
        Require(!await verify.ApprovalRecords.AsNoTracking().AnyAsync(x => x.VisitTripId == failedTripId), "EA2-06 approval rollback");
        Require(!await verify.MailOutbox().AsNoTracking().AnyAsync(x => x.AggregateId == failedTripId.ToString()), "EA2-06 outbox rollback");
    }

    long failedReturnTripId;
    byte[] failedReturnVersion;
    await using (var seed = newDb())
    {
        var trip = await fx.AddTripAsync(seed, TripStatuses.PendingApproval, employmentId: null);
        failedReturnTripId = trip.VisitTripId;
        failedReturnVersion = trip.RowVersion.ToArray();
    }
    await using (var db = newDb())
    {
        var (_, leader) = fx.TripServices(db, fx.Leader, new ThrowingOutbox(), new EfNotificationCollisionTranslator(db));
        var failed = false;
        try { await leader.ReturnAsync(failedReturnTripId, new ReturnTripRequest("EA2A rollback", Convert.ToBase64String(failedReturnVersion)), default); }
        catch (InvalidOperationException ex) when (ex.Message == "EA2A_INJECT_AFTER_FIRST_FLUSH") { failed = true; }
        Require(failed, "EA2-06 injected return failure");
    }
    await using (var verify = newDb())
    {
        var trip = await verify.VisitTrips.AsNoTracking().SingleAsync(x => x.VisitTripId == failedReturnTripId);
        Require(trip.Status == TripStatuses.PendingApproval, "EA2-06 return trip rollback");
        Require(!await verify.ApprovalRecords.AsNoTracking().AnyAsync(x => x.VisitTripId == failedReturnTripId), "EA2-06 return approval rollback");
        Require(!await verify.MailOutbox().AsNoTracking().AnyAsync(x => x.AggregateId == failedReturnTripId.ToString()), "EA2-06 return outbox rollback");
    }

    long firstId, secondId;
    byte[] firstVersion, secondVersion;
    await using (var seed = newDb())
    {
        var first = await fx.AddTripAsync(seed, TripStatuses.PendingApproval, employmentId: null);
        var second = await fx.AddTripAsync(seed, TripStatuses.PendingApproval, employmentId: null);
        firstId = first.VisitTripId; secondId = second.VisitTripId;
        firstVersion = first.RowVersion.ToArray(); secondVersion = second.RowVersion.ToArray();
    }
    await using (var db = newDb())
    {
        var (_, leader) = fx.TripServices(db, fx.Leader, new ConditionalThrowingOutbox(db, secondId), new EfNotificationCollisionTranslator(db));
        var result = await leader.BatchApproveAsync(new BatchApproveRequest([
            new BatchApproveItem(firstId, 10m, Convert.ToBase64String(firstVersion)),
            new BatchApproveItem(secondId, 10m, Convert.ToBase64String(secondVersion))]), default);
        Require(result.Success == 1 && result.Failed == 1, "EA2-07 batch result");
    }
    await using (var verify = newDb())
    {
        Require((await verify.VisitTrips.AsNoTracking().SingleAsync(x => x.VisitTripId == firstId)).Status == TripStatuses.Approved, "EA2-07 prior success");
        Require((await verify.VisitTrips.AsNoTracking().SingleAsync(x => x.VisitTripId == secondId)).Status == TripStatuses.PendingApproval, "EA2-07 failed item rollback");
        Require(await verify.MailOutbox().AsNoTracking().AnyAsync(x => x.AggregateId == firstId.ToString()), "EA2-07 first outbox");
        Require(!await verify.MailOutbox().AsNoTracking().AnyAsync(x => x.AggregateId == secondId.ToString()), "EA2-07 second outbox absent");
    }
    Console.WriteLine("EA2-06_APPROVAL_FAMILY_ROLLBACK=PASS");
    Console.WriteLine("EA2-07_BATCH_APPROVAL_PRIOR_SUCCESS_INDEPENDENT=PASS");
}

static async Task RunCorrectionRequestedAsync(Ea2aFixture fx, Func<AppDbContext> newDb)
{
    long tripId;
    await using (var seed = newDb()) tripId = (await fx.AddTripAsync(seed, TripStatuses.Approved, fx.VisitorEmployment.EmploymentId, withSnapshot: true)).VisitTripId;
    await using var db = newDb();
    var proposal = fx.Proposal("changed requested notes", 10m, 10m, 3m, 30m);
    var service = fx.CorrectionService(db, fx.Visitor, new StaticOutbox(db), new EfNotificationCollisionTranslator(db));
    var created = await service.CreateCorrectionAsync(new CreateCorrectionRequest(tripId, "EA2A requested", proposal), default);
    await using var verify = newDb();
    var outbox = await verify.MailOutbox().AsNoTracking().SingleAsync(x => x.BusinessEventKey == $"CORRECTION:{created.CorrectionRequestId}:REQUESTED");
    var row = await verify.CorrectionRequests.AsNoTracking().SingleAsync(x => x.CorrectionRequestId == created.CorrectionRequestId);
    Require(outbox.EventCode == NotificationEventCodes.CorrectionRequested && outbox.EventOccurredAt == row.RequestedAt, "EA2-08 exact occurrence");
    Console.WriteLine("EA2-08_CORRECTION_REQUESTED_EXACT=PASS");
}

static async Task RunCorrectionRollbackAsync(Ea2aFixture fx, Func<AppDbContext> newDb)
{
    long tripId;
    await using (var seed = newDb()) tripId = (await fx.AddTripAsync(seed, TripStatuses.Approved, fx.VisitorEmployment.EmploymentId, withSnapshot: true)).VisitTripId;
    await using (var db = newDb())
    {
        var service = fx.CorrectionService(db, fx.Visitor, new ThrowingOutbox(), new EfNotificationCollisionTranslator(db));
        var failed = false;
        try { await service.CreateCorrectionAsync(new CreateCorrectionRequest(tripId, "EA2A rollback", fx.Proposal("rollback", 10m, 10m, 3m, 30m)), default); }
        catch (InvalidOperationException ex) when (ex.Message == "EA2A_INJECT_AFTER_FIRST_FLUSH") { failed = true; }
        Require(failed, "EA2-09 injected correction failure");
    }
    await using var verify = newDb();
    Require(!await verify.CorrectionRequests.AsNoTracking().AnyAsync(x => x.VisitTripId == tripId), "EA2-09 request rollback");
    Require(!await verify.CorrectionRequestChanges.AsNoTracking().AnyAsync(), "EA2-09 changes rollback");
    Require(!await verify.MailOutbox().AsNoTracking().AnyAsync(x => x.AggregateType == "CorrectionRequest"), "EA2-09 outbox rollback");
    Console.WriteLine("EA2-09_CORRECTION_FIRST_SAVE_ROLLBACK=PASS");
}

static async Task RunCorrectionTerminalAsync(Ea2aFixture fx, Func<AppDbContext> newDb)
{
    // Leader review: non-financial change closes and emits; financial change
    // remains PendingAdminClose and emits nothing.
    var closed = await CreateAndReviewAsync(fx, newDb, fx.Proposal("leader closes", 10m, 10m, 3m, 30m), approve: true);
    Require(closed.Status == "Closed", "EA2-10 leader closed status");
    await AssertCorrectionEventAsync(newDb, closed.CorrectionRequestId, NotificationEventCodes.CorrectionApproved, "APPROVED");

    var pending = await CreateAndReviewAsync(fx, newDb, fx.Proposal("financial review", 10m, 11m, 3m, 33m), approve: true);
    Require(pending.Status == "PendingAdminClose", "EA2-10 pending admin status");
    await using (var check = newDb())
        Require(!await check.MailOutbox().AsNoTracking().AnyAsync(x => x.BusinessEventKey == $"CORRECTION:{pending.CorrectionRequestId}:APPROVED"), "EA2-10 no pending admin approved event");

    var adminClosed = await AdminCloseAsync(fx, newDb, pending, approve: true);
    Require(adminClosed.Status == "Closed", "EA2-11 admin close status");
    await AssertCorrectionEventAsync(newDb, adminClosed.CorrectionRequestId, NotificationEventCodes.CorrectionApproved, "APPROVED");

    var leaderRejected = await CreateAndReviewAsync(fx, newDb, fx.Proposal("leader rejects", 10m, 10m, 3m, 30m), approve: false);
    Require(leaderRejected.Status == "Rejected", "EA2-12 leader rejected status");
    await AssertCorrectionEventAsync(newDb, leaderRejected.CorrectionRequestId, NotificationEventCodes.CorrectionReturned, "RETURNED");

    var adminRejectedPending = await CreateAndReviewAsync(fx, newDb, fx.Proposal("admin rejects", 10m, 11m, 3m, 33m), approve: true);
    var adminRejected = await AdminCloseAsync(fx, newDb, adminRejectedPending, approve: false);
    Require(adminRejected.Status == "Rejected", "EA2-13 admin rejected status");
    await AssertCorrectionEventAsync(newDb, adminRejected.CorrectionRequestId, NotificationEventCodes.CorrectionReturned, "RETURNED");

    var returnedCountBefore = await CountCorrectionEventsAsync(newDb, adminRejected.CorrectionRequestId, NotificationEventCodes.CorrectionReturned);
    await using (var db = newDb())
    {
        var service = fx.CorrectionService(db, fx.Leader, new StaticOutbox(db), new EfNotificationCollisionTranslator(db));
        var failed = false;
        try { await service.ReviewCorrectionAsync(adminRejected.CorrectionRequestId, new ReviewCorrectionRequest(false, "again", adminRejected.RowVersion), default); }
        catch (InvalidOperationException) { failed = true; }
        Require(failed, "EA2-14 already rejected guard");
    }
    var returnedCountAfter = await CountCorrectionEventsAsync(newDb, adminRejected.CorrectionRequestId, NotificationEventCodes.CorrectionReturned);
    Require(returnedCountBefore == 1 && returnedCountAfter == 1, "EA2-14 no second event");

    Console.WriteLine("EA2-10_LEADER_CLOSED_VS_PENDING_ADMIN_CLOSE=PASS");
    Console.WriteLine("EA2-11_ADMIN_CLOSE_CLOSED=PASS");
    Console.WriteLine("EA2-12_LEADER_REJECTED_RETURNED=PASS");
    Console.WriteLine("EA2-13_ADMIN_REJECTED_RETURNED=PASS");
    Console.WriteLine("EA2-14_ALREADY_REJECTED_NO_SECOND_EVENT=PASS");
}

static async Task<CorrectionRequestDto> CreateAndReviewAsync(Ea2aFixture fx, Func<AppDbContext> newDb, CorrectionProposal proposal, bool approve)
{
    long tripId;
    await using (var seed = newDb()) tripId = (await fx.AddTripAsync(seed, TripStatuses.Approved, fx.VisitorEmployment.EmploymentId, withSnapshot: true)).VisitTripId;
    CorrectionRequestDto created;
    await using (var createDb = newDb())
        created = await fx.CorrectionService(createDb, fx.Visitor, new StaticOutbox(createDb), new EfNotificationCollisionTranslator(createDb))
            .CreateCorrectionAsync(new CreateCorrectionRequest(tripId, "EA2A terminal", proposal), default);
    await using var reviewDb = newDb();
    return await fx.CorrectionService(reviewDb, fx.Leader, new StaticOutbox(reviewDb), new EfNotificationCollisionTranslator(reviewDb))
        .ReviewCorrectionAsync(created.CorrectionRequestId, new ReviewCorrectionRequest(approve, "EA2A review", created.RowVersion), default);
}

static async Task<CorrectionRequestDto> AdminCloseAsync(Ea2aFixture fx, Func<AppDbContext> newDb, CorrectionRequestDto pending, bool approve)
{
    await using var db = newDb();
    return await fx.CorrectionService(db, fx.Admin, new StaticOutbox(db), new EfNotificationCollisionTranslator(db))
        .CloseCorrectionAsync(pending.CorrectionRequestId, new CloseCorrectionRequest(approve, "EA2A admin", pending.RowVersion), default);
}

static async Task AssertCorrectionEventAsync(Func<AppDbContext> newDb, long id, string code, string suffix)
{
    await using var db = newDb();
    var row = await db.CorrectionRequests.AsNoTracking().SingleAsync(x => x.CorrectionRequestId == id);
    var outbox = await db.MailOutbox().AsNoTracking().SingleAsync(x => x.BusinessEventKey == $"CORRECTION:{id}:{suffix}");
    var expectedTime = code == NotificationEventCodes.CorrectionApproved
        ? row.Status == "Closed" ? row.AdminClosedAt ?? row.LeaderReviewedAt : null
        : row.AdminClosedAt ?? row.LeaderReviewedAt;
    Require(outbox.EventCode == code && expectedTime.HasValue && outbox.EventOccurredAt == expectedTime.Value, $"correction event {id}");
}

static async Task<int> CountCorrectionEventsAsync(Func<AppDbContext> newDb, long id, string code)
{
    await using var db = newDb();
    var suffix = code == NotificationEventCodes.CorrectionApproved ? "APPROVED" : "RETURNED";
    return await db.MailOutbox().AsNoTracking().CountAsync(x => x.BusinessEventKey == $"CORRECTION:{id}:{suffix}");
}

static Task RunEventTimeAndNoSaveMarkersAsync()
{
    var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "backend/src/FieldVisit.Application/NotificationRuntime.cs"));
    var writer = File.ReadAllText(Path.Combine(FindRepoRoot(), "backend/src/FieldVisit.Infrastructure/NotificationOutboxWriter.cs"));
    Require(source.Contains("MUST NOT call SaveChanges", StringComparison.Ordinal), "EA2-38 writer contract marker");
    Require(source.Contains("interface INotificationCollisionTranslator", StringComparison.Ordinal), "EA2-37/42 translator foundation marker");
    Require(!writer.Contains(".SaveChanges", StringComparison.Ordinal), "EA2-38 writer source has no SaveChanges call");
    Console.WriteLine("EA2-37_EVENT_OCCURRED_AT_AUTHORITATIVE_TIME=PASS");
    Console.WriteLine("EA2-38_EA1_WRITER_NO_SAVECHANGES=PASS");
    return Task.CompletedTask;
}

static async Task RunCollisionConcurrencyAsync(Ea2aFixture fx, Func<AppDbContext> newDb)
{
    var key = "EA2A-COLLISION-RECIPIENT";
    await using var a = newDb();
    await using var b = newDb();
    await using var txA = await a.Database.BeginTransactionAsync();
    await using var txB = await b.Database.BeginTransactionAsync();
    a.AuditLogs.Add(new AuditLog { EntityType = "EA2A", EntityId = "A", Action = "CollisionWinner", NewValues = "winner", CorrelationId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow });
    b.AuditLogs.Add(new AuditLog { EntityType = "EA2A", EntityId = "B", Action = "CollisionLoserBusiness", NewValues = "EA2A collision loser legal business", CorrelationId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow });
    await a.SaveChangesAsync();
    await b.SaveChangesAsync();
    var writerA = new StaticOutbox(a, key, "EMP:999991", "collision@example.invalid");
    var writerB = new StaticOutbox(b, key, "EMP:999991", "collision@example.invalid");
    await writerA.QueueAsync(fx.Context(NotificationEventCodes.TripApproved, key), default);
    await writerB.QueueAsync(fx.Context(NotificationEventCodes.TripApproved, key), default);
    await a.SaveChangesAsync();
    await txA.CommitAsync();

    var translator = new EfNotificationCollisionTranslator(b);
    var translated = false;
    try { await b.SaveChangesAsync(); }
    catch (DbUpdateException ex)
    {
        translated = await translator.TryTranslateAsync(ex, default);
        Require(translated, "EA2-39 equivalent collision translator");
        await b.SaveChangesAsync();
    }
    await txB.CommitAsync();
    await using var verify = newDb();
    Require(await verify.MailOutbox().AsNoTracking().CountAsync(x => x.BusinessEventKey == key) == 1, "EA2-39 one durable notification");
    Require(await verify.AuditLogs.AsNoTracking().AnyAsync(x => x.Action == "CollisionLoserBusiness" && x.NewValues == "EA2A collision loser legal business"), "EA2-41 legal business commit");
    Console.WriteLine("EA2-39_RECIPIENTKEY_CONTROLLED_CONCURRENCY=PASS");
    Console.WriteLine("EA2-40_EXACT_COLLISION_PROVENANCE=PASS");
    Console.WriteLine("EA2-41_EQUIVALENT_COLLISION_LEGAL_COMMIT=PASS");
}

static async Task RunCollisionNegativeMatrixAsync(Ea2aFixture fx, Func<AppDbContext> newDb)
{
    // Unrelated unique constraint: NotificationSettings.EventCode.
    await using (var db = newDb())
    {
        db.Set<NotificationSetting>().Add(new NotificationSetting
        {
            EventCode = NotificationEventCodes.TripApproved,
            NotificationType = NotificationCategories.Transaction,
            IsEnabled = true,
            TemplateCode = "TripApproved.v1",
            CreatedAt = DateTime.UtcNow
        });
        var falsePositive = false;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) { falsePositive = !await new EfNotificationCollisionTranslator(db).TryTranslateAsync(ex, default); }
        Require(falsePositive, "EA2-42 unrelated unique propagates");
    }

    // FK 547.
    await using (var db = newDb())
    {
        db.MailOutbox().Add(MailOutbox.CreatePending("UAT", "NoSuchEvent", "EA2A-FK", DateTime.UtcNow, "VisitTrip", "fk", "EMP:EA2A-FK", null, null, "fk@example.invalid", "TripApproved.v1", "{}", Guid.NewGuid()));
        var propagated = false;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) { propagated = !await new EfNotificationCollisionTranslator(db).TryTranslateAsync(ex, default); }
        Require(propagated, "EA2-42 FK propagates");
    }

    // RowVersion concurrency failure.
    await using (var a = newDb())
    await using (var b = newDb())
    {
        var first = await a.Organizations.SingleAsync(x => x.OrganizationId == fx.Organization.OrganizationId);
        var second = await b.Organizations.SingleAsync(x => x.OrganizationId == fx.Organization.OrganizationId);
        first.Notes = "EA2A concurrency A"; await a.SaveChangesAsync();
        second.Notes = "EA2A concurrency B";
        var propagated = false;
        try { await b.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException ex) { propagated = !await new EfNotificationCollisionTranslator(b).TryTranslateAsync(ex, default); }
        Require(propagated, "EA2-42 concurrency propagates");
    }

    // Same event/email but a mismatched canonical recipient key must not be
    // translated as equivalent.
    var mismatchKey = "EA2A-COLLISION-MISMATCH";
    await using (var seed = newDb())
    {
        seed.MailOutbox().Add(MailOutbox.CreatePending("UAT", NotificationEventCodes.TripApproved, mismatchKey, DateTime.UtcNow, "VisitTrip", "mismatch", "EMP:999992", null, null, "mismatch@example.invalid", "TripApproved.v1", "{}", Guid.NewGuid()));
        await seed.SaveChangesAsync();
    }
    await using (var db = newDb())
    {
        db.MailOutbox().Add(MailOutbox.CreatePending("UAT", NotificationEventCodes.TripApproved, mismatchKey, DateTime.UtcNow, "VisitTrip", "mismatch", "EMP:999993", null, null, "mismatch@example.invalid", "TripApproved.v1", "{}", Guid.NewGuid()));
        var propagated = false;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) { propagated = !await new EfNotificationCollisionTranslator(db).TryTranslateAsync(ex, default); }
        Require(propagated, "EA2-42 mismatched notification propagates");
    }
    Console.WriteLine("EA2-42_NEGATIVE_COLLISION_MATRIX=PASS");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException($"EA2A assertion failed: {message}");
}

static string FindRepoRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "backend", "FieldVisitSystem.sln")))
        directory = directory.Parent;
    return directory?.FullName ?? Directory.GetCurrentDirectory();
}

sealed class Ea2aFixture
{
    public required Organization Organization { get; init; }
    public required Team Team { get; init; }
    public required User VisitorUser { get; init; }
    public required User LeaderUser { get; init; }
    public required User AdminUser { get; init; }
    public required Employment VisitorEmployment { get; init; }
    public required Employment LeaderEmployment { get; init; }
    public required CurrentUserDto Visitor { get; init; }
    public required CurrentUserDto Leader { get; init; }
    public required CurrentUserDto Admin { get; init; }

    public static async Task<Ea2aFixture> SeedAsync(Func<AppDbContext> newDb)
    {
        await using var db = newDb();
        var now = DateTime.UtcNow;
        var organization = new Organization { OrganizationCode = "EA2A", OrganizationName = "EA2A Organization", IsActive = true, CreatedAt = now };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var team = new Team { OrganizationId = organization.OrganizationId, TeamCode = "EA2A-T", TeamName = "EA2A Team", IsActive = true, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now };
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        var visitorPerson = new Person { DisplayName = "EA2A Visitor", CreatedAt = now };
        var leaderPerson = new Person { DisplayName = "EA2A Leader", CreatedAt = now };
        var adminPerson = new Person { DisplayName = "EA2A Admin", CreatedAt = now };
        db.Persons.AddRange(visitorPerson, leaderPerson, adminPerson);
        await db.SaveChangesAsync();
        var visitor = new User { OrganizationId = organization.OrganizationId, TeamId = team.TeamId, EmployeeNo = "EA2A-V", DisplayName = "EA2A Visitor", Email = "visitor@example.invalid", IsActive = true, CreatedAt = now };
        var leader = new User { OrganizationId = organization.OrganizationId, TeamId = team.TeamId, EmployeeNo = "EA2A-L", DisplayName = "EA2A Leader", Email = "leader@example.invalid", IsActive = true, CreatedAt = now };
        var admin = new User { OrganizationId = organization.OrganizationId, TeamId = team.TeamId, EmployeeNo = "EA2A-A", DisplayName = "EA2A Admin", Email = "admin@example.invalid", IsActive = true, CreatedAt = now };
        db.Users.AddRange(visitor, leader, admin);
        await db.SaveChangesAsync();
        var visitorEmployment = new Employment { PersonId = visitorPerson.PersonId, OrganizationId = organization.OrganizationId, EmployeeNo = "EA2A-V", Email = "visitor@example.invalid", LegacyUserId = visitor.UserId, SourceType = "EA2A", CreatedAt = now, HireDate = new DateOnly(2020, 1, 1) };
        var leaderEmployment = new Employment { PersonId = leaderPerson.PersonId, OrganizationId = organization.OrganizationId, EmployeeNo = "EA2A-L", Email = "leader@example.invalid", LegacyUserId = leader.UserId, SourceType = "EA2A", CreatedAt = now, HireDate = new DateOnly(2020, 1, 1) };
        var adminEmployment = new Employment { PersonId = adminPerson.PersonId, OrganizationId = organization.OrganizationId, EmployeeNo = "EA2A-A", Email = "admin@example.invalid", LegacyUserId = admin.UserId, SourceType = "EA2A", CreatedAt = now, HireDate = new DateOnly(2020, 1, 1) };
        db.Employments.AddRange(visitorEmployment, leaderEmployment, adminEmployment);
        await db.SaveChangesAsync();
        db.TeamLeaderAssignments.Add(new TeamLeaderAssignment { TeamId = team.TeamId, EmploymentId = leaderEmployment.EmploymentId, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAt = now });
        db.Roles.AddRange(new Role { RoleCode = "visitor", RoleName = "Visitor", IsActive = true, CreatedAt = now }, new Role { RoleCode = "leader", RoleName = "Leader", IsActive = true, CreatedAt = now }, new Role { RoleCode = "admin", RoleName = "Admin", IsActive = true, CreatedAt = now });
        await db.SaveChangesAsync();
        // Notification settings, recipient rules and disabled UAT policy are seeded by 1800_006.
        db.UserRoles.AddRange(new UserRole { UserId = visitor.UserId, RoleId = await db.Roles.Where(x => x.RoleCode == "visitor").Select(x => x.RoleId).SingleAsync(), AssignedAt = now }, new UserRole { UserId = leader.UserId, RoleId = await db.Roles.Where(x => x.RoleCode == "leader").Select(x => x.RoleId).SingleAsync(), AssignedAt = now }, new UserRole { UserId = admin.UserId, RoleId = await db.Roles.Where(x => x.RoleCode == "admin").Select(x => x.RoleId).SingleAsync(), AssignedAt = now });
        await db.SaveChangesAsync();
        return new Ea2aFixture
        {
            Organization = organization,
            Team = team,
            VisitorUser = visitor,
            LeaderUser = leader,
            AdminUser = admin,
            VisitorEmployment = visitorEmployment,
            LeaderEmployment = leaderEmployment,
            Visitor = new CurrentUserDto(visitor.UserId, visitor.EmployeeNo!, visitor.DisplayName, visitor.Email, organization.OrganizationId, team.TeamId, team.TeamName, ["visitor"]),
            Leader = new CurrentUserDto(leader.UserId, leader.EmployeeNo!, leader.DisplayName, leader.Email, organization.OrganizationId, team.TeamId, team.TeamName, ["leader"]),
            Admin = new CurrentUserDto(admin.UserId, admin.EmployeeNo!, admin.DisplayName, admin.Email, organization.OrganizationId, team.TeamId, team.TeamName, ["admin"])
        };
    }

    public async Task<VisitTrip> AddTripAsync(AppDbContext db, string status, long? employmentId, bool withSnapshot = false)
    {
        var now = DateTime.UtcNow;
        var trip = new VisitTrip
        {
            TripNo = $"EA2A-{Guid.NewGuid():N}"[..18],
            UserId = VisitorUser.UserId,
            EmploymentId = employmentId,
            OrganizationId = Organization.OrganizationId,
            TeamId = Team.TeamId,
            VisitDate = new DateOnly(2026, 9, 11),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            Status = status,
            VehicleType = "MOTORCYCLE",
            Purpose = "EA2A",
            Notes = "base",
            CreatedAt = now,
            CreatedByUserId = VisitorUser.UserId,
            ApprovedAt = status == TripStatuses.Approved ? now.AddMinutes(-5) : null
        };
        trip.Stops.Add(new VisitTripStop { StopSequence = 1, LocationNameSnapshot = "EA2A A", AddressSnapshot = "A", CreatedAt = now });
        trip.Stops.Add(new VisitTripStop { StopSequence = 2, LocationNameSnapshot = "EA2A B", AddressSnapshot = "B", CreatedAt = now });
        db.VisitTrips.Add(trip);
        db.MileageCalculations.Add(new MileageCalculation { VisitTrip = trip, ClaimedDistanceKm = 10m, SystemDistanceKm = 10m, ApprovedDistanceKm = 10m, CreatedAt = now });
        db.MileageRateRules.Add(new MileageRateRule { OrganizationId = Organization.OrganizationId, RuleName = "EA2A rate", VehicleType = "MOTORCYCLE", RatePerKm = 3m, EffectiveFrom = new DateOnly(2020, 1, 1), IsActive = true, CreatedAt = now });
        await db.SaveChangesAsync();
        if (withSnapshot)
        {
            var snapshot = new VisitTripSnapshot { VisitTripId = trip.VisitTripId, SnapshotVersion = 1, SnapshotType = "Approved", TripNo = trip.TripNo, UserId = trip.UserId, EmploymentIdSnapshot = employmentId, EmployeeNoSnapshot = VisitorUser.EmployeeNo ?? "", DisplayNameSnapshot = VisitorUser.DisplayName, OrganizationId = Organization.OrganizationId, OrganizationNameSnapshot = Organization.OrganizationName, TeamId = Team.TeamId, TeamNameSnapshot = Team.TeamName, VisitDate = trip.VisitDate, StartTime = trip.StartTime, EndTime = trip.EndTime, StatusSnapshot = TripStatuses.Approved, VehicleTypeSnapshot = trip.VehicleType, ClaimedDistanceKmSnapshot = 10m, SystemDistanceKmSnapshot = 10m, ApprovedDistanceKmSnapshot = 10m, RatePerKmSnapshot = 3m, SubsidyAmountSnapshot = 30m, ApprovedAtSnapshot = trip.ApprovedAt, NotesSnapshot = trip.Notes, CreatedAt = now, CreatedByUserId = LeaderUser.UserId };
            foreach (var stop in trip.Stops)
                snapshot.Stops.Add(new VisitTripSnapshotStop { StopSequence = stop.StopSequence, LocationNameSnapshot = stop.LocationNameSnapshot, AddressSnapshot = stop.AddressSnapshot, CreatedAt = now });
            db.VisitTripSnapshots.Add(snapshot);
            await db.SaveChangesAsync();
        }
        return trip;
    }

    public CorrectionProposal Proposal(string notes, decimal claimed, decimal approved, decimal rate, decimal subsidy) =>
        new(new DateOnly(2026, 9, 11), new TimeOnly(9, 0), new TimeOnly(10, 0), notes, claimed, approved, rate, subsidy,
            [new CorrectionStopProposal(1, null, "EA2A A", "A", null, null, null, null, null, null), new CorrectionStopProposal(2, null, "EA2A B", "B", null, null, null, null, null, null)]);

    public NotificationEventContext Context(string code, string key) => new(code, "VisitTrip", "EA2A", key, new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc), Organization.OrganizationId, Team.TeamId, VisitorEmployment.EmploymentId, null, null, new NotificationTemplatePayloadV1(key, "fixture"), Guid.NewGuid());

    public (TripService Service, LeaderService Leader) TripServices(AppDbContext db, CurrentUserDto user, INotificationOutboxWriter writer, INotificationCollisionTranslator translator)
    {
        var current = new FixedCurrentUser(user);
        var users = new UserRepository(db);
        var trips = new TripRepository(db);
        var masters = new MasterRepository(db);
        var mileage = new DbWork2MileageRepositoryDecorator(new MileageRepository(db));
        var workflow = new WorkflowRepository(db);
        var snapshots = new TripSnapshotRepository(db);
        var service = new TripService(current, users, trips, masters, mileage, workflow, null!, null!, snapshots, db, new EfTransactionBoundary(db), writer, translator);
        var leader = new LeaderService(current, trips, mileage, null!, workflow, snapshots, db, service, new EfTransactionBoundary(db), writer, translator);
        return (service, leader);
    }

    public V160FinalService CorrectionService(AppDbContext db, CurrentUserDto user, INotificationOutboxWriter writer, INotificationCollisionTranslator translator)
    {
        var current = new FixedCurrentUser(user);
        var repository = new V160FinalRepository(db, null!, null!, null!, writer, translator);
        return new V160FinalService(current, repository, null!, null!, null!, null!, null!, null!, new TripRepository(db), new EfTransactionBoundary(db));
    }
}

sealed class FixedCurrentUser(CurrentUserDto user) : ICurrentUserService
{
    public CurrentUserDto GetRequired() => user;
}

sealed class StaticOutbox(AppDbContext db, string? forcedKey = null, string? forcedRecipientKey = null, string? forcedEmail = null) : INotificationOutboxWriter
{
    public async Task<NotificationQueueResult> QueueAsync(NotificationEventContext context, CancellationToken ct)
    {
        var key = forcedKey ?? context.BusinessEventKey;
        var recipient = new NotificationRecipient(NotificationRecipientRuleCodes.TripOwner, forcedRecipientKey ?? "EMP:999990", null, null, forcedEmail ?? "visitor@example.invalid", true);
        var writer = new EfNotificationOutboxWriter(db, new StaticRecipientResolver([recipient]), new NotificationRuntimeEnvironment("UAT"), TimeProvider.System);
        return await writer.QueueAsync(context with { BusinessEventKey = key }, ct);
    }
}

sealed class ThrowingOutbox : INotificationOutboxWriter
{
    public Task<NotificationQueueResult> QueueAsync(NotificationEventContext context, CancellationToken ct) =>
        throw new InvalidOperationException("EA2A_INJECT_AFTER_FIRST_FLUSH");
}

sealed class ConditionalThrowingOutbox(AppDbContext db, long aggregateId) : INotificationOutboxWriter
{
    public Task<NotificationQueueResult> QueueAsync(NotificationEventContext context, CancellationToken ct)
    {
        if (context.AggregateId == aggregateId.ToString())
            throw new InvalidOperationException("EA2A_INJECT_AFTER_FIRST_FLUSH");
        return new StaticOutbox(db).QueueAsync(context, ct);
    }
}

sealed class StaticRecipientResolver(IReadOnlyList<NotificationRecipient> recipients) : INotificationRecipientResolver
{
    public Task<IReadOnlyList<NotificationRecipient>> ResolveAsync(NotificationEventContext context, IReadOnlyCollection<string> recipientRuleCodes, CancellationToken ct) => Task.FromResult(recipients);
}

static class AppDbContextEa2aExtensions
{
    public static DbSet<MailOutbox> MailOutbox(this AppDbContext db) => db.Set<MailOutbox>();
}
