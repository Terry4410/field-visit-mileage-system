using System.Text.Json;
using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public sealed record V170ProjectLocationItemDto(
    int LocationId,
    string? LocationCode,
    string LocationName,
    string? City,
    string? District,
    string? Address,
    string? PlusCode);

public sealed record V170ProjectLocationCandidateResult(
    IReadOnlyList<V170ProjectLocationItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasNextPage);

public sealed record V170ProjectLocationCandidateSpec(
    string? Query,
    int Page,
    int PageSize);

public sealed record V170SaveProjectLocationsRequest(
    IReadOnlyList<int> LocationIds);

public sealed record V170ProjectLocationCountDto(
    int ProjectId,
    int Count);

public static class V170ProjectLocationAdminRules
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public const int MaxAssignedLocations = 500;

    public static V170ProjectLocationCandidateSpec NormalizeSearch(
        string? query,
        int page,
        int pageSize)
    {
        var normalizedQuery =
            string.IsNullOrWhiteSpace(query)
                ? null
                : query.Trim();

        if (normalizedQuery is { Length: > 200 })
            throw new InvalidOperationException(
                "地點搜尋關鍵字不可超過 200 個字元。");

        return new V170ProjectLocationCandidateSpec(
            normalizedQuery,
            page < 1 ? 1 : page,
            pageSize <= 0
                ? DefaultPageSize
                : Math.Min(pageSize, MaxPageSize));
    }

    public static IReadOnlyList<int> NormalizeLocationIds(
        V170SaveProjectLocationsRequest request)
    {
        if (request.LocationIds is null)
            throw new InvalidOperationException(
                "LocationIds 不可為 null。");

        if (request.LocationIds.Count > MaxAssignedLocations)
            throw new InvalidOperationException(
                $"單一專案最多可設定 {MaxAssignedLocations} 個固定地點。");

        if (request.LocationIds.Any(x => x <= 0))
            throw new InvalidOperationException(
                "LocationId 必須大於 0。");

        return request.LocationIds
            .Distinct()
            .ToArray();
    }
}

public interface IV170ProjectLocationAdminRepository
{
    Task<Project?> GetProjectAsync(
        int projectId,
        CancellationToken ct);

    Task<IReadOnlyList<V170ProjectLocationItemDto>>
        GetAssignedLocationsAsync(
            int projectId,
            CancellationToken ct);

    Task<V170ProjectLocationCandidateResult>
        SearchCandidatesAsync(
            int organizationId,
            int? projectTeamId,
            V170ProjectLocationCandidateSpec spec,
            CancellationToken ct);

    Task<int> CountEligibleLocationsAsync(
        int organizationId,
        int? projectTeamId,
        IReadOnlyCollection<int> locationIds,
        CancellationToken ct);

    Task<List<ProjectLocation>> GetAssignmentsAsync(
        int projectId,
        CancellationToken ct);

    Task<IReadOnlyList<V170ProjectLocationCountDto>> GetLocationCountsAsync(
        int organizationId,
        CancellationToken ct);

    Task AddAssignmentAsync(
        ProjectLocation row,
        CancellationToken ct);
}

public sealed class V170ProjectLocationAdminService(
    ICurrentUserService current,
    IV170ProjectLocationAdminRepository repository,
    IWorkflowRepository workflow,
    IUnitOfWork uow)
{
    public async Task<IReadOnlyList<V170ProjectLocationCountDto>>
        GetLocationCountsAsync(CancellationToken ct)
    {
        var user = RequireAdmin();
        return await repository.GetLocationCountsAsync(
            user.OrganizationId!.Value,
            ct);
    }

    public async Task<
        IReadOnlyList<V170ProjectLocationItemDto>>
        GetAsync(
            int projectId,
            CancellationToken ct)
    {
        var user = RequireAdmin();
        await RequireProjectAsync(
            user,
            projectId,
            ct);

        return await repository
            .GetAssignedLocationsAsync(
                projectId,
                ct);
    }

    public async Task<V170ProjectLocationCandidateResult>
        SearchCandidatesAsync(
            int projectId,
            string? query,
            int page,
            int pageSize,
            CancellationToken ct)
    {
        var user = RequireAdmin();

        var project =
            await RequireProjectAsync(
                user,
                projectId,
                ct);

        var spec =
            V170ProjectLocationAdminRules
                .NormalizeSearch(
                    query,
                    page,
                    pageSize);

        // Search-first UX. Blank criteria should not load
        // hundreds of Location rows into the browser.
        if (spec.Query is null)
        {
            return new V170ProjectLocationCandidateResult(
                Array.Empty<V170ProjectLocationItemDto>(),
                spec.Page,
                spec.PageSize,
                0,
                false);
        }

        return await repository
            .SearchCandidatesAsync(
                user.OrganizationId!.Value,
                project.TeamId,
                spec,
                ct);
    }

    public async Task<
        IReadOnlyList<V170ProjectLocationItemDto>>
        SaveAsync(
            int projectId,
            V170SaveProjectLocationsRequest request,
            CancellationToken ct)
    {
        var user = RequireAdmin();

        var project =
            await RequireProjectAsync(
                user,
                projectId,
                ct);

        var ids =
            V170ProjectLocationAdminRules
                .NormalizeLocationIds(
                    request);

        if (ids.Count > 0)
        {
            var eligibleCount =
                await repository
                    .CountEligibleLocationsAsync(
                        user.OrganizationId!.Value,
                        project.TeamId,
                        ids.ToArray(),
                        ct);

            if (eligibleCount != ids.Count)
            {
                throw new InvalidOperationException(
                    "固定地點中包含不存在、停用、未核准、其他組織或不符合專案小組範圍的地點。");
            }
        }

        var assignments =
            await repository
                .GetAssignmentsAsync(
                    projectId,
                    ct);

        var targets =
            ids.ToHashSet();

        // Reuse historical ProjectLocation rows instead of
        // continuously inserting duplicates.
        foreach (var group in assignments
            .GroupBy(x => x.LocationId))
        {
            var first = group.First();
            first.IsActive =
                targets.Contains(
                    first.LocationId);

            first.IsPrimary = false;

            foreach (var duplicate in group.Skip(1))
            {
                duplicate.IsActive = false;
                duplicate.IsPrimary = false;
            }
        }

        var existingIds =
            assignments
                .Select(x => x.LocationId)
                .ToHashSet();

        foreach (var locationId in ids)
        {
            if (existingIds.Contains(locationId))
                continue;

            await repository
                .AddAssignmentAsync(
                    new ProjectLocation
                    {
                        ProjectId = projectId,
                        LocationId = locationId,
                        IsPrimary = false,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    ct);
        }

        await workflow.AddAuditAsync(
            new AuditLog
            {
                UserId = user.UserId,
                EntityType = "Project",
                EntityId = projectId.ToString(),
                Action = "ProjectLocationsReplace",
                NewValues =
                    JsonSerializer.Serialize(
                        new
                        {
                            projectId,
                            locationIds = ids
                        }),
                CreatedAt = DateTime.UtcNow
            },
            ct);

        await uow.SaveChangesAsync(ct);

        return await repository
            .GetAssignedLocationsAsync(
                projectId,
                ct);
    }

    private CurrentUserDto RequireAdmin()
    {
        var user =
            current.GetRequired();

        if (!user.Roles.Contains(
            "admin",
            StringComparer.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                "只有管理者可以維護專案固定地點。");
        }

        if (!user.OrganizationId.HasValue)
        {
            throw new InvalidOperationException(
                "目前帳號缺少 OrganizationId。");
        }

        return user;
    }

    private async Task<Project> RequireProjectAsync(
        CurrentUserDto user,
        int projectId,
        CancellationToken ct)
    {
        if (projectId <= 0)
            throw new InvalidOperationException(
                "ProjectId 必須大於 0。");

        var project =
            await repository.GetProjectAsync(
                projectId,
                ct)
            ?? throw new KeyNotFoundException(
                "找不到專案。");

        if (project.OrganizationId
            != user.OrganizationId)
        {
            throw new UnauthorizedAccessException(
                "無權維護其他組織的專案固定地點。");
        }

        return project;
    }
}
