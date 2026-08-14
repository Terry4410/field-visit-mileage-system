using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V170LocationRepository(
    AppDbContext db) : IV170LocationRepository
{
    public async Task<V170LocationSearchResult> SearchAsync(
        CurrentUserDto user,
        V170LocationSearchSpec spec,
        CancellationToken ct)
    {
        var q =
            db.Locations
                .AsNoTracking()
                .Where(x =>
                    x.IsActive
                    && x.ApprovalStatus == "Approved");

        // ------------------------------------------------------------
        // Organization scope
        // ------------------------------------------------------------

        if (user.OrganizationId.HasValue)
        {
            var organizationId =
                user.OrganizationId.Value;

            q = q.Where(x =>
                x.OrganizationId == organizationId
                || x.OrganizationId == null);
        }
        else
        {
            q = q.Where(_ => false);
        }

        // ------------------------------------------------------------
        // Team scope
        // Admin = organization-wide.
        // Leader / Visitor = own effective team memberships + global.
        // Supervisor is intentionally not a Smart Picker role.
        // ------------------------------------------------------------

        var isAdmin =
            user.Roles.Contains(
                "admin",
                StringComparer.OrdinalIgnoreCase);

        var isLeader =
            user.Roles.Contains(
                "leader",
                StringComparer.OrdinalIgnoreCase);

        var isVisitor =
            user.Roles.Contains(
                "visitor",
                StringComparer.OrdinalIgnoreCase);

        if (!isAdmin)
        {
            if (isLeader || isVisitor)
            {
                var teamIds =
                    user.TeamIds.ToArray();

                q = teamIds.Length > 0
                    ? q.Where(x =>
                        x.TeamId == null
                        || (x.TeamId.HasValue
                            && teamIds.Contains(
                                x.TeamId.Value)))
                    : q.Where(_ => false);
            }
            else
            {
                q = q.Where(_ => false);
            }
        }

        // ------------------------------------------------------------
        // Optional project-list restriction.
        //
        // This lets Phase 2F replace the current behavior where the
        // browser downloads the whole ProjectLocation list first.
        // ------------------------------------------------------------

        if (spec.ProjectId.HasValue)
        {
            var projectId =
                spec.ProjectId.Value;

            var project =
                await db.Projects
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.ProjectId == projectId,
                        ct)
                ?? throw new KeyNotFoundException(
                    "找不到專案。");

            if (user.OrganizationId.HasValue
                && project.OrganizationId
                    != user.OrganizationId.Value)
            {
                throw new UnauthorizedAccessException(
                    "無權使用其他 Organization 專案。");
            }

            if (!isAdmin
                && project.TeamId.HasValue
                && !user.TeamIds.Contains(
                    project.TeamId.Value))
            {
                throw new UnauthorizedAccessException(
                    "無權使用未授權小組專案。");
            }

            var today =
                BusinessTime.Today;

            if (!project.IsActive
                || (project.StartDate.HasValue
                    && project.StartDate.Value > today)
                || (project.EndDate.HasValue
                    && project.EndDate.Value < today))
            {
                throw new InvalidOperationException(
                    "專案目前不在可使用期間。");
            }

            q = q.Where(location =>
                db.ProjectLocations
                    .AsNoTracking()
                    .Any(pl =>
                        pl.ProjectId == projectId
                        && pl.LocationId
                            == location.LocationId
                        && pl.IsActive));
        }

        // ------------------------------------------------------------
        // Structured filters
        // ------------------------------------------------------------

        if (spec.City is not null)
        {
            var city = spec.City;

            q = q.Where(x =>
                x.City == city);
        }

        if (spec.District is not null)
        {
            var district = spec.District;

            q = q.Where(x =>
                x.District == district);
        }

        // ------------------------------------------------------------
        // Keyword search
        //
        // Government TaxId is used only when the government candidate
        // has already been explicitly matched to an application
        // Location. Pending candidates never appear in the picker.
        // ------------------------------------------------------------

        if (spec.Query is not null)
        {
            var keyword =
                spec.Query;

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
                    && x.PlusCode.Contains(keyword))
                || db.GovernmentLocationMasters
                    .AsNoTracking()
                    .Any(g =>
                        g.IsActive
                        && g.ReviewStatus
                            == GovernmentLocationReviewStatuses.Matched
                        && g.MatchedLocationId
                            == x.LocationId
                        && g.TaxId != null
                        && g.TaxId.Contains(keyword)));
        }

        var totalCount =
            await q.CountAsync(ct);

        IOrderedQueryable<Location> ordered;

        if (spec.Query is not null)
        {
            var keyword =
                spec.Query;

            // Relevance:
            // exact code
            // exact name
            // name prefix
            // code prefix
            // name contains
            // stable geographic/name ordering
            ordered =
                q.OrderByDescending(x =>
                        x.LocationCode != null
                        && x.LocationCode == keyword)
                    .ThenByDescending(x =>
                        x.LocationName == keyword)
                    .ThenByDescending(x =>
                        x.LocationName.StartsWith(
                            keyword))
                    .ThenByDescending(x =>
                        x.LocationCode != null
                        && x.LocationCode.StartsWith(
                            keyword))
                    .ThenByDescending(x =>
                        x.LocationName.Contains(
                            keyword))
                    .ThenBy(x => x.City)
                    .ThenBy(x => x.District)
                    .ThenBy(x => x.LocationName)
                    .ThenBy(x => x.LocationId);
        }
        else
        {
            ordered =
                q.OrderBy(x => x.City)
                    .ThenBy(x => x.District)
                    .ThenBy(x => x.LocationName)
                    .ThenBy(x => x.LocationId);
        }

        var skip =
            (spec.Page - 1)
            * spec.PageSize;

        var items =
            await ordered
                .Skip(skip)
                .Take(spec.PageSize)
                .Select(x =>
                    new V170LocationSearchItemDto(
                        x.LocationId,
                        x.LocationCode,
                        x.LocationName,
                        x.LocationType,
                        x.City,
                        x.District,
                        x.Address,
                        x.PlusCode,
                        x.Latitude,
                        x.Longitude))
                .ToListAsync(ct);

        var hasNextPage =
            (long)spec.Page
            * spec.PageSize
            < totalCount;

        return new V170LocationSearchResult(
            items,
            spec.Page,
            spec.PageSize,
            totalCount,
            hasNextPage);
    }
}
