namespace FieldVisit.Application;

public sealed record V180TeamLifecycleDetailDto(
    int TeamId,
    int OrganizationId,
    string Code,
    string Name,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    string? Notes,
    string Version);

public sealed record V180CenterLifecycleDetailDto(
    int CenterId,
    int OrganizationId,
    string Code,
    string Name,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    string? Notes,
    string Version);

public sealed record V180TeamCenterAssignmentAdminDto(
    long TeamCenterAssignmentId,
    int TeamId,
    int CenterId,
    string CenterCode,
    string CenterName,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? ChangeReason,
    string Version);

public interface IV180TeamCenterAdminReader
{
    Task<V180TeamLifecycleDetailDto?> GetTeamAsync(CurrentUserDto user, int teamId, CancellationToken ct);
    Task<V180CenterLifecycleDetailDto?> GetCenterAsync(CurrentUserDto user, int centerId, CancellationToken ct);
    Task<IReadOnlyList<V180TeamCenterAssignmentAdminDto>> ListAssignmentsAsync(
        CurrentUserDto user, int teamId, bool includeHistory, DateOnly? asOf, CancellationToken ct);
}
