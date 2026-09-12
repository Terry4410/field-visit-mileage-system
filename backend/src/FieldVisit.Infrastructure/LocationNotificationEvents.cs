using System.Globalization;
using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class EfLocationNotificationEvents(AppDbContext db, INotificationOutboxWriter writer) : ILocationNotificationEvents
{
    public Task MarkInitialCycleAsync(Location location, CancellationToken ct)
    {
        if (location.LocationId <= 0 || location.ApprovalStatus != "Pending")
            throw new InvalidOperationException("Initial review marker requires a materialized Pending Location.");
        db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Location", EntityId = Id(location), Action = LocationReviewCycleContract.InitialAuditAction,
            UserId = location.CreatedByUserId, CreatedAt = location.CreatedAt,
            NewValues = JsonSerializer.Serialize(new { markerVersion = 1, marker = LocationReviewCycleContract.InitialMarker, locationId = location.LocationId })
        });
        return Task.CompletedTask;
    }

    public async Task QueueReviewAsync(Location location, string businessEventKey, DateTime transitionAt, CancellationToken ct) =>
        await writer.QueueAsync(Context(location, NotificationEventCodes.LocationReviewRequested, businessEventKey, transitionAt), ct);

    public async Task QueueApprovedAsync(Location location, LocationApprovalHistory history, bool enteredApproved, CancellationToken ct)
    {
        if (!enteredApproved || location.ApprovalStatus != "Approved" || history.Action != "Approved") return;
        if (history.LocationApprovalHistoryId <= 0 || history.LocationId != location.LocationId)
            throw new InvalidOperationException("Approval event requires its persisted Location approval occurrence.");

        // Exact, durable applied relationships only. No LocationCode, ordering, clock,
        // approval-history absence, Outbox, reviewer or job requester authority.
        var facts = await db.Database.SqlQuery<int>($"""
            SELECT CASE
              WHEN EXISTS (SELECT 1 FROM dbo.ImportBatchItems WHERE Status = N'Applied'
                AND EntityType = N'Location' AND ISJSON(DataJson) = 1
                AND JSON_VALUE(DataJson, '$.envelopeVersion') = N'1'
                AND TRY_CONVERT(int, JSON_VALUE(DataJson, '$.appliedMutation.locationId')) = {location.LocationId}
                AND JSON_VALUE(DataJson, '$.appliedMutation.transitionKind') = N'REREVIEW') THEN 0
              WHEN EXISTS (SELECT 1 FROM dbo.AuditLogs WHERE EntityType = N'Location'
                AND EntityId = {Id(location)} AND Action = {LocationReviewCycleContract.InitialAuditAction}) THEN 1
              WHEN EXISTS (SELECT 1 FROM dbo.ImportBatchItems WHERE Status = N'Applied'
                AND EntityType = N'Location' AND ISJSON(DataJson) = 1
                AND JSON_VALUE(DataJson, '$.envelopeVersion') = N'1'
                AND TRY_CONVERT(int, JSON_VALUE(DataJson, '$.appliedMutation.locationId')) = {location.LocationId}
                AND JSON_VALUE(DataJson, '$.appliedMutation.transitionKind') = N'CREATE') THEN 1
              ELSE 0 END AS [Value]
            """).SingleAsync(ct);
        if (facts != 1) return; // Legacy ambiguous cycles intentionally do not emit.
        await writer.QueueAsync(Context(location, NotificationEventCodes.LocationApproved,
            $"LOCATION:{Id(location)}:APPROVED:HISTORY:{history.LocationApprovalHistoryId}", history.ActionAt), ct);
    }

    private static string Id(Location location) => location.LocationId.ToString(CultureInfo.InvariantCulture);
    private static NotificationEventContext Context(Location location, string code, string key, DateTime at) =>
        new(code, "Location", Id(location), key, at, location.OrganizationId, location.TeamId, null,
            location.CreatedByUserId, null, new NotificationTemplatePayloadV1(location.LocationName), Guid.NewGuid());
}
