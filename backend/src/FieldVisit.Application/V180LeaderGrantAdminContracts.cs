namespace FieldVisit.Application;

public sealed record V180LeaderGrantQueryRequest(
    int? TeamId = null, int? UserId = null, bool? CurrentOnly = null);

public sealed record V180LeaderAppointmentDto(
    long GrantId, int TeamId, string TeamName, int LeaderUserId, string LeaderName,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, string GrantStatus,
    string? AppointmentType, string? ProvenanceType, string? EvidenceReference,
    DateTime? RevokedAt, string? RevocationReason, string RowVersion);

public sealed record V180LeaderDelegationDto(
    long GrantId, long AppointmentGrantId, int TeamId, string TeamName,
    int DelegateUserId, string DelegateName, DateOnly EffectiveFrom,
    DateOnly EffectiveTo, string GrantStatus, string Scope, string? EvidenceReference,
    DateTime? RevokedAt, string? RevocationReason, string RowVersion);

public sealed record V180LeaderGrantSnapshotDto(
    IReadOnlyList<V180LeaderAppointmentDto> Appointments,
    IReadOnlyList<V180LeaderDelegationDto> Delegations);

public sealed record V180CreateLeaderAppointmentRequest(
    int TeamId, int LeaderUserId, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string EvidenceReference, string? ApprovedReason);

public sealed record V180CreateLeaderDelegationRequest(
    long AppointmentGrantId, int DelegateUserId, DateOnly EffectiveFrom,
    DateOnly EffectiveTo, string EvidenceReference, string Reason);

public sealed record V180EndLeaderGrantRequest(
    DateOnly EffectiveTo, string RowVersion, string Reason);
