namespace FieldVisit.Domain.Entities;

public sealed class GeocodingAttempt
{
    public long GeocodingAttemptId { get; set; }
    public int LocationId { get; set; }
    public string Provider { get; set; } = "";
    public byte[] AddressBasisHash { get; set; } = [];
    public string Status { get; set; } = "Pending";
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int RequestedByUserId { get; set; }
}

public sealed class RouteCalculationAttempt
{
    public long RouteCalculationAttemptId { get; set; }
    public long VisitTripId { get; set; }
    public string BasisType { get; set; } = "Draft";
    public long? BasisVisitTripSnapshotId { get; set; }
    public string CalculationReason { get; set; } = "VisitorCalculate";
    public string RequestedVehicleType { get; set; } = "Car";
    public string TravelMode { get; set; } = "DRIVE";
    public string Provider { get; set; } = "";
    public int StopCount { get; set; }
    public byte[] RequestBasisHash { get; set; } = [];
    public string Status { get; set; } = "Pending";
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int RequestedByUserId { get; set; }
}

public sealed class MileageGovernanceEvent
{
    public long MileageGovernanceEventId { get; set; }
    public long VisitTripId { get; set; }
    public long? VisitTripSnapshotId { get; set; }
    public long? RouteCalculationAttemptId { get; set; }
    public string EventType { get; set; } = "Calculated";
    public string? ReasonCode { get; set; }
    public string? Message { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTime OccurredAt { get; set; }
    public int? ActorUserId { get; set; }
}
