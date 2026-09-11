using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class EfNotificationOutboxWriter(
    AppDbContext db,
    INotificationRecipientResolver recipientResolver,
    NotificationRuntimeEnvironment runtimeEnvironment,
    TimeProvider timeProvider) : INotificationOutboxWriter
{
    public async Task<NotificationQueueResult> QueueAsync(NotificationEventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var definition = NotificationEventCatalog.GetRequired(context.EventCode);
        var environmentCode = runtimeEnvironment.GetRequiredCode();
        var businessEventKey = NotificationBusinessKeyAuthority.ValidateBusinessEventKey(context.BusinessEventKey);

        if (!await db.Set<NotificationEnvironmentPolicy>().AsNoTracking()
                .AnyAsync(x => x.EnvironmentCode == environmentCode, ct))
            throw new InvalidOperationException($"Notification environment policy not found: {environmentCode}");

        var setting = await db.Set<NotificationSetting>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.EventCode == definition.EventCode, ct)
            ?? throw new InvalidOperationException($"Notification setting not found: {definition.EventCode}");

        if (!string.Equals(setting.NotificationType, definition.Category, StringComparison.Ordinal)
            || setting.HonorsOptionalPreference != definition.HonorsOptionalPreference
            || !string.Equals(setting.TemplateCode, definition.TemplateCode, StringComparison.Ordinal))
            throw new InvalidOperationException($"Notification setting drift detected for {definition.EventCode}.");

        if (!setting.IsEnabled)
            return Result(businessEventKey, NotificationQueueOutcomes.Suppressed, 0, 0, 0, 0, 0, 0);

        var rules = await db.Set<NotificationSettingRecipient>().AsNoTracking()
            .Where(x => x.NotificationSettingId == setting.NotificationSettingId && x.IsActive)
            .Select(x => x.RecipientRuleCode)
            .ToListAsync(ct);
        if (rules.Count == 0)
            return Result(businessEventKey, NotificationQueueOutcomes.NoRecipient, 0, 0, 0, 0, 0, 0);

        // R3: resolve the complete set first, then canonicalize independently of query order.
        var resolved = await recipientResolver.ResolveAsync(context, rules, ct);
        if (resolved.Count == 0)
            return Result(businessEventKey, NotificationQueueOutcomes.NoRecipient, rules.Count, 0, 0, 0, 0, 0);

        var canonical = CanonicalizeRecipients(resolved);
        var eligible = new List<NotificationRecipient>(canonical.Count);
        var preferenceSuppressed = 0;
        foreach (var recipient in canonical)
        {
            if (definition.HonorsOptionalPreference && !recipient.OptionalEmailNotificationEnabled)
            {
                preferenceSuppressed++;
                continue;
            }
            eligible.Add(recipient);
        }

        if (eligible.Count == 0)
            return Result(businessEventKey, NotificationQueueOutcomes.Suppressed, rules.Count, resolved.Count, preferenceSuppressed, 0, 0, 0);

        var existingRows = await db.Set<MailOutbox>().AsNoTracking()
            .Where(x => x.BusinessEventKey == businessEventKey)
            .Select(x => new { x.RecipientKey, x.NormalizedRecipientEmail })
            .ToListAsync(ct);
        var existingKeys = existingRows.Select(x => x.RecipientKey).ToHashSet(StringComparer.Ordinal);
        var existingEmails = existingRows
            .Where(x => x.NormalizedRecipientEmail != null)
            .Select(x => x.NormalizedRecipientEmail!)
            .ToHashSet(StringComparer.Ordinal);

        var templateDataJson = NotificationTemplatePayloadAuthority.Serialize(definition.TemplateCode, context.TemplatePayload);
        var queuedCount = 0;
        var existingCount = 0;
        var unusableEmailEvidenceCount = 0;
        foreach (var recipient in eligible)
        {
            var normalizedEmail = NotificationBusinessKeyAuthority.NormalizeEmail(recipient.Email);
            if (existingKeys.Contains(recipient.RecipientKey)
                || (normalizedEmail is not null && existingEmails.Contains(normalizedEmail)))
            {
                existingCount++;
                continue;
            }

            MailOutbox row;
            if (normalizedEmail is null)
            {
                row = MailOutbox.CreateRecipientEmailFailure(
                    environmentCode,
                    definition.EventCode,
                    businessEventKey,
                    context.EventOccurredAt,
                    Required(context.AggregateType, nameof(context.AggregateType)),
                    Required(context.AggregateId, nameof(context.AggregateId)),
                    recipient.RecipientKey,
                    recipient.EmploymentId,
                    recipient.UserId,
                    definition.TemplateCode,
                    templateDataJson,
                    context.CorrelationId,
                    timeProvider.GetUtcNow().UtcDateTime);
                unusableEmailEvidenceCount++;
            }
            else
            {
                row = MailOutbox.CreatePending(
                    environmentCode,
                    definition.EventCode,
                    businessEventKey,
                    context.EventOccurredAt,
                    Required(context.AggregateType, nameof(context.AggregateType)),
                    Required(context.AggregateId, nameof(context.AggregateId)),
                    recipient.RecipientKey,
                    recipient.EmploymentId,
                    recipient.UserId,
                    normalizedEmail,
                    definition.TemplateCode,
                    templateDataJson,
                    context.CorrelationId);
            }

            db.Set<MailOutbox>().Add(row);
            existingKeys.Add(recipient.RecipientKey);
            if (normalizedEmail is not null) existingEmails.Add(normalizedEmail);
            queuedCount++;
        }

        var outcome = queuedCount > 0
            ? NotificationQueueOutcomes.Queued
            : existingCount > 0
                ? NotificationQueueOutcomes.Idempotent
                : NotificationQueueOutcomes.NoRecipient;

        // Deliberately no SaveChanges here. E-A2/caller owns the business transaction commit and concurrent collision translation.
        return Result(
            businessEventKey,
            outcome,
            rules.Count,
            resolved.Count,
            preferenceSuppressed,
            existingCount,
            queuedCount,
            unusableEmailEvidenceCount);
    }

    private static List<NotificationRecipient> CanonicalizeRecipients(IReadOnlyList<NotificationRecipient> resolved)
    {
        // Employment-backed identity wins before USER fallback. For otherwise duplicate normalized emails,
        // the lowest canonical RecipientKey wins. Stable ordering makes query order irrelevant.
        var byRecipientKey = resolved
            .OrderBy(x => x.EmploymentId.HasValue ? 0 : 1)
            .ThenBy(x => x.RecipientKey, StringComparer.Ordinal)
            .GroupBy(x => x.RecipientKey, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        var selected = new List<NotificationRecipient>();
        var emails = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipient in byRecipientKey
                     .OrderBy(x => x.EmploymentId.HasValue ? 0 : 1)
                     .ThenBy(x => x.RecipientKey, StringComparer.Ordinal))
        {
            var normalized = NotificationBusinessKeyAuthority.NormalizeEmail(recipient.Email);
            if (normalized is not null && !emails.Add(normalized))
                continue;
            selected.Add(recipient);
        }
        return selected;
    }

    private static string Required(string? value, string name)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) throw new ArgumentException($"{name} is required.", name);
        return trimmed;
    }

    private static NotificationQueueResult Result(
        string businessEventKey,
        string outcome,
        int rules,
        int resolved,
        int preferenceSuppressed,
        int existing,
        int queued,
        int unusableEmailEvidence)
        => new(businessEventKey, outcome, rules, resolved, preferenceSuppressed, existing, queued, unusableEmailEvidence);
}
