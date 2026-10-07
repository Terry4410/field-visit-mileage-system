namespace FieldVisit.Application;

public sealed record V180PersonDeleteImpactDto(
    int UserId,
    string UserCode,
    string DisplayName,
    bool CanDelete,
    int TripReferenceCount,
    int SnapshotReferenceCount,
    int WorkflowReferenceCount,
    int AuditReferenceCount,
    int LeadershipReferenceCount,
    int AdministrativeReferenceCount,
    string? Reason);

public sealed record V180TeamDeleteImpactDto(
    int TeamId,
    string TeamCode,
    string TeamName,
    bool CanDelete,
    int MembershipReferenceCount,
    int ScopeReferenceCount,
    int ProjectReferenceCount,
    int LocationReferenceCount,
    int TripReferenceCount,
    int SnapshotReferenceCount,
    int StructureReferenceCount,
    int NoteReferenceCount,
    string? Reason);

public sealed record V180ProjectDeleteImpactDto(
    int ProjectId,
    string ProjectCode,
    string ProjectName,
    bool CanDelete,
    int TripStopReferenceCount,
    int SnapshotStopReferenceCount,
    int ProjectLocationCount,
    string? Reason);
