using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V170ProjectLocationAdminRepository(
    AppDbContext db)
    : IV170ProjectLocationAdminRepository
{
    public Task<Project?> GetProjectAsync(
        int projectId,
        CancellationToken ct)
    {
        return db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ProjectId == projectId,
                ct);
    }

    public async Task<
        IReadOnlyList<V170ProjectLocationItemDto>>
        GetAssignedLocationsAsync(
            int projectId,
            CancellationToken ct)
    {
        return await (
            from assignment
                in db.ProjectLocations
                    .AsNoTracking()
            join location
                in db.Locations
                    .AsNoTracking()
                on assignment.LocationId
                equals location.LocationId
            where
                assignment.ProjectId == projectId
                && assignment.IsActive
            orderby
                location.City,
                location.District,
                location.LocationName,
                location.LocationId
            select new V170ProjectLocationItemDto(
                location.LocationId,
                location.LocationCode,
                location.LocationName,
                location.City,
                location.District,
                location.Address,
                location.PlusCode))
            .ToListAsync(ct);
    }

    public async Task<V170ProjectLocationCandidateResult>
        SearchCandidatesAsync(
            int organizationId,
            int? projectTeamId,
            V170ProjectLocationCandidateSpec spec,
            CancellationToken ct)
    {
        var q =
            EligibleLocations(
                organizationId,
                projectTeamId);

        var keyword =
            spec.Query!;

        q = q.Where(x =>
            x.LocationName.Contains(keyword)
            || (x.LocationCode != null
                && x.LocationCode.Contains(keyword))
            || (x.City != null
                && x.City.Contains(keyword))
            || (x.District != null
                && x.District.Contains(keyword))
            || (x.Address != null
                && x.Address.Contains(keyword))
            || (x.PlusCode != null
                && x.PlusCode.Contains(keyword)));

        var total =
            await q.CountAsync(ct);

        var rows =
            await q
                .OrderByDescending(x =>
                    x.LocationCode != null
                    && x.LocationCode == keyword)
                .ThenByDescending(x =>
                    x.LocationName == keyword)
                .ThenByDescending(x =>
                    x.LocationName.StartsWith(keyword))
                .ThenBy(x => x.City)
                .ThenBy(x => x.District)
                .ThenBy(x => x.LocationName)
                .ThenBy(x => x.LocationId)
                .Skip(
                    (spec.Page - 1)
                    * spec.PageSize)
                .Take(spec.PageSize)
                .Select(x =>
                    new V170ProjectLocationItemDto(
                        x.LocationId,
                        x.LocationCode,
                        x.LocationName,
                        x.City,
                        x.District,
                        x.Address,
                        x.PlusCode))
                .ToListAsync(ct);

        return new V170ProjectLocationCandidateResult(
            rows,
            spec.Page,
            spec.PageSize,
            total,
            (long)spec.Page
                * spec.PageSize
                < total);
    }

    public Task<int> CountEligibleLocationsAsync(
        int organizationId,
        int? projectTeamId,
        IReadOnlyCollection<int> locationIds,
        CancellationToken ct)
    {
        if (locationIds.Count == 0)
            return Task.FromResult(0);

        return EligibleLocations(
                organizationId,
                projectTeamId)
            .CountAsync(
                x => locationIds.Contains(
                    x.LocationId),
                ct);
    }

    public Task<List<ProjectLocation>>
        GetAssignmentsAsync(
            int projectId,
            CancellationToken ct)
    {
        return db.ProjectLocations
            .Where(x =>
                x.ProjectId == projectId)
            .OrderBy(x =>
                x.ProjectLocationId)
            .ToListAsync(ct);
    }

    public Task AddAssignmentAsync(
        ProjectLocation row,
        CancellationToken ct)
    {
        return db.ProjectLocations
            .AddAsync(
                row,
                ct)
            .AsTask();
    }

    private IQueryable<Location> EligibleLocations(
        int organizationId,
        int? projectTeamId)
    {
        var q =
            db.Locations
                .AsNoTracking()
                .Where(x =>
                    !x.IsTemporary
                    && x.IsActive
                    && x.ApprovalStatus
                        == "Approved"
                    && (x.OrganizationId
                            == organizationId
                        || x.OrganizationId
                            == null));

        // Team-specific projects can only use:
        // 1. global Locations, or
        // 2. Locations belonging to the same Team.
        if (projectTeamId.HasValue)
        {
            var teamId =
                projectTeamId.Value;

            q = q.Where(x =>
                x.TeamId == null
                || x.TeamId == teamId);
        }

        return q;
    }
}
