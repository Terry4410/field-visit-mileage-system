namespace FieldVisit.Application;

public sealed record V170LoginEligibility(
    bool IsAllowed,
    string UserType,
    string? EmploymentStatus,
    string? Reason);

public sealed record V170ReadScope(
    bool OrganizationWide,
    IReadOnlyList<int> TeamIds);

/// <summary>
/// v1.7 server-side authorization service.
///
/// Important:
/// - Team Membership != Data Scope
/// - Supervisor is read-only
/// - Supervisor export requires explicit capability
/// - HR employment eligibility is independent from system role/team
/// </summary>
public interface IV170AccessControl
{
    Task<V170LoginEligibility> EvaluateLoginAsync(
        int userId,
        bool adminEnabled,
        CancellationToken ct);

    Task<V170ReadScope> ResolveReadScopeAsync(
        CurrentUserDto user,
        CancellationToken ct);

    Task<bool> HasCapabilityAsync(
        int userId,
        string capabilityCode,
        CancellationToken ct);

    Task EnsureExportAllowedAsync(
        CurrentUserDto user,
        string format,
        CancellationToken ct);

    Task AuditSupervisorQueryAsync(
        CurrentUserDto user,
        TripQueryRequest request,
        int resultCount,
        CancellationToken ct);
}
