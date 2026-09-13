using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Add-only persistence surface for F-B/F-C. Transaction ownership and SaveChanges
/// remain with the caller; this repository deliberately never flushes the unit of work.
/// </summary>
public sealed class V180GoogleMileageGovernanceRepository(AppDbContext db) : IV180GoogleMileageGovernanceRepository
{
    public Task<GeocodingAttempt> AddGeocodingAttemptAsync(V180GeocodingAttemptRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var row = new GeocodingAttempt
        {
            LocationId = request.LocationId, Provider = request.Provider,
            AddressBasisHash = V180MileageGovernanceRules.RequireHash32(request.AddressBasisHash, nameof(request.AddressBasisHash)), CorrelationId = request.CorrelationId,
            RequestedAt = request.RequestedAt, RequestedByUserId = request.RequestedByUserId
        };
        db.GeocodingAttempts.Add(row);
        return Task.FromResult(row);
    }

    public Task<RouteCalculationAttempt> AddRouteCalculationAttemptAsync(V180RouteCalculationAttemptRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var row = new RouteCalculationAttempt
        {
            VisitTripId = request.VisitTripId, BasisType = request.BasisType,
            BasisVisitTripSnapshotId = request.BasisVisitTripSnapshotId, CalculationReason = request.CalculationReason,
            RequestedVehicleType = request.RequestedVehicleType, TravelMode = request.TravelMode,
            Provider = request.Provider, StopCount = request.StopCount,
            RequestBasisHash = V180MileageGovernanceRules.RequireHash32(request.RequestBasisHash, nameof(request.RequestBasisHash)), CorrelationId = request.CorrelationId,
            RequestedAt = request.RequestedAt, RequestedByUserId = request.RequestedByUserId
        };
        db.RouteCalculationAttempts.Add(row);
        return Task.FromResult(row);
    }

    public Task<MileageGovernanceEvent> AddGovernanceEventAsync(V180MileageGovernanceEventRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var row = new MileageGovernanceEvent
        {
            VisitTripId = request.VisitTripId, VisitTripSnapshotId = request.VisitTripSnapshotId,
            RouteCalculationAttemptId = request.RouteCalculationAttemptId, EventType = request.EventType,
            ReasonCode = request.ReasonCode, Message = request.Message, CorrelationId = request.CorrelationId,
            OccurredAt = request.OccurredAt, ActorUserId = request.ActorUserId
        };
        db.MileageGovernanceEvents.Add(row);
        return Task.FromResult(row);
    }

    public Task<RouteCalculationAttempt?> GetRouteCalculationAttemptAsync(long attemptId, CancellationToken ct) =>
        db.RouteCalculationAttempts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RouteCalculationAttemptId == attemptId, ct);

    public Task<GeocodingAttempt?> GetGeocodingAttemptAsync(long attemptId, CancellationToken ct) =>
        db.GeocodingAttempts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.GeocodingAttemptId == attemptId, ct);

    public async Task<bool> TryFinalizeRouteCalculationAttemptAsync(
        long attemptId, string status, string? errorCode, string? errorMessage,
        DateTime completedAt, CancellationToken ct)
    {
        EnsureTerminalValues(status, errorCode);
        var affected = await db.RouteCalculationAttempts
            .Where(x => x.RouteCalculationAttemptId == attemptId && x.Status == "Pending")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.ErrorCode, errorCode)
                .SetProperty(x => x.ErrorMessage, errorMessage)
                .SetProperty(x => x.CompletedAt, completedAt), ct);
        return affected == 1;
    }

    public async Task<bool> TryFinalizeGeocodingAttemptAsync(
        long attemptId, string status, string? errorCode, string? errorMessage,
        DateTime completedAt, CancellationToken ct)
    {
        EnsureTerminalValues(status, errorCode);
        var affected = await db.GeocodingAttempts
            .Where(x => x.GeocodingAttemptId == attemptId && x.Status == "Pending")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.ErrorCode, errorCode)
                .SetProperty(x => x.ErrorMessage, errorMessage)
                .SetProperty(x => x.CompletedAt, completedAt), ct);
        return affected == 1;
    }

    private static void EnsureTerminalValues(string status, string? errorCode)
    {
        if (status is not ("Succeeded" or "Failed"))
            throw new ArgumentException("Attempt terminal status must be Succeeded or Failed.", nameof(status));
        if (status == "Succeeded" && errorCode is not null)
            throw new ArgumentException("Succeeded attempt cannot have an error code.", nameof(errorCode));
        if (status == "Failed" && string.IsNullOrWhiteSpace(errorCode))
            throw new ArgumentException("Failed attempt requires an error code.", nameof(errorCode));
    }
}
