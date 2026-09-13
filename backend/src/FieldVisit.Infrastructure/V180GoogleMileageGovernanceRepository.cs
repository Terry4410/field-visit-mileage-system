using FieldVisit.Application;
using FieldVisit.Domain.Entities;

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
}
