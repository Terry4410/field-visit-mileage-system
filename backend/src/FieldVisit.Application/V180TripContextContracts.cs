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

public static class V180TripPersistenceRules
{
    public static (int? Start, int? End) ResolveDraftSites(
        V180TripContextDto context,
        int? requestedStart,
        int? requestedEnd,
        int? existingStart = null,
        int? existingEnd = null)
    {
        if (!context.SelectedTeamId.HasValue)
            throw new InvalidOperationException($"{context.ValidationCode}：{context.ValidationMessage}");

        var eligible = context.EligibleDeploymentSites.Select(x => x.DeploymentSiteId).ToHashSet();
        return (
            ResolveOne("出發", requestedStart, existingStart, context.DefaultStartDeploymentSiteId, eligible),
            ResolveOne("返回", requestedEnd, existingEnd, context.DefaultEndDeploymentSiteId, eligible));
    }

    public static void EnsureReadyForSubmit(
        V180TripContextDto context,
        long employmentId,
        int? teamId,
        int? startSiteId,
        int? endSiteId)
    {
        if (context.EmploymentId != employmentId)
            throw new InvalidOperationException("TRIP_CONTEXT_EMPLOYMENT_CHANGED：人員資料已變更，請重新開啟行程。");
        if (!context.EligibleForTrip || context.SelectedTeamId != teamId)
            throw new InvalidOperationException($"{context.ValidationCode}：{context.ValidationMessage} 請重新開啟行程並確認小組。");
        if (!startSiteId.HasValue)
            throw new InvalidOperationException("START_DEPLOYMENT_SITE_REQUIRED：送出前請選擇出發派駐點。");
        if (!endSiteId.HasValue)
            throw new InvalidOperationException("END_DEPLOYMENT_SITE_REQUIRED：送出前請選擇返回派駐點。");
        var eligible = context.EligibleDeploymentSites.Select(x => x.DeploymentSiteId).ToHashSet();
        if (!eligible.Contains(startSiteId.Value))
            throw new InvalidOperationException("START_DEPLOYMENT_SITE_INELIGIBLE：出發派駐點已失效，請重新選擇。");
        if (!eligible.Contains(endSiteId.Value))
            throw new InvalidOperationException("END_DEPLOYMENT_SITE_INELIGIBLE：返回派駐點已失效，請重新選擇。");
    }

    private static int? ResolveOne(
        string label, int? requested, int? existing, int? deterministicDefault, HashSet<int> eligible)
    {
        if (requested.HasValue)
        {
            if (!eligible.Contains(requested.Value))
                throw new InvalidOperationException($"DEPLOYMENT_SITE_INELIGIBLE：{label}派駐點不在本日期與小組的有效範圍。");
            return requested;
        }
        if (existing.HasValue && eligible.Contains(existing.Value)) return existing;
        return deterministicDefault;
    }
}
