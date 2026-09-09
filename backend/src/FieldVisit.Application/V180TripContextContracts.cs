namespace FieldVisit.Application;

public sealed record V180TripContextTeamDto(
    int TeamId, string Code, string Name, bool IsPrimary);

public sealed record V180TripContextDeploymentSiteDto(
    int DeploymentSiteId,
    int CenterId,
    string CenterCode,
    string CenterName,
    string SiteCode,
    string SiteName,
    int LocationId,
    string? LocationCode,
    string LocationName,
    string? Address,
    bool IsPrimary);

public sealed record V180TripContextDto(
    long EmploymentId,
    DateOnly VisitDate,
    bool EligibleForTrip,
    string ValidationCode,
    string ValidationMessage,
    IReadOnlyList<V180TripContextTeamDto> Teams,
    int? SelectedTeamId,
    IReadOnlyList<V180TripContextDeploymentSiteDto> EligibleDeploymentSites,
    int? PrimaryDeploymentSiteId,
    int? DefaultStartDeploymentSiteId,
    int? DefaultEndDeploymentSiteId);

public interface IV180TripContextReader
{
    Task<V180TripContextDto> ResolveAsync(
        CurrentUserDto user, DateOnly visitDate, int? teamId, CancellationToken ct);
}
