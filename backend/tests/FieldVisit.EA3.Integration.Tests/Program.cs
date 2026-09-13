using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

var connection = Environment.GetEnvironmentVariable("EA3_SQL_CONNECTION")
    ?? throw new InvalidOperationException("EA3_SQL_CONNECTION is required.");
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(connection, sql => sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null))
    .ReplaceService<IModelCustomizer, NotificationModelCustomizer>()
    .Options;
AppDbContext NewDb() => new(options);

await Ea2aSchemaBootstrap.InitializeAsync(connection);
var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
var fixture = await Fixture.CreateAsync(NewDb, clock);
Console.WriteLine("EA3_REAL_SQL_DISPOSABLE=PASS");

await RunClaimAndInvariantMatrixAsync(fixture);
await RunReservationAndFencingMatrixAsync(fixture);
await RunRetryMatrixAsync(fixture);
await RunPolicyMatrixAsync(fixture);
Console.WriteLine("EA3_REAL_SQL_REGRESSION=35/35=PASS");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("EA3 assertion failed: " + message);
}

static void Pass(int number, string marker)
    => Console.WriteLine($"EA3-{number:00}_{marker}=PASS");

static async Task ExpectAsync<TException>(Func<Task> action, string message) where TException : Exception
{
    try { await action(); }
    catch (TException) { return; }
    throw new InvalidOperationException("EA3 expected failure: " + message);
}

static async Task RunClaimAndInvariantMatrixAsync(Fixture fx)
{
    await using (var schema = fx.NewDb())
    {
        var checks = await schema.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value]
            FROM sys.check_constraints
            WHERE parent_object_id = OBJECT_ID(N'dbo.MailOutbox')
              AND name IN (N'CK_MailOutbox_ProcessingOwnership', N'CK_MailOutbox_Finalization')
            """).SingleAsync();
        Require(checks == 2, "frozen 1800_006 ownership/finalization constraints remain authoritative");
        Pass(0, "FROZEN_SCHEMA_AUTHORITY_UNCHANGED");
    }

    var row = await fx.PendingAsync(Fixture.LiveEnvironment, "claim@example.test");
    await using var dbA = fx.NewDb();
    await using var dbB = fx.NewDb();
    var storeA = new EfNotificationDeliveryStore(dbA);
    var storeB = new EfNotificationDeliveryStore(dbB);
    var claims = await Task.WhenAll(
        storeA.TryClaimNextAsync(fx.Now, default),
        storeB.TryClaimNextAsync(fx.Now, default));
    var claim = claims.Single(x => x is not null)!;
    Require(claim.MailOutboxId == row.MailOutboxId && claims.Count(x => x is not null) == 1,
        "simultaneous claim has exactly one owner");
    Pass(1, "ATOMIC_SINGLE_OWNER_CLAIM");
    Pass(24, "TWO_WORKER_CONCURRENT_CLAIM");

    await using (var protectionDb = fx.NewDb())
    {
        var protectedClaim = await new EfNotificationDeliveryStore(protectionDb)
            .TryClaimNextAsync(fx.Now.AddMinutes(4).AddSeconds(59), default);
        Require(protectedClaim is null, "unexpired lease cannot be stolen");
        Pass(2, "UNEXPIRED_LEASE_PROTECTED");
    }

    fx.Clock.Advance(NotificationDeliveryAuthority.LeaseDuration);
    await using var reclaimDb = fx.NewDb();
    var reclaimStore = new EfNotificationDeliveryStore(reclaimDb);
    var reclaimed = await reclaimStore.TryClaimNextAsync(fx.Now, default);
    Require(reclaimed is not null && reclaimed.MailOutboxId == row.MailOutboxId
        && reclaimed.ProcessingToken != claim.ProcessingToken
        && reclaimed.ProcessingLeaseUntil == fx.Now.AddMinutes(5), "expired lease gets a new token and exact lease");
    Pass(3, "EXPIRED_LEASE_RECLAIM_NEW_TOKEN");

    await fx.TerminalAsync("Sent");
    await fx.TerminalAsync("Failed");
    await fx.TerminalAsync("Cancelled");
    await using (var terminalDb = fx.NewDb())
    {
        var terminalIds = await terminalDb.Set<MailOutbox>().AsNoTracking()
            .Where(x => x.Status == "Sent" || x.Status == "Failed" || x.Status == "Cancelled")
            .Select(x => x.MailOutboxId).ToListAsync();
        var noTerminalClaim = await new EfNotificationDeliveryStore(terminalDb).TryClaimNextAsync(fx.Now, default);
        Require(terminalIds.Count >= 3 && !terminalIds.Contains(row.MailOutboxId) && noTerminalClaim is null,
            "terminal rows are not reclaimable");
        Pass(4, "TERMINAL_ROWS_NEVER_RECLAIMABLE");

        var persistedClaim = await terminalDb.Set<MailOutbox>().AsNoTracking()
            .SingleAsync(x => x.MailOutboxId == row.MailOutboxId);
        Require(persistedClaim.Status == "Processing" && persistedClaim.ProcessingToken.HasValue
            && persistedClaim.ProcessingLeaseUntil.HasValue, "Processing exclusively owns token and lease");
        Pass(5, "PROCESSING_OWNS_TOKEN_AND_LEASE");

        Require(!await terminalDb.Set<MailOutbox>().AsNoTracking().AnyAsync(x => x.Status != "Processing"
            && (x.ProcessingToken != null || x.ProcessingLeaseUntil != null)), "non-Processing clears ownership");
        Pass(6, "NON_PROCESSING_CLEARS_OWNERSHIP");

        Require(!await terminalDb.Set<MailOutbox>().AsNoTracking().AnyAsync(x =>
                ((x.Status == "Pending" || x.Status == "Processing") && x.FinalizedAt != null)
                || ((x.Status == "Sent" || x.Status == "Failed" || x.Status == "Cancelled") && x.FinalizedAt == null)),
            "finalization nullability follows state");
        Pass(7, "FINALIZATION_STATE_INVARIANTS");
    }

    Require(await reclaimStore.TryCancelAsync(row.MailOutboxId, reclaimed!.ProcessingToken, fx.Now,
        NotificationDeliveryErrorCodes.PolicyInvalid, default), "claim scenario cleanup");
}

static async Task RunReservationAndFencingMatrixAsync(Fixture fx)
{
    var beforeProvider = false;
    var provider = new RecordingProvider(NotificationProviderResult.Success("provider-1"))
    {
        BeforeResult = async (request, _) =>
        {
            await using var probe = fx.NewDb();
            var durable = await probe.Set<MailOutbox>().AsNoTracking()
                .SingleAsync(x => x.MailOutboxId == request.MailOutboxId);
            beforeProvider = durable.Status == "Processing"
                             && durable.ProcessingToken.HasValue
                             && durable.ProcessingLeaseUntil > fx.Now
                             && durable.AttemptCount == request.ReservedAttemptNumber
                             && !await probe.Set<MailDeliveryLog>().AnyAsync(x => x.MailOutboxId == request.MailOutboxId);
        }
    };
    var committedRow = await fx.PendingAsync(Fixture.LiveEnvironment, "reserved@example.test");
    await using (var db = fx.NewDb())
        Require(await fx.Processor(db, provider).ProcessNextAsync(default), "reservation scenario processed");
    Require(beforeProvider, "reservation committed before provider invocation");
    Pass(8, "RESERVATION_COMMITTED_BEFORE_PROVIDER");
    await using (var check = fx.NewDb())
    {
        var log = await check.Set<MailDeliveryLog>().AsNoTracking()
            .SingleAsync(x => x.MailOutboxId == committedRow.MailOutboxId);
        Require(log.AttemptNumber == 1, "delivery log uses reserved ordinal");
        Pass(21, "DELIVERY_LOG_USES_RESERVED_ORDINAL");
    }

    var reservationFailureRow = await fx.PendingAsync(Fixture.LiveEnvironment, "reserve-fail@example.test");
    var neverCalled = new RecordingProvider(NotificationProviderResult.Success("must-not-call"));
    await using (var db = fx.NewDb())
    {
        var inner = new EfNotificationDeliveryStore(db);
        await ExpectAsync<InjectedFailureException>(
            () => fx.Processor(db, neverCalled, new ReservationFailingStore(inner)).ProcessNextAsync(default),
            "reservation persistence failure");
    }
    await using (var check = fx.NewDb())
    {
        var persisted = await check.Set<MailOutbox>().AsNoTracking()
            .SingleAsync(x => x.MailOutboxId == reservationFailureRow.MailOutboxId);
        Require(neverCalled.Requests.Count == 0 && persisted.AttemptCount == 0 && persisted.Status == "Processing",
            "failed reservation invokes no provider and reserves no ordinal");
        Pass(9, "RESERVATION_SAVE_FAILURE_ZERO_PROVIDER_CALLS");
        Require(await new EfNotificationDeliveryStore(check).TryCancelAsync(
            persisted.MailOutboxId, persisted.ProcessingToken!.Value, fx.Now,
            NotificationDeliveryErrorCodes.PolicyInvalid, default), "reservation failure cleanup");
    }

    var preProviderCrashRow = await fx.PendingAsync(Fixture.LiveEnvironment, "pre-provider-crash@example.test");
    await using (var db = fx.NewDb())
    {
        var store = new EfNotificationDeliveryStore(db);
        var claim = await store.TryClaimNextAsync(fx.Now, default) ?? throw new InvalidOperationException("claim missing");
        var reserved = await store.TryReserveAttemptAsync(claim.MailOutboxId, claim.ProcessingToken, 0, fx.Now, default);
        Require(reserved?.AttemptNumber == 1, "first crash reservation durable");
    }
    fx.Clock.Advance(NotificationDeliveryAuthority.LeaseDuration + TimeSpan.FromMilliseconds(1));
    var afterCrashProvider = new RecordingProvider(NotificationProviderResult.Success("provider-gap"));
    await using (var db = fx.NewDb())
        await fx.Processor(db, afterCrashProvider).ProcessNextAsync(default);
    await using (var check = fx.NewDb())
    {
        var persisted = await check.Set<MailOutbox>().AsNoTracking()
            .SingleAsync(x => x.MailOutboxId == preProviderCrashRow.MailOutboxId);
        var logs = await check.Set<MailDeliveryLog>().AsNoTracking()
            .Where(x => x.MailOutboxId == preProviderCrashRow.MailOutboxId).ToListAsync();
        Require(persisted.Status == "Sent" && persisted.AttemptCount == 2
            && logs.Count == 1 && logs[0].AttemptNumber == 2, "crash consumes #1 and permits log gap");
        Pass(10, "CRASH_BEFORE_PROVIDER_CONSUMES_RESERVATION");
        Pass(22, "DELIVERY_LOG_GAPS_ARE_VALID");
    }

    var persistenceCrashRow = await fx.PendingAsync(Fixture.LiveEnvironment, "post-provider-crash@example.test");
    var acceptedFirst = new RecordingProvider(NotificationProviderResult.Success("accepted-before-crash"));
    await using (var db = fx.NewDb())
    {
        var inner = new EfNotificationDeliveryStore(db);
        await ExpectAsync<InjectedFailureException>(
            () => fx.Processor(db, acceptedFirst, new ResultFailingStore(inner)).ProcessNextAsync(default),
            "provider result persistence crash");
    }
    await using (var check = fx.NewDb())
    {
        var persisted = await check.Set<MailOutbox>().AsNoTracking()
            .SingleAsync(x => x.MailOutboxId == persistenceCrashRow.MailOutboxId);
        Require(acceptedFirst.Requests.Count == 1 && persisted.Status == "Processing" && persisted.AttemptCount == 1
            && !await check.Set<MailDeliveryLog>().AnyAsync(x => x.MailOutboxId == persistenceCrashRow.MailOutboxId),
            "provider acceptance with persistence crash leaves durable reservation and no result log");
        Pass(11, "PROVIDER_ACCEPTED_DB_CRASH_CONSUMES_RESERVATION");
    }
    fx.Clock.Advance(NotificationDeliveryAuthority.LeaseDuration + TimeSpan.FromMilliseconds(1));
    var acceptedRetry = new RecordingProvider(NotificationProviderResult.Success("accepted-after-reclaim"));
    await using (var db = fx.NewDb())
        await fx.Processor(db, acceptedRetry).ProcessNextAsync(default);
    Require(acceptedFirst.Requests.Single().IdempotencyKey == acceptedRetry.Requests.Single().IdempotencyKey,
        "same outbox retains stable provider idempotency key");
    Pass(32, "STABLE_IDEMPOTENCY_KEY_ACROSS_RETRIES");
    Require(provider.Requests.Single().IdempotencyKey != acceptedFirst.Requests.Single().IdempotencyKey,
        "different outbox rows use different provider keys");
    Pass(33, "DIFFERENT_OUTBOX_ROWS_DIFFERENT_PROVIDER_KEYS");

    var staleRow = await fx.PendingAsync(Fixture.LiveEnvironment, "stale@example.test");
    NotificationDeliveryClaim oldClaim;
    await using (var oldDb = fx.NewDb())
    {
        var oldStore = new EfNotificationDeliveryStore(oldDb);
        oldClaim = await oldStore.TryClaimNextAsync(fx.Now, default) ?? throw new InvalidOperationException("old claim missing");
        Require(await oldStore.TryReserveAttemptAsync(oldClaim.MailOutboxId, oldClaim.ProcessingToken, 0, fx.Now, default)
            is not null, "old reservation missing");
    }
    fx.Clock.Advance(NotificationDeliveryAuthority.LeaseDuration + TimeSpan.FromMilliseconds(1));
    await using var currentDb = fx.NewDb();
    var currentStore = new EfNotificationDeliveryStore(currentDb);
    var currentClaim = await currentStore.TryClaimNextAsync(fx.Now, default) ?? throw new InvalidOperationException("reclaim missing");
    Require(currentClaim.MailOutboxId == staleRow.MailOutboxId && currentClaim.ProcessingToken != oldClaim.ProcessingToken,
        "new claim established for fencing");

    await using (var staleDb = fx.NewDb())
    {
        var staleStore = new EfNotificationDeliveryStore(staleDb);
        var sent = await staleStore.TryRecordResultAsync(new NotificationDeliveryCompletion(
            staleRow.MailOutboxId, oldClaim.ProcessingToken, 1, fx.Now, "TEST",
            NotificationProviderResult.Success("stale")), default);
        Require(!sent && !await staleDb.Set<MailDeliveryLog>().AnyAsync(x => x.MailOutboxId == staleRow.MailOutboxId),
            "stale token cannot persist log");
        Pass(12, "STALE_TOKEN_CANNOT_PERSIST_DELIVERY_LOG");
        Pass(13, "STALE_TOKEN_CANNOT_PERSIST_SENT");

        Require(!await staleStore.TryRecordResultAsync(new NotificationDeliveryCompletion(
            staleRow.MailOutboxId, oldClaim.ProcessingToken, 1, fx.Now, "TEST",
            NotificationProviderResult.Retryable("STALE")), default), "stale retry rejected");
        Pass(14, "STALE_TOKEN_CANNOT_PERSIST_RETRY");
        Require(!await staleStore.TryRecordResultAsync(new NotificationDeliveryCompletion(
            staleRow.MailOutboxId, oldClaim.ProcessingToken, 1, fx.Now, "TEST",
            NotificationProviderResult.Permanent("STALE")), default), "stale failure rejected");
        Pass(15, "STALE_TOKEN_CANNOT_PERSIST_FAILED");
        Require(!await staleStore.TryCancelAsync(staleRow.MailOutboxId, oldClaim.ProcessingToken, fx.Now,
            NotificationDeliveryErrorCodes.PolicyDisabled, default), "stale cancellation rejected");
        Pass(16, "STALE_TOKEN_CANNOT_PERSIST_CANCELLED");
    }
    Require(await currentStore.TryCancelAsync(staleRow.MailOutboxId, currentClaim.ProcessingToken, fx.Now,
        NotificationDeliveryErrorCodes.PolicyInvalid, default), "stale scenario cleanup");

    await using (var duplicateDb = fx.NewDb())
    {
        duplicateDb.Set<MailDeliveryLog>().Add(new MailDeliveryLog
        {
            MailOutboxId = committedRow.MailOutboxId,
            AttemptNumber = 1,
            Provider = "TEST",
            Status = "Sent",
            AttemptedAt = fx.Now
        });
        await ExpectAsync<DbUpdateException>(() => duplicateDb.SaveChangesAsync(), "delivery log ordinal uniqueness");
        Pass(23, "DELIVERY_LOG_ORDINAL_UNIQUENESS_AUTHORITY");
    }
}

static async Task RunRetryMatrixAsync(Fixture fx)
{
    var retryRow = await fx.PendingAsync(Fixture.LiveEnvironment, "retry@example.test");
    var provider = new RecordingProvider(
        NotificationProviderResult.Retryable("TEMPORARY_ONE"),
        NotificationProviderResult.Retryable("TEMPORARY_TWO"),
        NotificationProviderResult.Retryable("TEMPORARY_THREE"));

    var firstFailureAt = fx.Now;
    await using (var db = fx.NewDb())
        await fx.Processor(db, provider).ProcessNextAsync(default);
    await using (var check = fx.NewDb())
    {
        var row = await check.Set<MailOutbox>().AsNoTracking().SingleAsync(x => x.MailOutboxId == retryRow.MailOutboxId);
        Require(row.Status == "Pending" && row.AttemptCount == 1
            && row.AvailableAt == firstFailureAt.AddMinutes(1) && row.FinalizedAt == null
            && row.ProcessingToken == null && row.ProcessingLeaseUntil == null,
            "attempt #1 schedules exact one-minute retry and clears ownership");
    }
    fx.Clock.Advance(TimeSpan.FromMinutes(1) - TimeSpan.FromMilliseconds(1));
    await using (var earlyDb = fx.NewDb())
        Require(await new EfNotificationDeliveryStore(earlyDb).TryClaimNextAsync(fx.Now, default) is null,
            "one-minute retry not early");
    fx.Clock.Advance(TimeSpan.FromMilliseconds(1));
    await using (var dueDb = fx.NewDb())
        await fx.Processor(dueDb, provider).ProcessNextAsync(default);
    Pass(17, "RETRY_ONE_MINUTE_EXACT_BOUNDARY");

    var secondFailureAt = fx.Now;
    await using (var check = fx.NewDb())
    {
        var row = await check.Set<MailOutbox>().AsNoTracking().SingleAsync(x => x.MailOutboxId == retryRow.MailOutboxId);
        Require(row.Status == "Pending" && row.AttemptCount == 2
            && row.AvailableAt == secondFailureAt.AddMinutes(5) && row.FinalizedAt == null
            && row.ProcessingToken == null && row.ProcessingLeaseUntil == null,
            "attempt #2 schedules exact five-minute retry and clears ownership");
    }
    fx.Clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromMilliseconds(1));
    await using (var earlyDb = fx.NewDb())
        Require(await new EfNotificationDeliveryStore(earlyDb).TryClaimNextAsync(fx.Now, default) is null,
            "five-minute retry not early");
    fx.Clock.Advance(TimeSpan.FromMilliseconds(1));
    await using (var dueDb = fx.NewDb())
        await fx.Processor(dueDb, provider).ProcessNextAsync(default);
    Pass(18, "RETRY_FIVE_MINUTE_EXACT_BOUNDARY");

    await using (var check = fx.NewDb())
    {
        var row = await check.Set<MailOutbox>().AsNoTracking().SingleAsync(x => x.MailOutboxId == retryRow.MailOutboxId);
        Require(row.Status == "Failed" && row.AttemptCount == 3 && row.FinalizedAt.HasValue
            && row.ProcessingToken == null && row.ProcessingLeaseUntil == null, "third retryable reservation is terminal");
        Require(await check.Set<MailDeliveryLog>().CountAsync(x => x.MailOutboxId == retryRow.MailOutboxId) == 3,
            "all persisted provider results use their reservations");
        Pass(19, "THIRD_RESERVATION_EXHAUSTS_ATTEMPTS");
    }
    fx.Clock.Advance(TimeSpan.FromHours(1));
    await using (var noFourthDb = fx.NewDb())
        Require(await fx.Processor(noFourthDb, provider).ProcessNextAsync(default) == false && provider.Requests.Count == 3,
            "terminal row has no fourth provider call");
    var thirdCrashRow = await fx.PendingAsync(Fixture.LiveEnvironment, "third-crash@example.test", attemptCount: 3);
    await using (var crashStateDb = fx.NewDb())
    {
        await crashStateDb.Set<MailOutbox>()
            .Where(x => x.MailOutboxId == thirdCrashRow.MailOutboxId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Processing")
                .SetProperty(x => x.ProcessingToken, (Guid?)Guid.NewGuid())
                .SetProperty(x => x.ProcessingLeaseUntil, (DateTime?)fx.Now.AddMilliseconds(-1)));
    }
    var noFourthProvider = new RecordingProvider(NotificationProviderResult.Success("must-not-send"));
    await using (var exhaustedDb = fx.NewDb())
        Require(await fx.Processor(exhaustedDb, noFourthProvider).ProcessNextAsync(default),
            "expired third reservation is finalized");
    await using (var exhaustedCheck = fx.NewDb())
    {
        var exhausted = await exhaustedCheck.Set<MailOutbox>().AsNoTracking()
            .SingleAsync(x => x.MailOutboxId == thirdCrashRow.MailOutboxId);
        Require(exhausted.Status == "Failed" && exhausted.AttemptCount == 3
            && exhausted.LastErrorCode == NotificationDeliveryErrorCodes.AttemptsExhausted
            && noFourthProvider.Requests.Count == 0, "crashed reservation #3 cannot create #4");
    }
    Pass(20, "NO_FOURTH_RESERVATION_OR_PROVIDER_CALL");

    var permanentRow = await fx.PendingAsync(Fixture.LiveEnvironment, "permanent@example.test");
    var permanentProvider = new RecordingProvider(NotificationProviderResult.Permanent("INVALID_RECIPIENT", "rejected"));
    await using (var db = fx.NewDb())
        await fx.Processor(db, permanentProvider).ProcessNextAsync(default);
    await using (var check = fx.NewDb())
    {
        var row = await check.Set<MailOutbox>().AsNoTracking().SingleAsync(x => x.MailOutboxId == permanentRow.MailOutboxId);
        Require(row.Status == "Failed" && row.AttemptCount == 1 && row.FinalizedAt.HasValue
            && row.LastErrorCode == "PROVIDER_INVALID_RECIPIENT", "permanent provider failure terminates immediately");
        Pass(34, "PERMANENT_PROVIDER_FAILURE_TERMINAL");
    }
}

static async Task RunPolicyMatrixAsync(Fixture fx)
{
    var provider = new RecordingProvider(NotificationProviderResult.Success("policy-send"));
    var disabled = await fx.PendingAsync(Fixture.DisabledEnvironment, "disabled@example.test");
    await using (var db = fx.NewDb())
        await fx.Processor(db, provider).ProcessNextAsync(default);
    var flagOff = await fx.PendingAsync(Fixture.FlagOffEnvironment, "flag-off@example.test", attemptCount: 2);
    await using (var db = fx.NewDb())
        await fx.Processor(db, provider).ProcessNextAsync(default);
    await using (var check = fx.NewDb())
    {
        var rows = await check.Set<MailOutbox>().AsNoTracking()
            .Where(x => x.MailOutboxId == disabled.MailOutboxId || x.MailOutboxId == flagOff.MailOutboxId)
            .ToListAsync();
        Require(rows.All(x => x.Status == "Cancelled") && provider.Requests.Count == 0
            && rows.Single(x => x.MailOutboxId == disabled.MailOutboxId).AttemptCount == 0,
            "disabled modes cancel before reservation/provider");
        Pass(25, "DISABLED_POLICY_CANCELS_WITHOUT_ATTEMPT");
    }

    var allowlisted = await fx.PendingAsync(Fixture.TestEnvironment, "allow@example.test");
    var allowProvider = new RecordingProvider(NotificationProviderResult.Success("allowlisted"));
    await using (var db = fx.NewDb())
        await fx.Processor(db, allowProvider).ProcessNextAsync(default);
    await using (var check = fx.NewDb())
    {
        var row = await check.Set<MailOutbox>().AsNoTracking().SingleAsync(x => x.MailOutboxId == allowlisted.MailOutboxId);
        Require(row.Status == "Sent" && allowProvider.Requests.Count == 1, "normalized active allowlist permits Test delivery");
        Pass(26, "TEST_ALLOWLIST_PERMITS_DELIVERY");
    }

    var nonallowlisted = await fx.PendingAsync(Fixture.TestEnvironment, "not-allowed@example.test");
    var blockedProvider = new RecordingProvider(NotificationProviderResult.Success("must-not-send"));
    await using (var db = fx.NewDb())
        await fx.Processor(db, blockedProvider).ProcessNextAsync(default);
    await using (var check = fx.NewDb())
    {
        var row = await check.Set<MailOutbox>().AsNoTracking().SingleAsync(x => x.MailOutboxId == nonallowlisted.MailOutboxId);
        Require(row.Status == "Cancelled" && row.AttemptCount == 0
            && row.LastErrorCode == NotificationDeliveryErrorCodes.TestRecipientNotAllowlisted
            && blockedProvider.Requests.Count == 0, "nonallowlisted Test recipient cancels before provider");
        Pass(27, "TEST_NONALLOWLISTED_RECIPIENT_CANCELLED");
    }

    var uatLive = NotificationDeliveryPolicyAuthority.Decide(
        "UAT", "Live", true, "sender", true, true);
    Require(!uatLive.IsPermitted && uatLive.CancellationCode == NotificationDeliveryErrorCodes.UatLiveForbidden,
        "UAT Live fails closed independent of database check constraint");
    Pass(28, "UAT_LIVE_FAILS_CLOSED");

    await using (var db = fx.NewDb())
    {
        var missing = await new EfNotificationDeliveryPolicyEvaluator(db).EvaluateAsync(
            new NotificationDeliveryClaim(1, "MISSING", NotificationEventCodes.ImportCompleted, "MISSING", fx.Now,
                "EA3", "missing", "USER:1", "missing@example.test", "ImportCompleted.v1", "{}", Guid.NewGuid(),
                0, Guid.NewGuid(), fx.Now.AddMinutes(5)), default);
        var invalid = NotificationDeliveryPolicyAuthority.Decide("EA3INVALID", "Unknown", true, null, true, true);
        var missingSender = NotificationDeliveryPolicyAuthority.Decide("EA3LIVE", "Live", true, null, true, false);
        Require(!missing.IsPermitted && missing.CancellationCode == NotificationDeliveryErrorCodes.PolicyMissing
            && !invalid.IsPermitted && invalid.CancellationCode == NotificationDeliveryErrorCodes.PolicyInvalid
            && !missingSender.IsPermitted && missingSender.CancellationCode == NotificationDeliveryErrorCodes.PolicyInvalid,
            "missing and invalid policies fail closed");
        Pass(29, "MISSING_INVALID_POLICY_FAILS_CLOSED");
    }

    await using (var check = fx.NewDb())
    {
        var row = await check.Set<MailOutbox>().AsNoTracking().SingleAsync(x => x.MailOutboxId == flagOff.MailOutboxId);
        Require(row.Status == "Cancelled" && row.AttemptCount == 2,
            "policy suppression preserves prior durable attempt count");
        Pass(30, "SUPPRESSION_PRESERVES_ATTEMPT_COUNT");
    }

    var live = await fx.PendingAsync(Fixture.LiveEnvironment, "not-in-any-allowlist@example.test");
    var liveProvider = new RecordingProvider(NotificationProviderResult.Success("live"));
    await using (var db = fx.NewDb())
        await fx.Processor(db, liveProvider).ProcessNextAsync(default);
    await using (var check = fx.NewDb())
    {
        var row = await check.Set<MailOutbox>().AsNoTracking().SingleAsync(x => x.MailOutboxId == live.MailOutboxId);
        Require(row.Status == "Sent" && liveProvider.Requests.Count == 1,
            "Live outside UAT ignores Test allowlist");
        Pass(31, "LIVE_OUTSIDE_UAT_IGNORES_ALLOWLIST");
    }
}

sealed class Fixture
{
    public const string LiveEnvironment = "EA3LIVE";
    public const string TestEnvironment = "EA3TEST";
    public const string DisabledEnvironment = "EA3OFF";
    public const string FlagOffEnvironment = "EA3FLAGOFF";

    private int sequence;
    public Func<AppDbContext> NewDb { get; }
    public ManualTimeProvider Clock { get; }
    public DateTime Now => Clock.GetUtcNow().UtcDateTime;

    private Fixture(Func<AppDbContext> newDb, ManualTimeProvider clock)
    {
        NewDb = newDb;
        Clock = clock;
    }

    public static async Task<Fixture> CreateAsync(Func<AppDbContext> newDb, ManualTimeProvider clock)
    {
        await using var db = newDb();
        db.Set<NotificationEnvironmentPolicy>().AddRange(
            Policy(LiveEnvironment, "Live", true),
            Policy(TestEnvironment, "Test", true),
            Policy(DisabledEnvironment, "Disabled", true),
            Policy(FlagOffEnvironment, "Test", false));
        db.Set<NotificationEmailAllowlist>().Add(new NotificationEmailAllowlist
        {
            EnvironmentCode = TestEnvironment,
            Email = "Allow@Example.Test",
            IsActive = true,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync();
        return new Fixture(newDb, clock);
    }

    public NotificationDeliveryProcessor Processor(
        AppDbContext db,
        INotificationEmailProvider provider,
        INotificationDeliveryStore? store = null)
        => new(
            store ?? new EfNotificationDeliveryStore(db),
            new EfNotificationDeliveryPolicyEvaluator(db),
            provider,
            Clock,
            new NotificationDeliveryRuntimeOptions { ProviderTimeout = TimeSpan.FromSeconds(10) });

    public async Task<MailOutbox> PendingAsync(string environmentCode, string email, int attemptCount = 0)
    {
        await using var db = NewDb();
        var number = Interlocked.Increment(ref sequence);
        var id = Guid.NewGuid();
        var row = MailOutbox.CreatePending(
            environmentCode,
            NotificationEventCodes.ImportCompleted,
            $"EA3:{id:D}",
            Now,
            "EA3",
            id.ToString("D"),
            $"USER:{100000 + number}",
            null,
            null,
            email,
            "ImportCompleted.v1",
            "{\"version\":1,\"reference\":\"EA3\"}",
            id);
        row.AttemptCount = attemptCount;
        db.Set<MailOutbox>().Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    public async Task<MailOutbox> TerminalAsync(string status)
    {
        await using var db = NewDb();
        var number = Interlocked.Increment(ref sequence);
        var id = Guid.NewGuid();
        var row = MailOutbox.CreatePending(
            LiveEnvironment,
            NotificationEventCodes.ImportCompleted,
            $"EA3:{id:D}",
            Now,
            "EA3",
            id.ToString("D"),
            $"USER:{100000 + number}",
            null,
            null,
            "terminal@example.test",
            "ImportCompleted.v1",
            "{\"version\":1,\"reference\":\"EA3\"}",
            id);
        row.Status = status;
        row.FinalizedAt = Now;
        if (status == "Sent") row.SentAt = Now;
        db.Set<MailOutbox>().Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    private static NotificationEnvironmentPolicy Policy(string code, string mode, bool enabled) => new()
    {
        EnvironmentCode = code,
        EmailMode = mode,
        IsEnabled = enabled,
        SenderIdentityReference = "EA3-TEST-SENDER",
        UpdatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}

sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset current = initial;
    public override DateTimeOffset GetUtcNow() => current;
    public void Advance(TimeSpan amount) => current = current.Add(amount);
}

sealed class RecordingProvider : INotificationEmailProvider
{
    private readonly Queue<NotificationProviderResult> results;

    public RecordingProvider(params NotificationProviderResult[] results)
    {
        this.results = new Queue<NotificationProviderResult>(results);
    }

    public string ProviderName => "EA3 Test Provider";
    public List<NotificationProviderRequest> Requests { get; } = [];
    public Func<NotificationProviderRequest, CancellationToken, Task>? BeforeResult { get; init; }

    public async Task<NotificationProviderResult> SendAsync(NotificationProviderRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        if (BeforeResult is not null) await BeforeResult(request, ct);
        return results.Count > 0 ? results.Dequeue() : NotificationProviderResult.Success("default");
    }
}

sealed class ReservationFailingStore(INotificationDeliveryStore inner) : INotificationDeliveryStore
{
    public Task<NotificationDeliveryClaim?> TryClaimNextAsync(DateTime now, CancellationToken ct) => inner.TryClaimNextAsync(now, ct);
    public Task<bool> TryCancelAsync(long id, Guid token, DateTime now, string code, CancellationToken ct) => inner.TryCancelAsync(id, token, now, code, ct);
    public Task<bool> TryFailExhaustedAsync(long id, Guid token, DateTime now, CancellationToken ct) => inner.TryFailExhaustedAsync(id, token, now, ct);
    public Task<NotificationAttemptReservation?> TryReserveAttemptAsync(long id, Guid token, int count, DateTime now, CancellationToken ct)
        => throw new InjectedFailureException();
    public Task<bool> OwnsReservationAsync(NotificationAttemptReservation reservation, DateTime now, CancellationToken ct)
        => inner.OwnsReservationAsync(reservation, now, ct);
    public Task<bool> TryRecordResultAsync(NotificationDeliveryCompletion completion, CancellationToken ct)
        => inner.TryRecordResultAsync(completion, ct);
}

sealed class ResultFailingStore(INotificationDeliveryStore inner) : INotificationDeliveryStore
{
    public Task<NotificationDeliveryClaim?> TryClaimNextAsync(DateTime now, CancellationToken ct) => inner.TryClaimNextAsync(now, ct);
    public Task<bool> TryCancelAsync(long id, Guid token, DateTime now, string code, CancellationToken ct) => inner.TryCancelAsync(id, token, now, code, ct);
    public Task<bool> TryFailExhaustedAsync(long id, Guid token, DateTime now, CancellationToken ct) => inner.TryFailExhaustedAsync(id, token, now, ct);
    public Task<NotificationAttemptReservation?> TryReserveAttemptAsync(long id, Guid token, int count, DateTime now, CancellationToken ct)
        => inner.TryReserveAttemptAsync(id, token, count, now, ct);
    public Task<bool> OwnsReservationAsync(NotificationAttemptReservation reservation, DateTime now, CancellationToken ct)
        => inner.OwnsReservationAsync(reservation, now, ct);
    public Task<bool> TryRecordResultAsync(NotificationDeliveryCompletion completion, CancellationToken ct)
        => throw new InjectedFailureException();
}

sealed class InjectedFailureException : Exception { }
