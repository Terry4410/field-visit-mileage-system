using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;

var cs = Environment.GetEnvironmentVariable("EA1_SQL_CONNECTION")
    ?? throw new InvalidOperationException("EA1_SQL_CONNECTION is required.");
var fx = new Ea1Fixture(cs);
await fx.SeedAsync();

// C1 malformed-email fixtures. Kept inside the real relational harness so FK/evidence behavior is exercised.
await using (var seed = fx.NewDb())
{
    await seed.Database.ExecuteSqlRawAsync("""
INSERT dbo.Users(UserId,OrganizationId,TeamId,EmployeeNo,DisplayName,Email,IsActive,CreatedAt) VALUES
(1005,1,10,N'U1005',N'Valid Fallback',N'valid-fallback@example.invalid',1,SYSUTCDATETIME()),
(1006,1,10,N'U1006',N'Malformed User Only',N'not-an-email',1,SYSUTCDATETIME()),
(1007,1,10,N'U1007',N'Malformed Fallback',N'@example.com',1,SYSUTCDATETIME());
INSERT dbo.Employments(EmploymentId,PersonId,OrganizationId,EmployeeNo,Email,HireDate,TerminationDate,LegacyUserId,SourceType,CreatedAt,OptionalEmailNotificationEnabled) VALUES
(2012,12,1,N'E2012',N'not-an-email','2020-01-01',NULL,1005,N'Test',SYSUTCDATETIME(),1),
(2013,13,1,N'E2013',N'user@','2020-01-01',NULL,1007,N'Test',SYSUTCDATETIME(),1);
""");
}

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

// Canonical seed Transaction ignores optional preference=false.
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

// Canonical reminder preference=false suppresses; preference=true queues.
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

// Canonical System ignores optional preference through Initiator -> linked Employment(false).
var (systemResult, systemDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.ImportCompleted, "IMPORT:44:COMPLETED:v1", initiator: 1001));
if (systemResult.Outcome != NotificationQueueOutcomes.Queued || systemResult.QueuedCount != 1 || systemResult.PreferenceSuppressedCount != 0)
    throw new Exception("System optional-preference contract failed.");
await systemDb.SaveChangesAsync(); await systemDb.DisposeAsync();
Console.WriteLine("EA1_SYSTEM_PREF_FALSE=PASS");

// R2: User identity without UserIdentityProfile canonicalizes to EMP through LegacyUserId.
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

// R3: full-set canonicalization is query-order independent and Employment-backed beats USER fallback.
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

// C1: NotificationSettings is sole runtime authority for type/preference/template semantics.
await using (var authorityDb = fx.NewDb())
{
    await authorityDb.Database.ExecuteSqlRawAsync("UPDATE dbo.NotificationSettings SET NotificationType=N'Reminder', HonorsOptionalPreference=1, TemplateCode=N'ApprovalRuntime.v1' WHERE EventCode=N'TripApproved';");
}
var (dbReminder, dbReminderDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.TripApproved, "TRIP:9100:APPROVED:v1", owner: 2001));
if (dbReminder.Outcome != NotificationQueueOutcomes.Suppressed || dbReminder.PreferenceSuppressedCount != 1 || dbReminder.QueuedCount != 0)
    throw new Exception("DB NotificationType/HonorsOptionalPreference did not control preference semantics.");
await dbReminderDb.DisposeAsync();
Console.WriteLine("EA1_DB_NOTIFICATION_TYPE_AUTHORITY=PASS");
Console.WriteLine("EA1_DB_HONORS_OPTIONAL_PREFERENCE_AUTHORITY=PASS");

await using (var authorityDb = fx.NewDb())
{
    await authorityDb.Database.ExecuteSqlRawAsync("UPDATE dbo.NotificationSettings SET NotificationType=N'Transaction', HonorsOptionalPreference=0, TemplateCode=N'ApprovalRuntime.v1' WHERE EventCode=N'TripApproved';");
}
var (dbTemplate, dbTemplateDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.TripApproved, "TRIP:9101:APPROVED:v1", owner: 2001));
var dbTemplateTracked = dbTemplateDb.ChangeTracker.Entries<MailOutbox>().Single().Entity;
if (dbTemplate.QueuedCount != 1 || dbTemplate.PreferenceSuppressedCount != 0 || dbTemplateTracked.TemplateCode != "ApprovalRuntime.v1")
    throw new Exception("DB TemplateCode was not snapshotted into new Outbox.");
var frozenTemplateJson = dbTemplateTracked.TemplateDataJson;
await dbTemplateDb.SaveChangesAsync(); await dbTemplateDb.DisposeAsync();
Console.WriteLine("EA1_DB_TEMPLATE_CODE_AUTHORITY=PASS");

await using (var authorityDb = fx.NewDb())
    await authorityDb.Database.ExecuteSqlRawAsync("UPDATE dbo.NotificationSettings SET TemplateCode=N'ApprovalRuntime.changed.v1' WHERE EventCode=N'TripApproved';");
await using (var probe = fx.NewDb())
{
    var row = await probe.Set<MailOutbox>().SingleAsync(x => x.BusinessEventKey == "TRIP:9101:APPROVED:v1");
    if (row.TemplateCode != "ApprovalRuntime.v1" || row.TemplateDataJson != frozenTemplateJson)
        throw new Exception("Setting mutation rewrote existing Outbox template/payload snapshot.");
}
Console.WriteLine("EA1_DB_TEMPLATE_SNAPSHOT_IMMUTABLE=PASS");

// C1: DB-selected incompatible version fails as payload contract/version mismatch, never setting drift.
await using (var authorityDb = fx.NewDb())
    await authorityDb.Database.ExecuteSqlRawAsync("UPDATE dbo.NotificationSettings SET TemplateCode=N'ApprovalRuntime.v2' WHERE EventCode=N'TripApproved';");
var mismatchDb = fx.NewDb();
var mismatchWriter = new EfNotificationOutboxWriter(
    mismatchDb,
    new EfNotificationRecipientResolver(mismatchDb),
    new NotificationRuntimeEnvironment("UAT"),
    new FixedTimeProvider(new DateTimeOffset(fx.ProcessingAt, TimeSpan.Zero)));
var mismatchRejected = false;
try
{
    await mismatchWriter.QueueAsync(fx.Ctx(NotificationEventCodes.TripApproved, "TRIP:9102:APPROVED:v1", owner: 2001), CancellationToken.None);
}
catch (InvalidOperationException ex) when (
    ex.Message.Contains("payload-contract/version mismatch", StringComparison.OrdinalIgnoreCase)
    && !ex.Message.Contains("setting drift", StringComparison.OrdinalIgnoreCase))
{
    mismatchRejected = true;
}
await mismatchDb.DisposeAsync();
if (!mismatchRejected) throw new Exception("Incompatible DB-selected template version was not rejected as payload-contract/version mismatch.");
Console.WriteLine("EA1_TYPED_PAYLOAD_VERSION_MISMATCH=PASS");

await using (var restore = fx.NewDb())
    await restore.Database.ExecuteSqlRawAsync("UPDATE dbo.NotificationSettings SET NotificationType=N'Transaction', HonorsOptionalPreference=0, TemplateCode=N'TripApproved.v1' WHERE EventCode=N'TripApproved';");

// R6: typed/versioned payload is serialized at enqueue time with deterministic immutable JSON.
var typedPayload = new NotificationTemplatePayloadV1("REF-9005", "original");
var (payloadResult, payloadDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.TripReturned, "TRIP:9005:RETURNED:v1", owner: 2001, payload: typedPayload));
var payloadTracked = payloadDb.ChangeTracker.Entries<MailOutbox>().Single().Entity;
if (payloadTracked.TemplateDataJson != "{\"version\":1,\"reference\":\"REF-9005\",\"detail\":\"original\"}")
    throw new Exception($"Typed payload JSON mismatch: {payloadTracked.TemplateDataJson}");
await payloadDb.SaveChangesAsync(); await payloadDb.DisposeAsync();
Console.WriteLine("EA1_R6_TYPED_VERSIONED_PAYLOAD=PASS");

// C1 R7: malformed Employment.Email + valid User.Email => valid fallback is queued under EMP identity.
var (malformedFallback, malformedFallbackDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.DeploymentSiteChangeEffective, "EMP:2012:SITE-EFFECTIVE:v1", affected: 2012));
var malformedFallbackTracked = malformedFallbackDb.ChangeTracker.Entries<MailOutbox>().Single().Entity;
if (malformedFallbackTracked.RecipientKey != "EMP:2012"
    || malformedFallbackTracked.RecipientEmail != "valid-fallback@example.invalid"
    || malformedFallbackTracked.Status != "Pending")
    throw new Exception("Malformed Employment.Email did not fall back to valid User.Email.");
await malformedFallbackDb.SaveChangesAsync(); await malformedFallbackDb.DisposeAsync();
Console.WriteLine("EA1_R7_MALFORMED_EMPLOYMENT_VALID_USER_FALLBACK=PASS");

// C1 R7: malformed Employment.Email + malformed User.Email => terminal Failed evidence.
var (malformedBoth, malformedBothDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.DeploymentSiteChangeEffective, "EMP:2013:SITE-EFFECTIVE:v1", affected: 2013));
var malformedBothTracked = malformedBothDb.ChangeTracker.Entries<MailOutbox>().Single().Entity;
if (malformedBothTracked.Status != "Failed"
    || malformedBothTracked.RecipientEmail is not null
    || malformedBothTracked.FinalizedAt != fx.ProcessingAt
    || malformedBothTracked.LastErrorCode != "RECIPIENT_EMAIL_UNUSABLE"
    || malformedBothTracked.AttemptCount != 0)
    throw new Exception("Malformed Employment/User emails did not create terminal Failed evidence.");
await malformedBothDb.SaveChangesAsync(); await malformedBothDb.DisposeAsync();
Console.WriteLine("EA1_R7_MALFORMED_EMPLOYMENT_MALFORMED_USER_FAILED=PASS");

// C1 R7: malformed User-only recipient => terminal Failed evidence.
var (malformedUser, malformedUserDb) = await fx.QueueAsync(fx.Ctx(NotificationEventCodes.ImportCompleted, "IMPORT:1006:COMPLETED:v1", initiator: 1006));
var malformedUserTracked = malformedUserDb.ChangeTracker.Entries<MailOutbox>().Single().Entity;
if (malformedUserTracked.RecipientKey != "USER:1006"
    || malformedUserTracked.Status != "Failed"
    || malformedUserTracked.RecipientEmail is not null
    || malformedUserTracked.FinalizedAt != fx.ProcessingAt
    || malformedUserTracked.LastErrorCode != "RECIPIENT_EMAIL_UNUSABLE"
    || malformedUserTracked.AttemptCount != 0)
    throw new Exception("Malformed User-only email did not create terminal Failed evidence.");
await malformedUserDb.SaveChangesAsync(); await malformedUserDb.DisposeAsync();
Console.WriteLine("EA1_R7_MALFORMED_USER_ONLY_FAILED=PASS");

// Existing null/blank R7 path remains terminal Failed with zero provider attempts.
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

// C1: event/enqueue-time freeze. Persist current leader/admin/email/template/payload snapshot first.
var freezeContext = fx.Ctx(
    NotificationEventCodes.LocationReviewRequested,
    "LOCATION:89:REVIEW-REQUESTED:v1",
    team: 10,
    payload: new NotificationTemplatePayloadV1("LOC-89", "frozen"));
var (freezeResult, freezeDb) = await fx.QueueAsync(freezeContext);
if (freezeResult.QueuedCount != 2) throw new Exception("Freeze fixture expected two current recipients.");
var freezeBefore = freezeDb.ChangeTracker.Entries<MailOutbox>()
    .Select(x => x.Entity)
    .OrderBy(x => x.RecipientKey)
    .Select(x => new { x.RecipientKey, x.RecipientEmail, x.TemplateCode, x.TemplateDataJson, x.EventOccurredAt })
    .ToArray();
await freezeDb.SaveChangesAsync(); await freezeDb.DisposeAsync();

await using (var mutate = fx.NewDb())
{
    await mutate.Database.ExecuteSqlRawAsync("""
UPDATE dbo.Employments SET Email=N'leader-new@example.invalid', TerminationDate='2026-09-10' WHERE EmploymentId=2002;
UPDATE dbo.Employments SET Email=N'admin-new@example.invalid', OrganizationId=2 WHERE EmploymentId=2004;
UPDATE dbo.TeamLeaderAssignments SET EffectiveTo='2026-09-10' WHERE TeamLeaderAssignmentId=1;
UPDATE dbo.TeamLeaderAssignments SET EmploymentId=2006, EffectiveFrom='2026-09-11', EffectiveTo=NULL WHERE TeamLeaderAssignmentId=2;
UPDATE dbo.EmploymentRoleAssignments SET EffectiveTo='2026-09-10' WHERE EmploymentRoleAssignmentId=1;
INSERT dbo.EmploymentRoleAssignments(EmploymentRoleAssignmentId,EmploymentId,RoleId,EffectiveFrom,EffectiveTo,CreatedAt)
VALUES(2,2005,1,'2026-09-11',NULL,SYSUTCDATETIME());
UPDATE dbo.NotificationSettings SET TemplateCode=N'LocationReviewRequested.changed.v1' WHERE EventCode=N'LocationReviewRequested';
""");
}

await using (var probe = fx.NewDb())
{
    var frozenRows = await probe.Set<MailOutbox>().AsNoTracking()
        .Where(x => x.BusinessEventKey == freezeContext.BusinessEventKey)
        .OrderBy(x => x.RecipientKey)
        .Select(x => new { x.RecipientKey, x.RecipientEmail, x.TemplateCode, x.TemplateDataJson, x.EventOccurredAt })
        .ToArrayAsync();
    if (frozenRows.Length != freezeBefore.Length)
        throw new Exception("Historical frozen Outbox recipient count changed after master mutation.");
    for (var i = 0; i < frozenRows.Length; i++)
    {
        if (frozenRows[i].RecipientKey != freezeBefore[i].RecipientKey
            || frozenRows[i].RecipientEmail != freezeBefore[i].RecipientEmail
            || frozenRows[i].TemplateCode != freezeBefore[i].TemplateCode
            || frozenRows[i].TemplateDataJson != freezeBefore[i].TemplateDataJson
            || frozenRows[i].EventOccurredAt != freezeBefore[i].EventOccurredAt)
            throw new Exception("Historical recipient/email/template/payload/event-time snapshot was rewritten by current master mutation.");
    }
}
Console.WriteLine("EA1_EVENT_ENQUEUE_FREEZE_MUTATION=PASS");

// A later event resolves today's current authority and snapshots the newly selected DB template.
var (afterMutation, afterMutationDb) = await fx.QueueAsync(fx.Ctx(
    NotificationEventCodes.LocationReviewRequested,
    "LOCATION:90:REVIEW-REQUESTED:v1",
    team: 10,
    payload: new NotificationTemplatePayloadV1("LOC-90", "current")));
var afterRows = afterMutationDb.ChangeTracker.Entries<MailOutbox>()
    .Select(x => x.Entity)
    .OrderBy(x => x.RecipientKey)
    .ToArray();
if (afterMutation.QueuedCount != 2
    || !afterRows.Select(x => x.RecipientKey).SequenceEqual(new[] { "EMP:2005", "EMP:2006" })
    || afterRows.Any(x => x.TemplateCode != "LocationReviewRequested.changed.v1"))
    throw new Exception("New event did not use current event-time recipient and NotificationSettings authority.");
await afterMutationDb.SaveChangesAsync(); await afterMutationDb.DisposeAsync();
Console.WriteLine("EA1_NEW_EVENT_USES_CURRENT_AUTHORITY=PASS");

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
Console.WriteLine("EA1_NOTIFICATION_RUNTIME_CORE_C1=29/29=PASS");
