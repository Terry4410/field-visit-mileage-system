namespace FieldVisit.Domain.Entities;

public sealed class Organization
{
    public int OrganizationId { get; set; }
    public string OrganizationCode { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class Team
{
    public int TeamId { get; set; }
    public int OrganizationId { get; set; }
    public string TeamCode { get; set; } = "";
    public string TeamName { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class User
{
    public int UserId { get; set; }
    public int? OrganizationId { get; set; }
    public int? TeamId { get; set; }
    public string EmployeeNo { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public Guid? EntraObjectId { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class Role
{
    public int RoleId { get; set; }
    public string RoleCode { get; set; } = "";
    public string RoleName { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class UserRole
{
    public int UserRoleId { get; set; }
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public DateTime AssignedAt { get; set; }
}

public sealed class Location
{
    public int LocationId { get; set; }
    public int? OrganizationId { get; set; }
    public int? TeamId { get; set; }
    public string? LocationCode { get; set; }
    public string LocationName { get; set; } = "";
    public string LocationType { get; set; } = "Official";
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? Address { get; set; }
    public string? PlusCode { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool IsTemporary { get; set; }
    public string ApprovalStatus { get; set; } = "Pending";
    public string GeocodingStatus { get; set; } = "Pending";
    public DateTime? GeocodedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class LocationApprovalHistory
{
    public long LocationApprovalHistoryId { get; set; }
    public int LocationId { get; set; }
    public string Action { get; set; } = "";
    public int? ReviewedByUserId { get; set; }
    public string? Comments { get; set; }
    public DateTime ActionAt { get; set; }
}

public sealed class Project
{
    public int ProjectId { get; set; }
    public int OrganizationId { get; set; }
    public int? TeamId { get; set; }
    public string ProjectCode { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string? Description { get; set; }
    public string LocationMode { get; set; } = "List";
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class ProjectLocation
{
    public int ProjectLocationId { get; set; }
    public int ProjectId { get; set; }
    public int LocationId { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class VisitType
{
    public int VisitTypeId { get; set; }
    public string VisitTypeCode { get; set; } = "";
    public string VisitTypeName { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class VisitTrip
{
    public long VisitTripId { get; set; }
    public string TripNo { get; set; } = "";
    public int UserId { get; set; }
    public int OrganizationId { get; set; }
    public int? TeamId { get; set; }
    public DateOnly VisitDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public bool HasTimeOverlapWarning { get; set; }
    public bool TimeOverlapConfirmed { get; set; }
    public string Status { get; set; } = "Draft";
    public string? VehicleType { get; set; }
    public string? Purpose { get; set; }
    public string? Notes { get; set; }
    public string? ReturnReason { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<VisitTripStop> Stops { get; set; } = [];
    public MileageCalculation? MileageCalculation { get; set; }
}

public sealed class VisitTripStop
{
    public long VisitTripStopId { get; set; }
    public long VisitTripId { get; set; }
    public int StopSequence { get; set; }
    public int? LocationId { get; set; }
    public int? ProjectId { get; set; }
    public int? VisitTypeId { get; set; }
    public string? LocationNameSnapshot { get; set; }
    public string? AddressSnapshot { get; set; }
    public DateTime? ArrivalTime { get; set; }
    public DateTime? DepartureTime { get; set; }
    public string? VisitPurpose { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public VisitTrip VisitTrip { get; set; } = null!;
    public Location? Location { get; set; }
}

public sealed class MileageRateRule
{
    public int MileageRateRuleId { get; set; }
    public int? OrganizationId { get; set; }
    public string RuleName { get; set; } = "";
    public string VehicleType { get; set; } = "Motorcycle";
    public decimal RatePerKm { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class MileageCalculation
{
    public long MileageCalculationId { get; set; }
    public long VisitTripId { get; set; }
    public int? MileageRateRuleId { get; set; }
    public decimal? SystemDistanceKm { get; set; }
    public decimal? ClaimedDistanceKm { get; set; }
    public decimal? ApprovedDistanceKm { get; set; }
    public decimal? RatePerKmSnapshot { get; set; }
    public decimal? ClaimedAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public string? CalculationSource { get; set; }
    public DateTime? CalculatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public VisitTrip VisitTrip { get; set; } = null!;
}

public sealed class ApprovalRecord
{
    public long ApprovalRecordId { get; set; }
    public long VisitTripId { get; set; }
    public int ApprovalStep { get; set; }
    public int? ApproverUserId { get; set; }
    public string Action { get; set; } = "";
    public string? Comments { get; set; }
    public DateTime ActionAt { get; set; }
}

public sealed class VisitTripStatusHistory
{
    public long VisitTripStatusHistoryId { get; set; }
    public long VisitTripId { get; set; }
    public string? PreviousStatus { get; set; }
    public string NewStatus { get; set; } = "";
    public string Action { get; set; } = "";
    public int? ActionByUserId { get; set; }
    public string? Comments { get; set; }
    public DateTime ActionAt { get; set; }
}

public sealed class AuditLog
{
    public long AuditLogId { get; set; }
    public int? UserId { get; set; }
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public string Action { get; set; } = "";
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? IpAddress { get; set; }
    public Guid? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; }
}
