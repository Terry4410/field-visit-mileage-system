using System.Text.Json;
using System.Text.Json.Serialization;

namespace FieldVisit.Application;

public static class NotificationCategories
{
    public const string Transaction = "Transaction";
    public const string Reminder = "Reminder";
    public const string System = "System";
}

public static class NotificationEventCodes
{
    public const string TripSubmitted = "TripSubmitted";
    public const string TripApproved = "TripApproved";
    public const string TripReturned = "TripReturned";
    public const string CorrectionRequested = "CorrectionRequested";
    public const string CorrectionApproved = "CorrectionApproved";
    public const string CorrectionReturned = "CorrectionReturned";
    public const string LocationReviewRequested = "LocationReviewRequested";
    public const string LocationApproved = "LocationApproved";
    public const string LocationReturned = "LocationReturned";
    public const string EmploymentAuthorizationExpiring = "EmploymentAuthorizationExpiring";
    public const string DeploymentSiteChangeEffective = "DeploymentSiteChangeEffective";
    public const string ProjectExpiring = "ProjectExpiring";
    public const string ImportCompleted = "ImportCompleted";
    public const string ImportFailed = "ImportFailed";
}

public static class NotificationRecipientRuleCodes
{
    public const string TeamLeader = "TeamLeader";
    public const string DelegatedLeader = "DelegatedLeader";
    public const string TripOwner = "TripOwner";
    public const string Administrator = "Administrator";
    public const string Initiator = "Initiator";
    public const string AffectedEmployment = "AffectedEmployment";
    public const string ProjectManager = "ProjectManager";
}

public static class NotificationQueueOutcomes
{
    public const string Queued = "Queued";
    public const string Suppressed = "Suppressed";
    public const string NoRecipient = "NoRecipient";
    public const string Idempotent = "Idempotent";
}

public sealed record NotificationEventDefinition(
    string EventCode,
    string Category,
    bool HonorsOptionalPreference,
    string TemplateCode);

public static class NotificationEventCatalog
{
    private static readonly IReadOnlyDictionary<string, NotificationEventDefinition> ByCode =
        new Dictionary<string, NotificationEventDefinition>(StringComparer.Ordinal)
        {
            [NotificationEventCodes.TripSubmitted] = Tx(NotificationEventCodes.TripSubmitted),
            [NotificationEventCodes.TripApproved] = Tx(NotificationEventCodes.TripApproved),
            [NotificationEventCodes.TripReturned] = Tx(NotificationEventCodes.TripReturned),
            [NotificationEventCodes.CorrectionRequested] = Tx(NotificationEventCodes.CorrectionRequested),
            [NotificationEventCodes.CorrectionApproved] = Tx(NotificationEventCodes.CorrectionApproved),
            [NotificationEventCodes.CorrectionReturned] = Tx(NotificationEventCodes.CorrectionReturned),
            [NotificationEventCodes.LocationReviewRequested] = Tx(NotificationEventCodes.LocationReviewRequested),
            [NotificationEventCodes.LocationApproved] = Tx(NotificationEventCodes.LocationApproved),
            [NotificationEventCodes.LocationReturned] = Tx(NotificationEventCodes.LocationReturned),
            [NotificationEventCodes.EmploymentAuthorizationExpiring] = Reminder(NotificationEventCodes.EmploymentAuthorizationExpiring),
            [NotificationEventCodes.DeploymentSiteChangeEffective] = Reminder(NotificationEventCodes.DeploymentSiteChangeEffective),
            [NotificationEventCodes.ProjectExpiring] = Reminder(NotificationEventCodes.ProjectExpiring),
            [NotificationEventCodes.ImportCompleted] = SystemEvent(NotificationEventCodes.ImportCompleted),
            [NotificationEventCodes.ImportFailed] = SystemEvent(NotificationEventCodes.ImportFailed)
        };

    public static IReadOnlyCollection<NotificationEventDefinition> All => ByCode.Values.ToArray();

    public static NotificationEventDefinition GetRequired(string eventCode)
        => ByCode.TryGetValue(eventCode, out var definition)
            ? definition
            : throw new InvalidOperationException($"Unknown notification event code: {eventCode}");

    private static NotificationEventDefinition Tx(string code) => new(code, NotificationCategories.Transaction, false, $"{code}.v1");
    private static NotificationEventDefinition Reminder(string code) => new(code, NotificationCategories.Reminder, true, $"{code}.v1");
    private static NotificationEventDefinition SystemEvent(string code) => new(code, NotificationCategories.System, false, $"{code}.v1");
}

public sealed record NotificationRuntimeEnvironment(string EnvironmentCode)
{
    public string GetRequiredCode()
    {
        var value = (EnvironmentCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Notifications:EnvironmentCode is required before notification enqueue is used.");
        if (value.Length > 20)
            throw new InvalidOperationException("Notifications:EnvironmentCode exceeds the 20-character database authority.");
        return value;
    }
}

public interface INotificationTemplatePayload
{
    int Version { get; }
}

/// <summary>
/// E-A1 typed/versioned immutable payload foundation. Business integrations may add
/// additional typed payload records later, but raw JSON/property-bag authority is forbidden.
/// </summary>
public sealed record NotificationTemplatePayloadV1(string Reference, string? Detail = null) : INotificationTemplatePayload
{
    public int Version => 1;
}

public sealed record NotificationEventContext(
    string EventCode,
    string AggregateType,
    string AggregateId,
    string BusinessEventKey,
    DateTime EventOccurredAt,
    int? OrganizationId,
    int? TeamId,
    long? TripOwnerEmploymentId,
    int? InitiatorUserId,
    long? AffectedEmploymentId,
    INotificationTemplatePayload TemplatePayload,
    Guid CorrelationId);

public sealed record NotificationRecipient(
    string RecipientRuleCode,
    string RecipientKey,
    long? EmploymentId,
    int? UserId,
    string? Email,
    bool OptionalEmailNotificationEnabled);

public sealed record NotificationQueueResult(
    string BusinessEventKey,
    string Outcome,
    int RecipientRuleCount,
    int ResolvedRecipientCount,
    int PreferenceSuppressedCount,
    int ExistingCount,
    int QueuedCount,
    int UnusableEmailEvidenceCount);

public interface INotificationRecipientResolver
{
    Task<IReadOnlyList<NotificationRecipient>> ResolveAsync(
        NotificationEventContext context,
        IReadOnlyCollection<string> recipientRuleCodes,
        CancellationToken ct);
}

public interface INotificationOutboxWriter
{
    /// <summary>
    /// Performs durable pre-checks and Adds outbox rows to the caller-owned DbContext only.
    /// This method MUST NOT call SaveChanges; the caller owns the business transaction commit.
    /// </summary>
    Task<NotificationQueueResult> QueueAsync(NotificationEventContext context, CancellationToken ct);
}

public static class NotificationBusinessKeyAuthority
{
    public static string ValidateBusinessEventKey(string? businessEventKey)
    {
        if (string.IsNullOrWhiteSpace(businessEventKey))
            throw new ArgumentException("BusinessEventKey is required.", nameof(businessEventKey));
        if (!string.Equals(businessEventKey, businessEventKey.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("BusinessEventKey must already be canonical and must not contain leading/trailing whitespace.", nameof(businessEventKey));
        if (businessEventKey.Length > 200)
            throw new ArgumentException("BusinessEventKey exceeds 200 characters.", nameof(businessEventKey));
        return businessEventKey;
    }

    public static string ForEmployment(long employmentId)
    {
        if (employmentId <= 0) throw new ArgumentOutOfRangeException(nameof(employmentId));
        return $"EMP:{employmentId}";
    }

    public static string ForUser(int userId)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        return $"USER:{userId}";
    }

    public static string? NormalizeEmail(string? email)
    {
        var value = email?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value.ToLowerInvariant();
    }
}

public static class NotificationTemplatePayloadAuthority
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize(string templateCode, INotificationTemplatePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!templateCode.EndsWith(".v1", StringComparison.Ordinal) || payload.Version != 1)
            throw new InvalidOperationException($"Template payload version does not match fixed template authority: {templateCode}.");

        return payload switch
        {
            NotificationTemplatePayloadV1 v1 => JsonSerializer.Serialize(
                new PayloadEnvelopeV1(v1.Version, Required(v1.Reference), v1.Detail), JsonOptions),
            _ => throw new InvalidOperationException($"Unsupported typed notification payload: {payload.GetType().Name}.")
        };
    }

    private static string Required(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("Notification payload Reference is required.");
        return trimmed;
    }

    private sealed record PayloadEnvelopeV1(
        [property: JsonPropertyOrder(0)] int Version,
        [property: JsonPropertyOrder(1)] string Reference,
        [property: JsonPropertyOrder(2)] string? Detail);
}
