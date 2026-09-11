using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;

var cs = Environment.GetEnvironmentVariable("EA1_SQL_CONNECTION")
    ?? throw new InvalidOperationException("EA1_SQL_CONNECTION is required.");
var fx = new Ea1Fixture(cs);
await fx.SeedAsync();

// R1 + no-save: caller-supplied canonical key is used exactly; writer never commits it.
var r1Ctx = fx.Ctx(NotificationEventCodes.TripApproved, "TRIP:9001:APPROVED:v1", owner: 2001);
var (r1, r1Db) = await fx.QueueAsync(r1Ctx);
if (r1.BusinessEventKey != "TRIP:9001:APPROVED:v1" || r1.Outcome != NotificationQueueOutcomes.Queued || r1.QueuedCount != 1)
    throw new Exception("R1 canonical caller key contract failed.");
await using (var probe = fx.NewDb())
    if (await probe.Set<MailOutbox>().AnyAsync(x => x.BusinessEventKey == r1.BusinessEventKey))
        throw new Exception("Outbox writer called SaveChanges.");
Console.WriteLine("EA1_R1_CALLER_BUSINESS_EVENT_KEY=PASS");
Console.WriteLine("EA1_NO_SAVE_OUTBOX_WRITER=PASS");
await r1Db.SaveChangesAsync();
await r1Db.DisposeAsync();

// Transaction ignores optional preference=false.
await using (var probe = fx.NewDb())
{
    var row = await probe.Set<MailOutbox>().SingleAsync(x => x.BusinessEventKey == r1.BusinessEventKey);
    if (row.TemplateCode != "TripApproved.v1" || row.EnvironmentCode != "UAT" || row.RecipientEmploymentId != 2001)
        throw new Exception("Transaction persisted authority mismatch.");
}
Console.WriteLine("EA1_TRANSACTION_PREF_FALSE=PASS");
Console.WriteLine("EA1_ENVIRONMENT_BINDING=PASS");

// R4: disabled setting is structured Suppressed, zero outbox, non-exceptional.
var (disabled, disabledDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.ProjectExpiring, "PROJECT:77:EXPIRING:v1"));
if (disabled.Outcome != NotificationQueueOutcomes.Suppressed || disabled.QueuedCount != 0)
    throw new Exception("Disabled setting did not return Suppressed.");
await disabledDb.SaveChangesAsync();
await disabledDb.DisposeAsync();
Console.WriteLine("EA1_R4_DISABLED_SUPPRESSED=PASS");

// R5: active rule but zero resolved recipients => structured NoRecipient.
var (zeroRecipient, zeroDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.TripApproved, "TRIP:9002:APPROVED:v1"));
if (zeroRecipient.Outcome != NotificationQueueOutcomes.NoRecipient || zeroRecipient.QueuedCount != 0)
    throw new Exception("Zero recipient did not return NoRecipient.");
await zeroDb.SaveChangesAsync();
await zeroDb.DisposeAsync();
Console.WriteLine("EA1_R5_ZERO_RECIPIENT=PASS");

// R5: no active recipient rule => structured NoRecipient, then restore canonical seed state.
await using (var db = fx.NewDb())
    await db.Database.ExecuteSqlRawAsync("UPDATE r SET IsActive=0 FROM dbo.NotificationSettingRecipients r JOIN dbo.NotificationSettings s ON s.NotificationSettingId=r.NotificationSettingId WHERE s.EventCode=N'TripApproved';");
var (noRule, noRuleDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.TripApproved, "TRIP:9003:APPROVED:v1", owner: 2001));
if (noRule.Outcome != NotificationQueueOutcomes.NoRecipient || noRule.QueuedCount != 0)
    throw new Exception("Missing active rule did not return NoRecipient.");
await noRuleDb.DisposeAsync();
await using (var db = fx.NewDb())
    await db.Database.ExecuteSqlRawAsync("UPDATE r SET IsActive=1 FROM dbo.NotificationSettingRecipients r JOIN dbo.NotificationSettings s ON s.NotificationSettingId=r.NotificationSettingId WHERE s.EventCode=N'TripApproved' AND r.RecipientRuleCode=N'TripOwner';");
Console.WriteLine("EA1_R5_MISSING_RULE=PASS");

// Reminder preference=false suppresses; preference=true queues.
var (remOff, remOffDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.DeploymentSiteChangeEffective, "EMP:2005:SITE-EFFECTIVE:v1", affected: 2005));
if (remOff.Outcome != NotificationQueueOutcomes.Suppressed || remOff.QueuedCount != 0 || remOff.PreferenceSuppressedCount != 1)
    throw new Exception("Reminder false preference did not suppress.");
await remOffDb.SaveChangesAsync(); await remOffDb.DisposeAsync();
Console.WriteLine("EA1_REMINDER_PREF_FALSE=PASS");
var (remOn, remOnDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.DeploymentSiteChangeEffective, "EMP:2006:SITE-EFFECTIVE:v1", affected: 2006));
if (remOn.Outcome != NotificationQueueOutcomes.Queued || remOn.QueuedCount != 1 || remOn.PreferenceSuppressedCount != 0)
    throw new Exception("Reminder true preference did not queue.");
await remOnDb.SaveChangesAsync(); await remOnDb.DisposeAsync();
Console.WriteLine("EA1_REMINDER_PREF_TRUE=PASS");

// System ignores optional preference through Initiator -> linked Employment(false).
var (systemResult, systemDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.ImportCompleted, "IMPORT:44:COMPLETED:v1", initiator: 1001));
if (systemResult.Outcome != NotificationQueueOutcomes.Queued || systemResult.QueuedCount != 1 || systemResult.PreferenceSuppressedCount != 0)
    throw new Exception("System optional-preference contract failed.");
await systemDb.SaveChangesAsync(); await systemDb.DisposeAsync();
Console.WriteLine("EA1_SYSTEM_PREF_FALSE=PASS");

// R2: User identity without UserIdentityProfile still canonicalizes to EMP through LegacyUserId.
var (legacyIdentity, legacyDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.ImportCompleted, "IMPORT:45:COMPLETED:v1", initiator: 1002));
var legacyTracked = legacyDb.ChangeTracker.Entries<MailOutbox>().Single().Entity;
if (legacyTracked.RecipientKey != "EMP:2008" || legacyTracked.RecipientEmploymentId != 2008 || legacyTracked.RecipientUserId != 1002)
    throw new Exception("Employment did not win over USER fallback for same logical person.");
await legacyDb.SaveChangesAsync(); await legacyDb.DisposeAsync();
Console.WriteLine("EA1_R2_EMPLOYMENT_IDENTITY_WINS=PASS");

// Event-time leader/delegation resolution excludes future leader; normalized-email dedup picks lowest EMP key.
var (leaders, leaderDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.TripSubmitted, "TRIP:9004:SUBMITTED:v1", team: 10));
if (leaders.ResolvedRecipientCount != 2 || leaders.QueuedCount != 1)
    throw new Exception($"Expected two resolved current recipients deduped to one email; got {leaders.ResolvedRecipientCount}/{leaders.QueuedCount}.");
var leaderTracked = leaderDb.ChangeTracker.Entries<MailOutbox>().Single().Entity;
if (leaderTracked.RecipientKey != "EMP:2002")
    throw new Exception("Normalized-email dedup did not choose lowest canonical RecipientKey.");
await leaderDb.SaveChangesAsync(); await leaderDb.DisposeAsync();
Console.WriteLine("EA1_EVENT_TIME_RECIPIENT_RESOLUTION=PASS");
Console.WriteLine("EA1_R3_EMAIL_DEDUP_LOWEST_KEY=PASS");

// R3: full-set canonicalization is query-order independent and employment-backed beats USER fallback.
var duplicateRecipientsForward = new NotificationRecipient[]
{
    new(NotificationRecipientRuleCodes.Initiator, "USER:1011", null, 1011, "dedup@example.invalid", true),
    new(NotificationRecipientRuleCodes.Initiator, "EMP:2011", 2011, 1011, "DEDUP@example.invalid", true)
};
var duplicateRecipientsReverse = duplicateRecipientsForward.Reverse().ToArray();
var (dedupForward, dedupForwardDb) = await fx.QueueAsync(
    fx.Ctx(NotificationEventCodes.ImportCompleted, "IMPORT:46:COMPLETED:v1", initiator: 1011),
    new StaticRecipientResolver(duplicateRecipientsForward));
var selectedForward = dedupForwardDb.ChangeTracker.Entries<MailOutbox>().Single().Entity.RecipientKey;
await dedupForwardDb.DisposeAsync();
var (dedupReverse, dedupReverseDb) = await fx.QueueAsync(
    fx.Ctx(NotificationEventCodes.ImportCompleted, "IMPORT:47:COMPLETED:v1", initiator: 1011),
    new StaticRecipientResolver(duplicateRecipientsReverse));
var selectedReverse = dedupReverseDb.ChangeTracker.Entries<MailOutbox>().Single().Entity.RecipientKey;
await dedupReverseDb.DisposeAsync();
if (selectedForward != "EMP:2011" || selectedReverse != "EMP:2011" || dedupForward.QueuedCount != 1 || dedupReverse.QueuedCount != 1)
    throw new Exception("Recipient canonicalization depends on query order or failed Employment precedence.");
Console.WriteLine("EA1_R3_QUERY_ORDER_INDEPENDENT=PASS");

// Administrator resolution is organization-scoped and event-time effective.
var (locationReview, locationDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.LocationReviewRequested, "LOCATION:88:REVIEW-REQUESTED:v1", team: 10));
if (locationReview.ResolvedRecipientCount != 2 || locationReview.QueuedCount != 2)
    throw new Exception($"Expected current team leader + organization admin; got {locationReview.ResolvedRecipientCount}/{locationReview.QueuedCount}.");
await locationDb.SaveChangesAsync(); await locationDb.DisposeAsync();
await using (var probe = fx.NewDb())
{
    var emails = await probe.Set<MailOutbox>()
        .Where(x => x.BusinessEventKey == locationReview.BusinessEventKey)
        .Select(x => x.RecipientEmail)
        .OrderBy(x => x)
        .ToListAsync();
    if (!emails.SequenceEqual(new string?[] { "admin@example.invalid", "shared-leader@example.invalid" }))
        throw new Exception("Administrator organization/event-time authority failed.");
}
Console.WriteLine("EA1_ADMIN_EVENT_TIME_RESOLUTION=PASS");

// R6: typed/versioned payload is serialized at enqueue time with deterministic immutable JSON.
var typedPayload = new NotificationTemplatePayloadV1("REF-9005", "original");
var (payloadResult, payloadDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.TripReturned, "TRIP:9005:RETURNED:v1", owner: 2001, payload: typedPayload));
var payloadTracked = payloadDb.ChangeTracker.Entries<MailOutbox>().Single().Entity;
if (payloadTracked.TemplateDataJson != "{\"version\":1,\"reference\":\"REF-9005\",\"detail\":\"original\"}")
    throw new Exception($"Typed payload JSON mismatch: {payloadTracked.TemplateDataJson}");
await payloadDb.SaveChangesAsync(); await payloadDb.DisposeAsync();
Console.WriteLine("EA1_R6_TYPED_VERSIONED_PAYLOAD=PASS");

// Fixed template drift remains fail-closed.
await using (var driftDb = fx.NewDb())
    await driftDb.Database.ExecuteSqlRawAsync("UPDATE dbo.NotificationSettings SET TemplateCode=N'TripApproved.v9' WHERE EventCode=N'TripApproved';");
var driftRejected = false;
var driftWriterDb = fx.NewDb();
try
{
    var writer = new EfNotificationOutboxWriter(
        driftWriterDb,
        new EfNotificationRecipientResolver(driftWriterDb),
        new NotificationRuntimeEnvironment("UAT"),
        new FixedTimeProvider(new DateTimeOffset(fx.ProcessingAt, TimeSpan.Zero)));
    await writer.QueueAsync(fx.Ctx(NotificationEventCodes.TripApproved, "TRIP:9006:APPROVED:v1", owner: 2001), CancellationToken.None);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("setting drift", StringComparison.OrdinalIgnoreCase))
{
    driftRejected = true;
}
await driftWriterDb.DisposeAsync();
if (!driftRejected) throw new Exception("Fixed template drift was not rejected.");
await using (var restore = fx.NewDb())
    await restore.Database.ExecuteSqlRawAsync("UPDATE dbo.NotificationSettings SET TemplateCode=N'TripApproved.v1' WHERE EventCode=N'TripApproved';");
Console.WriteLine("EA1_FIXED_TEMPLATE_AUTHORITY=PASS");

// R7: Employment.Email unusable -> User.Email fallback, retaining EMP identity.
var (fallback, fallbackDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.DeploymentSiteChangeEffective, "EMP:2009:SITE-EFFECTIVE:v1", affected: 2009));
var fallbackTracked = fallbackDb.ChangeTracker.Entries<MailOutbox>().Single().Entity;
if (fallbackTracked.RecipientKey != "EMP:2009" || fallbackTracked.RecipientEmail != "fallback@example.invalid" || fallbackTracked.Status != "Pending")
    throw new Exception("Employment -> User email fallback contract failed.");
await fallbackDb.SaveChangesAsync(); await fallbackDb.DisposeAsync();
Console.WriteLine("EA1_R7_USER_EMAIL_FALLBACK=PASS");

// R7: no usable Employment/User email -> terminal Failed evidence, no provider attempts.
var (unusable, unusableDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.DeploymentSiteChangeEffective, "EMP:2010:SITE-EFFECTIVE:v1", affected: 2010));
var unusableTracked = unusableDb.ChangeTracker.Entries<MailOutbox>().Single().Entity;
if (unusable.Outcome != NotificationQueueOutcomes.Queued
    || unusable.UnusableEmailEvidenceCount != 1
    || unusableTracked.Status != "Failed"
    || unusableTracked.RecipientEmail is not null
    || unusableTracked.FinalizedAt != fx.ProcessingAt
    || unusableTracked.LastErrorCode != "RECIPIENT_EMAIL_UNUSABLE"
    || unusableTracked.AttemptCount != 0)
    throw new Exception("Unusable-email terminal evidence contract failed.");
await unusableDb.SaveChangesAsync(); await unusableDb.DisposeAsync();
await using (var probe = fx.NewDb())
{
    var row = await probe.Set<MailOutbox>().SingleAsync(x => x.BusinessEventKey == unusable.BusinessEventKey);
    if (await probe.Set<MailDeliveryLog>().AnyAsync(x => x.MailOutboxId == row.MailOutboxId))
        throw new Exception("Unusable-email evidence unexpectedly created a provider attempt.");
}
Console.WriteLine("EA1_R7_UNUSABLE_EMAIL_FAILED_EVIDENCE=PASS");

// R8: sequential duplicate is detected durably and returns idempotent success; no SaveChanges/collision translation here.
var retryContext = fx.Ctx(NotificationEventCodes.TripReturned, "TRIP:9007:RETURNED:v1", owner: 2001);
var (retry1, retry1Db) = await fx.QueueAsync(retryContext);
await retry1Db.SaveChangesAsync(); await retry1Db.DisposeAsync();
var (retry2, retry2Db) = await fx.QueueAsync(retryContext);
if (retry2.Outcome != NotificationQueueOutcomes.Idempotent || retry2.ExistingCount != 1 || retry2.QueuedCount != 0)
    throw new Exception("Sequential durable duplicate did not return idempotent success.");
if (retry2Db.ChangeTracker.Entries<MailOutbox>().Any())
    throw new Exception("Sequential duplicate incorrectly added another outbox row.");
await retry2Db.DisposeAsync();
Console.WriteLine("EA1_R8_SEQUENTIAL_IDEMPOTENCY=PASS");

// EF mapping for corrected 1800_006 preference/RowVersion.
await using (var finalDb = fx.NewDb())
{
    var employment = await finalDb.Employments.AsNoTracking().SingleAsync(x => x.EmploymentId == 2001);
    var setting = await finalDb.Set<NotificationSetting>().AsNoTracking().SingleAsync(x => x.EventCode == NotificationEventCodes.TripApproved);
    if (employment.OptionalEmailNotificationEnabled || setting.RowVersion.Length != 8)
        throw new Exception("EF mapping for preference/RowVersion failed.");
}
Console.WriteLine("EA1_EF_MAPPING=PASS");
Console.WriteLine("EA1_NOTIFICATION_RUNTIME_CORE=21/21=PASS");
