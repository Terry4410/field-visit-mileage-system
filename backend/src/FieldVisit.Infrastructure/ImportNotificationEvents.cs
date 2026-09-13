using FieldVisit.Application;

namespace FieldVisit.Infrastructure;

public sealed class EfImportNotificationEvents(INotificationOutboxWriter writer) : IImportNotificationEvents
{
    public Task QueueCompletedAsync(
        Guid importBatchId,
        string finalStatus,
        DateTime confirmedAt,
        int organizationId,
        int initiatorUserId,
        CancellationToken ct)
    {
        if (finalStatus is not ("Confirmed" or "PartiallyFailed"))
            throw new InvalidOperationException("ImportCompleted requires a terminal completed batch status.");

        var id = importBatchId.ToString("D");
        return writer.QueueAsync(
            new NotificationEventContext(
                NotificationEventCodes.ImportCompleted,
                "ImportBatch",
                id,
                $"IMPORT:{id}:COMPLETED",
                confirmedAt,
                organizationId,
                null,
                null,
                initiatorUserId,
                null,
                new NotificationTemplatePayloadV1(id, finalStatus),
                Guid.NewGuid()),
            ct);
    }
}
