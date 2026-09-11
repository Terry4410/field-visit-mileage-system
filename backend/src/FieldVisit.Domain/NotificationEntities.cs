namespace FieldVisit.Domain.Entities;

public sealed class NotificationEnvironmentPolicy
{
    public string EnvironmentCode { get; set; } = "";
    public string EmailMode { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string? SenderIdentityReference { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class NotificationEmailAllowlist
{
    public long NotificationEmailAllowlistId { get; set; }
    public string EnvironmentCode { get; set; } = "";
    public string Email { get; set; } = "";
    public string? NormalizedEmail { get; private set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime? InactivatedAt { get; set; }
    public int? InactivatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class NotificationSetting
{
    public int NotificationSettingId { get; set; }
    public string EventCode { get; set; } = "";
    public string NotificationType { get; set; } = "";
    public bool IsEnabled { get; set; }
    public bool HonorsOptionalPreference { get; set; }
    public int? ReminderDays { get; set; }
    public string TemplateCode { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class NotificationSettingRecipient
{
    public int NotificationSettingRecipientId { get; set; }
    public int NotificationSettingId { get; set; }
    public string RecipientRuleCode { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class MailOutbox
{
    public long MailOutboxId { get; set; }
    public string EnvironmentCode { get; private set; } = "";
    public string EventCode { get; private set; } = "";
    public string BusinessEventKey { get; private set; } = "";
    public DateTime EventOccurredAt { get; private set; }
    public string AggregateType { get; private set; } = "";
    public string AggregateId { get; private set; } = "";
    public string RecipientKey { get; private set; } = "";
    public long? RecipientEmploymentId { get; private set; }
    public int? RecipientUserId { get; private set; }
    public string? RecipientEmail { get; private set; }
    public string? NormalizedRecipientEmail { get; private set; }
    public string TemplateCode { get; private set; } = "";
    public string TemplateDataJson { get; private set; } = "{}";
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTime AvailableAt { get; set; }
    public Guid? ProcessingToken { get; set; }
    public DateTime? ProcessingLeaseUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public Guid CorrelationId { get; private set; }

    private MailOutbox() { }

    public static MailOutbox CreatePending(
        string environmentCode,
        string eventCode,
        string businessEventKey,
        DateTime eventOccurredAt,
        string aggregateType,
        string aggregateId,
        string recipientKey,
        long? recipientEmploymentId,
        int? recipientUserId,
        string? recipientEmail,
        string templateCode,
        string templateDataJson,
        Guid correlationId)
        => Create(
            environmentCode, eventCode, businessEventKey, eventOccurredAt, aggregateType, aggregateId, recipientKey,
            recipientEmploymentId, recipientUserId, recipientEmail, templateCode, templateDataJson, correlationId,
            status: "Pending", createdAt: eventOccurredAt, finalizedAt: null, lastErrorCode: null);

    public static MailOutbox CreateRecipientEmailFailure(
        string environmentCode,
        string eventCode,
        string businessEventKey,
        DateTime eventOccurredAt,
        string aggregateType,
        string aggregateId,
        string recipientKey,
        long? recipientEmploymentId,
        int? recipientUserId,
        string templateCode,
        string templateDataJson,
        Guid correlationId,
        DateTime processingTime)
        => Create(
            environmentCode, eventCode, businessEventKey, eventOccurredAt, aggregateType, aggregateId, recipientKey,
            recipientEmploymentId, recipientUserId, null, templateCode, templateDataJson, correlationId,
            status: "Failed", createdAt: processingTime, finalizedAt: processingTime, lastErrorCode: "RECIPIENT_EMAIL_UNUSABLE");

    private static MailOutbox Create(
        string environmentCode,
        string eventCode,
        string businessEventKey,
        DateTime eventOccurredAt,
        string aggregateType,
        string aggregateId,
        string recipientKey,
        long? recipientEmploymentId,
        int? recipientUserId,
        string? recipientEmail,
        string templateCode,
        string templateDataJson,
        Guid correlationId,
        string status,
        DateTime createdAt,
        DateTime? finalizedAt,
        string? lastErrorCode)
        => new()
        {
            EnvironmentCode = environmentCode,
            EventCode = eventCode,
            BusinessEventKey = businessEventKey,
            EventOccurredAt = eventOccurredAt,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            RecipientKey = recipientKey,
            RecipientEmploymentId = recipientEmploymentId,
            RecipientUserId = recipientUserId,
            RecipientEmail = recipientEmail,
            TemplateCode = templateCode,
            TemplateDataJson = templateDataJson,
            Status = status,
            AttemptCount = 0,
            AvailableAt = eventOccurredAt,
            CreatedAt = createdAt,
            FinalizedAt = finalizedAt,
            LastErrorCode = lastErrorCode,
            CorrelationId = correlationId
        };

}

public sealed class MailDeliveryLog
{
    public long MailDeliveryLogId { get; set; }
    public long MailOutboxId { get; set; }
    public int AttemptNumber { get; set; }
    public string? Provider { get; set; }
    public string Status { get; set; } = "";
    public string? ProviderMessageId { get; set; }
    public DateTime AttemptedAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
