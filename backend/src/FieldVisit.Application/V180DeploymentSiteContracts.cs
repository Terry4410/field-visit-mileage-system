namespace FieldVisit.Application;

public sealed record V180DeploymentSiteQuery(
    DateOnly? AsOf = null, string? Keyword = null, int? CenterId = null,
    bool IncludeInactive = false, int Page = 1, int PageSize = 50);

public sealed record V180DeploymentSiteLocationSummary(
    int LocationId, string? Code, string Name, string? Address);

public sealed record V180DeploymentSiteAdminDto(
    int DeploymentSiteId, int CenterId, string CenterCode, string CenterName,
    string Code, string Name, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    bool IsActive, string? Notes, V180DeploymentSiteLocationSummary? CurrentLocation,
    int TeamAssignmentCount, int EmploymentAssignmentCount, string Version);

public sealed record V180DeploymentSiteLocationAssignmentDto(
    long DeploymentSiteLocationAssignmentId, int DeploymentSiteId, int LocationId,
    string? LocationCode, string LocationName, string? Address,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? ChangeReason, string Version);

public sealed record V180TeamDeploymentSiteAssignmentDto(
    long TeamDeploymentSiteAssignmentId, int TeamId, string TeamCode, string TeamName,
    int DeploymentSiteId, string SiteCode, string SiteName,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, string Version);

public sealed record V180EmploymentDeploymentSiteAssignmentDto(
    long EmploymentDeploymentSiteAssignmentId, long EmploymentId, string? EmployeeNo,
    string DisplayName, int DeploymentSiteId, string SiteCode, string SiteName,
    bool IsPrimary, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string Version);

public sealed record V180CreateDeploymentSiteRequest(
    int CenterId, string Code, string Name, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null, string? Notes = null, bool IsActive = true);
public sealed record V180UpdateDeploymentSiteRequest(
    int CenterId, string Code, string Name, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, string? Notes, bool IsActive, string Version);
public sealed record V180DeploymentSiteWriteResult(
    int DeploymentSiteId, int CenterId, string Code, string Name,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, string? Notes, string Version);

public sealed record V180CreateDeploymentSiteLocationAssignmentRequest(
    int DeploymentSiteId, int LocationId, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null, string? ChangeReason = null);
public sealed record V180UpdateDeploymentSiteLocationAssignmentRequest(
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? ChangeReason, string Version);

public sealed record V180CreateTeamDeploymentSiteAssignmentRequest(
    int TeamId, int DeploymentSiteId, DateOnly EffectiveFrom, DateOnly? EffectiveTo = null);
public sealed record V180UpdateTeamDeploymentSiteAssignmentRequest(
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, string Version);

public sealed record V180CreateEmploymentDeploymentSiteAssignmentRequest(
    long EmploymentId, int DeploymentSiteId, bool IsPrimary,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo = null);
public sealed record V180UpdateEmploymentDeploymentSiteAssignmentRequest(
    bool IsPrimary, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string Version);

public sealed record V180DeploymentAssignmentWriteResult(
    long AssignmentId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string Version);

public interface IV180DeploymentSiteReader
{
    Task<PagedResult<V180DeploymentSiteAdminDto>> SearchAsync(
        CurrentUserDto user, V180DeploymentSiteQuery query, CancellationToken ct);
    Task<V180DeploymentSiteAdminDto?> GetAsync(
        CurrentUserDto user, int deploymentSiteId, DateOnly? asOf, CancellationToken ct);
    Task<IReadOnlyList<V180DeploymentSiteLocationAssignmentDto>> LocationAssignmentsAsync(
        CurrentUserDto user, int deploymentSiteId, bool includeHistory, DateOnly? asOf, CancellationToken ct);
    Task<IReadOnlyList<V180TeamDeploymentSiteAssignmentDto>> TeamAssignmentsAsync(
        CurrentUserDto user, int deploymentSiteId, bool includeHistory, DateOnly? asOf, CancellationToken ct);
    Task<IReadOnlyList<V180EmploymentDeploymentSiteAssignmentDto>> EmploymentAssignmentsAsync(
        CurrentUserDto user, int deploymentSiteId, bool includeHistory, DateOnly? asOf, CancellationToken ct);
}

public interface IV180DeploymentSiteWriter
{
    Task<V180DeploymentSiteWriteResult> CreateSiteAsync(CurrentUserDto admin, V180CreateDeploymentSiteRequest request, CancellationToken ct);
    Task<V180DeploymentSiteWriteResult> UpdateSiteAsync(CurrentUserDto admin, int deploymentSiteId, V180UpdateDeploymentSiteRequest request, CancellationToken ct);
    Task<V180DeploymentSiteWriteResult> DeactivateSiteAsync(CurrentUserDto admin, int deploymentSiteId, V180DeactivateRequest request, CancellationToken ct);

    Task<V180DeploymentAssignmentWriteResult> CreateLocationAssignmentAsync(CurrentUserDto admin, V180CreateDeploymentSiteLocationAssignmentRequest request, CancellationToken ct);
    Task<V180DeploymentAssignmentWriteResult> UpdateLocationAssignmentAsync(CurrentUserDto admin, long assignmentId, V180UpdateDeploymentSiteLocationAssignmentRequest request, CancellationToken ct);
    Task<V180DeploymentAssignmentWriteResult> EndLocationAssignmentAsync(CurrentUserDto admin, long assignmentId, V180DeactivateRequest request, CancellationToken ct);

    Task<V180DeploymentAssignmentWriteResult> CreateTeamAssignmentAsync(CurrentUserDto admin, V180CreateTeamDeploymentSiteAssignmentRequest request, CancellationToken ct);
    Task<V180DeploymentAssignmentWriteResult> UpdateTeamAssignmentAsync(CurrentUserDto admin, long assignmentId, V180UpdateTeamDeploymentSiteAssignmentRequest request, CancellationToken ct);
    Task<V180DeploymentAssignmentWriteResult> EndTeamAssignmentAsync(CurrentUserDto admin, long assignmentId, V180DeactivateRequest request, CancellationToken ct);

    Task<V180DeploymentAssignmentWriteResult> CreateEmploymentAssignmentAsync(CurrentUserDto admin, V180CreateEmploymentDeploymentSiteAssignmentRequest request, CancellationToken ct);
    Task<V180DeploymentAssignmentWriteResult> UpdateEmploymentAssignmentAsync(CurrentUserDto admin, long assignmentId, V180UpdateEmploymentDeploymentSiteAssignmentRequest request, CancellationToken ct);
    Task<V180DeploymentAssignmentWriteResult> EndEmploymentAssignmentAsync(CurrentUserDto admin, long assignmentId, V180DeactivateRequest request, CancellationToken ct);
}

public static class V180DeploymentSiteRules
{
    public static bool IsEffective(DateOnly from, DateOnly? to, DateOnly asOf) =>
        from <= asOf && (!to.HasValue || asOf <= to.Value);

    public static bool Overlaps(DateOnly leftFrom, DateOnly? leftTo, DateOnly rightFrom, DateOnly? rightTo) =>
        V180TeamCenterLifecycleRules.Overlaps(leftFrom, leftTo, rightFrom, rightTo);

    public static bool IsWithin(DateOnly from, DateOnly? to, DateOnly ownerFrom, DateOnly? ownerTo) =>
        V180TeamCenterLifecycleRules.IsWithin(from, to, ownerFrom, ownerTo);

    public static byte[] DecodeVersion(string version) => V180TeamCenterLifecycleRules.DecodeVersion(version);
}
