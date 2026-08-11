namespace FieldVisit.Domain.Entities;

public sealed class UserTeamScope
{
    public int UserTeamScopeId { get; set; }
    public int UserId { get; set; }
    public int TeamId { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime AssignedAt { get; set; }
    public int? AssignedByUserId { get; set; }
    public DateTime? EndedAt { get; set; }
}

public sealed class VisitTripSnapshot
{
    public long VisitTripSnapshotId { get; set; }
    public long VisitTripId { get; set; }
    public int SnapshotVersion { get; set; }
    public string SnapshotType { get; set; } = "Approved";
    public string TripNo { get; set; } = "";
    public int UserId { get; set; }
    public string EmployeeNoSnapshot { get; set; } = "";
    public string DisplayNameSnapshot { get; set; } = "";
    public int OrganizationId { get; set; }
    public string OrganizationNameSnapshot { get; set; } = "";
    public int? TeamId { get; set; }
    public string? TeamNameSnapshot { get; set; }
    public DateOnly VisitDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string StatusSnapshot { get; set; } = "Approved";
    public string? VehicleTypeSnapshot { get; set; }
    public decimal? ClaimedDistanceKmSnapshot { get; set; }
    public decimal? SystemDistanceKmSnapshot { get; set; }
    public decimal? ApprovedDistanceKmSnapshot { get; set; }
    public decimal? RatePerKmSnapshot { get; set; }
    public decimal? SubsidyAmountSnapshot { get; set; }
    public string? RouteProviderSnapshot { get; set; }
    public DateTime? SubmittedAtSnapshot { get; set; }
    public DateTime? ApprovedAtSnapshot { get; set; }
    public int? ApproverUserId { get; set; }
    public string? ApproverNameSnapshot { get; set; }
    public string? NotesSnapshot { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public List<VisitTripSnapshotStop> Stops { get; set; } = [];
}

public sealed class VisitTripSnapshotStop
{
    public long VisitTripSnapshotStopId { get; set; }
    public long VisitTripSnapshotId { get; set; }
    public int StopSequence { get; set; }
    public int? LocationId { get; set; }
    public string? LocationCodeSnapshot { get; set; }
    public string LocationNameSnapshot { get; set; } = "";
    public string? AddressSnapshot { get; set; }
    public int? ProjectId { get; set; }
    public string? ProjectCodeSnapshot { get; set; }
    public string? ProjectNameSnapshot { get; set; }
    public int? VisitTypeId { get; set; }
    public string? VisitTypeCodeSnapshot { get; set; }
    public string? VisitTypeNameSnapshot { get; set; }
    public string? VisitPurposeSnapshot { get; set; }
    public string? NotesSnapshot { get; set; }
    public DateTime CreatedAt { get; set; }
    public VisitTripSnapshot Snapshot { get; set; } = null!;
}

public sealed class CorrectionRequest
{
    public long CorrectionRequestId { get; set; }
    public long VisitTripId { get; set; }
    public long BaseSnapshotId { get; set; }
    public long? ResultSnapshotId { get; set; }
    public string Status { get; set; } = "PendingLeaderReview";
    public string Reason { get; set; } = "";
    public string? ProposedChangesJson { get; set; }
    public int RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; }
    public int? LeaderReviewedByUserId { get; set; }
    public DateTime? LeaderReviewedAt { get; set; }
    public string? LeaderComments { get; set; }
    public int? AdminClosedByUserId { get; set; }
    public DateTime? AdminClosedAt { get; set; }
    public string? AdminComments { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class CorrectionRequestChange
{
    public long CorrectionRequestChangeId { get; set; }
    public long CorrectionRequestId { get; set; }
    public string FieldName { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class BackgroundJob
{
    public Guid BackgroundJobId { get; set; }
    public string JobType { get; set; } = "";
    public string Status { get; set; } = "Waiting";
    public string? Mode { get; set; }
    public int? OrganizationId { get; set; }
    public string? TeamScopeJson { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int RequestedByUserId { get; set; }
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public string? PayloadJson { get; set; }
    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class BackgroundJobItem
{
    public long BackgroundJobItemId { get; set; }
    public Guid BackgroundJobId { get; set; }
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Status { get; set; } = "Waiting";
    public string? ResultJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
