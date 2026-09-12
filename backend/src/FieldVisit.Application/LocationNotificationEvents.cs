using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

/// <summary>Recovery boundary used only by Location-producing mutations.</summary>
public interface ILocationMutationBoundary : ITransactionBoundary { }

/// <summary>Caller-owned Location events; this coordinator never saves or commits.</summary>
public interface ILocationNotificationEvents
{
    Task MarkInitialCycleAsync(Location location, CancellationToken ct);
    Task QueueReviewAsync(Location location, string businessEventKey, DateTime transitionAt, CancellationToken ct);
    Task QueueApprovedAsync(Location location, LocationApprovalHistory history, bool enteredApproved, CancellationToken ct);
}

public static class LocationReviewCycleContract
{
    public const string InitialAuditAction = "LocationInitialReviewCycleV1";
    public const string InitialMarker = "INITIAL_REVIEW_CYCLE";
}
