namespace FieldVisit.Application;

/// <summary>Caller-owned terminal import events; this coordinator never saves or commits.</summary>
public interface IImportNotificationEvents
{
    Task QueueCompletedAsync(
        Guid importBatchId,
        string finalStatus,
        DateTime confirmedAt,
        int organizationId,
        int initiatorUserId,
        CancellationToken ct);
}
