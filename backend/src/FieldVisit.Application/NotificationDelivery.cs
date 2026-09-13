namespace FieldVisit.Application;

public static class NotificationDeliveryAuthority
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    public static TimeSpan RetryDelay(int reservedAttemptNumber) => reservedAttemptNumber switch
    {
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        _ => TimeSpan.Zero
    };

    public static string ProviderIdempotencyKey(long mailOutboxId)
    {
        if (mailOutboxId <= 0) throw new ArgumentOutOfRangeException(nameof(mailOutboxId));
        return $"MAILOUTBOX:{mailOutboxId}";
    }

    public static string SanitizeProviderName(string? value)
        => Sanitize(value, "UNCONFIGURED", 80, prefix: null);

    public static string SanitizeErrorCode(string? value, string fallback)
        => Sanitize(value, fallback, 100, "PROVIDER_");

    public static string? SanitizeMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return cleaned.Length <= 2000 ? cleaned : cleaned[..2000];
    }

    public static string? SanitizeProviderMessageId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return cleaned.Length <= 200 ? cleaned : cleaned[..200];
    }

    private static string Sanitize(string? value, string fallback, int maxLength, string? prefix)
    {
        var normalized = new string((value ?? "")
            .Trim()
            .ToUpperInvariant()
            .Select(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ? c : '_')
            .ToArray())
            .Trim('_');
        if (string.IsNullOrWhiteSpace(normalized)) normalized = fallback;
        if (prefix is not null && !normalized.StartsWith(prefix, StringComparison.Ordinal))
            normalized = prefix + normalized;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}

public static class NotificationDeliveryErrorCodes
{
    public const string PolicyMissing = "POLICY_MISSING";
    public const string PolicyInvalid = "POLICY_INVALID";
    public const string PolicyDisabled = "POLICY_DISABLED";
    public const string TestRecipientNotAllowlisted = "TEST_RECIPIENT_NOT_ALLOWLISTED";
    public const string UatLiveForbidden = "UAT_LIVE_FORBIDDEN";
    public const string AttemptsExhausted = "ATTEMPTS_EXHAUSTED";
    public const string ProviderNotConfigured = "PROVIDER_NOT_CONFIGURED";
    public const string ProviderTimeout = "PROVIDER_TIMEOUT";
    public const string ProviderNetwork = "PROVIDER_NETWORK";
    public const string ProviderUnexpected = "PROVIDER_UNEXPECTED";
}

public sealed record NotificationDeliveryClaim(
    long MailOutboxId,
    string EnvironmentCode,
    string EventCode,
    string BusinessEventKey,
    DateTime EventOccurredAt,
    string AggregateType,
    string AggregateId,
    string RecipientKey,
    string? RecipientEmail,
    string TemplateCode,
    string TemplateDataJson,
    Guid CorrelationId,
    int AttemptCount,
    Guid ProcessingToken,
    DateTime ProcessingLeaseUntil);

public sealed record NotificationAttemptReservation(
    long MailOutboxId,
    Guid ProcessingToken,
    int AttemptNumber,
    DateTime ProcessingLeaseUntil);

public sealed record NotificationDeliveryPolicyDecision(
    bool IsPermitted,
    string? SenderIdentityReference,
    string? CancellationCode)
{
    public static NotificationDeliveryPolicyDecision Permit(string? senderIdentityReference)
        => new(true, senderIdentityReference, null);

    public static NotificationDeliveryPolicyDecision Cancel(string errorCode)
        => new(false, null, errorCode);
}

public enum NotificationProviderOutcome
{
    Sent,
    RetryableFailure,
    PermanentFailure
}

public sealed record NotificationProviderRequest(
    long MailOutboxId,
    string RecipientEmail,
    string TemplateCode,
    string TemplateDataJson,
    string? SenderIdentityReference,
    string IdempotencyKey,
    int ReservedAttemptNumber,
    Guid CorrelationId);

public sealed record NotificationProviderResult(
    NotificationProviderOutcome Outcome,
    string? ProviderMessageId,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static NotificationProviderResult Success(string? providerMessageId)
        => new(NotificationProviderOutcome.Sent, providerMessageId, null, null);

    public static NotificationProviderResult Retryable(string errorCode, string? errorMessage = null)
        => new(NotificationProviderOutcome.RetryableFailure, null, errorCode, errorMessage);

    public static NotificationProviderResult Permanent(string errorCode, string? errorMessage = null)
        => new(NotificationProviderOutcome.PermanentFailure, null, errorCode, errorMessage);
}

public sealed record NotificationDeliveryCompletion(
    long MailOutboxId,
    Guid ProcessingToken,
    int AttemptNumber,
    DateTime CompletedAt,
    string Provider,
    NotificationProviderResult ProviderResult);

public interface INotificationEmailProvider
{
    string ProviderName { get; }
    Task<NotificationProviderResult> SendAsync(NotificationProviderRequest request, CancellationToken ct);
}

public interface INotificationDeliveryPolicyEvaluator
{
    Task<NotificationDeliveryPolicyDecision> EvaluateAsync(NotificationDeliveryClaim claim, CancellationToken ct);
}

public interface INotificationDeliveryStore
{
    Task<NotificationDeliveryClaim?> TryClaimNextAsync(DateTime now, CancellationToken ct);
    Task<bool> TryCancelAsync(long mailOutboxId, Guid processingToken, DateTime now, string reasonCode, CancellationToken ct);
    Task<bool> TryFailExhaustedAsync(long mailOutboxId, Guid processingToken, DateTime now, CancellationToken ct);
    Task<NotificationAttemptReservation?> TryReserveAttemptAsync(
        long mailOutboxId,
        Guid processingToken,
        int expectedAttemptCount,
        DateTime now,
        CancellationToken ct);
    Task<bool> OwnsReservationAsync(NotificationAttemptReservation reservation, DateTime now, CancellationToken ct);
    Task<bool> TryRecordResultAsync(NotificationDeliveryCompletion completion, CancellationToken ct);
}

public interface INotificationDeliveryProcessor
{
    Task<bool> ProcessNextAsync(CancellationToken ct);
}

public sealed class NotificationDeliveryRuntimeOptions
{
    public TimeSpan ProviderTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (ProviderTimeout <= TimeSpan.Zero || ProviderTimeout >= NotificationDeliveryAuthority.LeaseDuration)
            throw new InvalidOperationException("Notification provider timeout must be positive and shorter than the five-minute lease.");
    }
}
