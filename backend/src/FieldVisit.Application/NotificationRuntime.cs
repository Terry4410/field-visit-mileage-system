using System.Text.Json;
using System.Text.Json.Serialization;

namespace FieldVisit.Application;

public static class NotificationCategories
{
    public const string Transaction = "Transaction";
    public const string Reminder = "Reminder";
    public const string System = "System";

    public static bool IsKnown(string? value)
        => string.Equals(value, Transaction, StringComparison.Ordinal)
           || string.Equals(value, Reminder, StringComparison.Ordinal)
           || string.Equals(value, System, StringComparison.Ordinal);
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

    private static readonly string[] FrozenCodes =
    [
        TripSubmitted,
        TripApproved,
        TripReturned,
        CorrectionRequested,
        CorrectionApproved,
        CorrectionReturned,
        LocationReviewRequested,
        LocationApproved,
        LocationReturned,
        EmploymentAuthorizationExpiring,
        DeploymentSiteChangeEffective,
        ProjectExpiring,
        ImportCompleted,
        ImportFailed
    ];

    private static readonly HashSet<string> FrozenSet = new(FrozenCodes, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> All => Array.AsReadOnly(FrozenCodes);

    public static string Validate(string? eventCode)
    {
        if (string.IsNullOrWhiteSpace(eventCode) || !FrozenSet.Contains(eventCode))
            throw new InvalidOperationException($"Unknown notification event code: {eventCode}");
        return eventCode;
    }
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
/// E-A1 typed/versioned immutable payload foundation. Raw JSON/property-bag authority is forbidden.
/// NotificationSettings.TemplateCode selects the authoritative version at enqueue time.
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

/// <summary>
/// Caller-side, provider-aware translation for the two MailOutbox uniqueness
/// authorities.  The translator is intentionally separate from the outbox
/// writer: the writer only tracks rows, while the transaction owner decides
/// whether a failed SaveChanges may be recovered and retried.
/// </summary>
public interface INotificationCollisionTranslator
{
    Task<bool> TryTranslateAsync(Exception exception, CancellationToken ct);
}

public static class NotificationSaveChanges
{
    /// <summary>
    /// Saves caller-owned business state and, only for a verified equivalent
    /// concurrent MailOutbox collision, detaches the losing notification row
    /// and retries the business save.  Every other failure is rethrown.
    /// </summary>
    public static async Task<int> SaveAsync(
        IUnitOfWork uow,
        INotificationCollisionTranslator? collisionTranslator,
        CancellationToken ct)
    {
        try
        {
            return await uow.SaveChangesAsync(ct);
        }
        catch (Exception exception)
        {
            if (collisionTranslator is null
                || !await collisionTranslator.TryTranslateAsync(exception, ct))
                throw;

            return await uow.SaveChangesAsync(ct);
        }
    }
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
}

public static class NotificationEmailAuthority
{
    private const string LocalSpecials = "!#$%&'*+-/=?^_`{|}~.";

    /// <summary>
    /// Deterministic v1.8 email-usability authority. Null/blank/malformed values are unusable.
    /// Only usable values are returned normalized for delivery/deduplication.
    /// </summary>
    public static string? NormalizeUsable(string? email)
    {
        var value = email?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 320)
            return null;
        if (value.Any(char.IsWhiteSpace) || value.Any(char.IsControl))
            return null;

        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
            return null;

        var local = value[..at];
        var domain = value[(at + 1)..];
        if (local.Length > 64 || domain.Length > 253)
            return null;
        if (local.StartsWith(".", StringComparison.Ordinal)
            || local.EndsWith(".", StringComparison.Ordinal)
            || local.Contains("..", StringComparison.Ordinal))
            return null;
        if (!local.All(c => char.IsLetterOrDigit(c) || LocalSpecials.Contains(c)))
            return null;

        if (!domain.Contains('.'))
            return null;
        var labels = domain.Split('.');
        if (labels.Any(label =>
                label.Length == 0
                || label.Length > 63
                || label[0] == '-'
                || label[^1] == '-'
                || !label.All(c => char.IsLetterOrDigit(c) || c == '-')))
            return null;

        return value.ToLowerInvariant();
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
        var version = GetRequiredTemplateVersion(templateCode);
        if (payload.Version != version)
            throw PayloadMismatch(templateCode, payload.Version, version);

        return (version, payload) switch
        {
            (1, NotificationTemplatePayloadV1 v1) => JsonSerializer.Serialize(
                new PayloadEnvelopeV1(v1.Version, Required(v1.Reference), v1.Detail), JsonOptions),
            _ => throw PayloadMismatch(templateCode, payload.Version, version)
        };
    }

    private static int GetRequiredTemplateVersion(string? templateCode)
    {
        var value = templateCode?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Notification payload-contract/version mismatch: TemplateCode is blank.");

        var marker = value.LastIndexOf(".v", StringComparison.Ordinal);
        if (marker <= 0 || marker + 2 >= value.Length
            || !int.TryParse(value[(marker + 2)..], out var version)
            || version <= 0)
            throw new InvalidOperationException($"Notification payload-contract/version mismatch: TemplateCode '{value}' is not versioned.");

        return version;
    }

    private static InvalidOperationException PayloadMismatch(string? templateCode, int payloadVersion, int templateVersion)
        => new($"Notification payload-contract/version mismatch: TemplateCode '{templateCode}' requires v{templateVersion}, payload is v{payloadVersion}.");

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
