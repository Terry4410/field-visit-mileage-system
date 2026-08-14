using FieldVisit.Application;
using FieldVisit.Domain;
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
            AccessibleLocations(user);

        var isAdmin =
            user.Roles.Contains(
                "admin",
                StringComparer.OrdinalIgnoreCase);

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

    public async Task<
        IReadOnlyList<V170LocationFavoriteDto>>
        GetFavoritesAsync(
            CurrentUserDto user,
            CancellationToken ct)
    {
        var accessible =
            AccessibleLocations(user);

        return await (
            from favorite
                in db.UserFavoriteLocations
                    .AsNoTracking()
            join location
                in accessible
                on favorite.LocationId
                equals location.LocationId
            where favorite.UserId == user.UserId
            orderby
                favorite.SortOrder,
                favorite.CreatedAt,
                favorite.UserFavoriteLocationId
            select new V170LocationFavoriteDto(
                location.LocationId,
                location.LocationCode,
                location.LocationName,
                location.LocationType,
                location.City,
                location.District,
                location.Address,
                location.PlusCode,
                location.Latitude,
                location.Longitude,
                favorite.SortOrder,
                favorite.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<bool> AddFavoriteAsync(
        CurrentUserDto user,
        int locationId,
        CancellationToken ct)
    {
        var accessible =
            await AccessibleLocations(user)
                .AnyAsync(
                    x => x.LocationId == locationId,
                    ct);

        if (!accessible)
            throw new KeyNotFoundException(
                "找不到可使用的正式地點。");

        var exists =
            await db.UserFavoriteLocations
                .AnyAsync(
                    x =>
                        x.UserId == user.UserId
                        && x.LocationId == locationId,
                    ct);

        if (exists)
            return false;

        var currentMax =
            await db.UserFavoriteLocations
                .Where(x =>
                    x.UserId == user.UserId)
                .Select(x =>
                    (int?)x.SortOrder)
                .MaxAsync(ct);

        await db.UserFavoriteLocations.AddAsync(
            new UserFavoriteLocation
            {
                UserId = user.UserId,
                LocationId = locationId,
                SortOrder =
                    (currentMax ?? -1) + 1,
                CreatedAt = DateTime.UtcNow
            },
            ct);

        return true;
    }

    public async Task<bool> RemoveFavoriteAsync(
        CurrentUserDto user,
        int locationId,
        CancellationToken ct)
    {
        var row =
            await db.UserFavoriteLocations
                .FirstOrDefaultAsync(
                    x =>
                        x.UserId == user.UserId
                        && x.LocationId == locationId,
                    ct);

        if (row is null)
            return false;

        db.UserFavoriteLocations.Remove(row);

        return true;
    }

    public async Task<bool> ReorderFavoritesAsync(
        CurrentUserDto user,
        IReadOnlyList<int> locationIds,
        CancellationToken ct)
    {
        var accessibleIds =
            AccessibleLocations(user)
                .Select(x => x.LocationId);

        var rows =
            await db.UserFavoriteLocations
                .Where(x =>
                    x.UserId == user.UserId
                    && accessibleIds.Contains(
                        x.LocationId))
                .ToListAsync(ct);

        if (rows.Count != locationIds.Count)
        {
            throw new InvalidOperationException(
                "排序清單必須包含目前所有可使用的常用地點。");
        }

        var requested =
            locationIds.ToHashSet();

        if (rows.Any(x =>
            !requested.Contains(x.LocationId)))
        {
            throw new InvalidOperationException(
                "排序清單與目前常用地點不一致。");
        }

        var order =
            locationIds
                .Select((id, index) =>
                    new { id, index })
                .ToDictionary(
                    x => x.id,
                    x => x.index);

        var changed = false;

        foreach (var row in rows)
        {
            var next =
                order[row.LocationId];

            if (row.SortOrder == next)
                continue;

            row.SortOrder = next;
            changed = true;
        }

        return changed;
    }

    public async Task<
        IReadOnlyList<V170LocationRecentDto>>
        GetRecentAsync(
            CurrentUserDto user,
            int limit,
            CancellationToken ct)
    {
        var accessible =
            AccessibleLocations(user);

        var recent =
            from stop
                in db.VisitTripStops.AsNoTracking()
            join trip
                in db.VisitTrips.AsNoTracking()
                on stop.VisitTripId
                equals trip.VisitTripId
            where
                trip.UserId == user.UserId
                && trip.Status != TripStatuses.Cancelled
                && stop.LocationId.HasValue
            group trip
                by stop.LocationId!.Value
                into usage
            select new
            {
                LocationId = usage.Key,
                LastVisitedOn =
                    usage.Max(x => x.VisitDate),
                LastTripId =
                    usage.Max(x => x.VisitTripId)
            };

        return await (
            from usage in recent
            join location
                in accessible
                on usage.LocationId
                equals location.LocationId
            orderby
                usage.LastVisitedOn descending,
                usage.LastTripId descending,
                location.LocationName
            select new V170LocationRecentDto(
                location.LocationId,
                location.LocationCode,
                location.LocationName,
                location.LocationType,
                location.City,
                location.District,
                location.Address,
                location.PlusCode,
                location.Latitude,
                location.Longitude,
                usage.LastVisitedOn))
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<
        IReadOnlyList<V170LocationNearbyDto>>
        GetNearbyAsync(
            CurrentUserDto user,
            V170LocationNearbySpec spec,
            CancellationToken ct)
    {
        var q =
            AccessibleLocations(user)
                .Where(x =>
                    x.Latitude.HasValue
                    && x.Longitude.HasValue);

        var isAdmin =
            user.Roles.Contains(
                "admin",
                StringComparer.OrdinalIgnoreCase);

        // Optional project-list restriction.
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

        // Use a lightweight equirectangular approximation in SQL
        // only to reduce the candidate set. There is intentionally
        // no hard radius such as 5 km.
        //
        // Exact Haversine distance is calculated below for the small
        // candidate set before the final Top-N result is returned.
        var longitudeScale =
            Convert.ToDecimal(
                Math.Cos(
                    (double)spec.Latitude
                    * Math.PI
                    / 180d));

        var candidateLimit =
            Math.Min(
                spec.Limit * 3,
                150);

        var latitude =
            spec.Latitude;

        var longitude =
            spec.Longitude;

        var candidates =
            await q
                .OrderBy(x =>
                    (x.Latitude!.Value - latitude)
                    * (x.Latitude.Value - latitude)
                    +
                    (
                        (x.Longitude!.Value - longitude)
                        * longitudeScale
                    )
                    *
                    (
                        (x.Longitude.Value - longitude)
                        * longitudeScale
                    ))
                .ThenBy(x =>
                    x.LocationId)
                .Take(candidateLimit)
                .Select(x =>
                    new
                    {
                        x.LocationId,
                        x.LocationCode,
                        x.LocationName,
                        x.LocationType,
                        x.City,
                        x.District,
                        x.Address,
                        x.PlusCode,
                        Latitude =
                            x.Latitude!.Value,
                        Longitude =
                            x.Longitude!.Value
                    })
                .ToListAsync(ct);

        return candidates
            .Select(x =>
                new V170LocationNearbyDto(
                    x.LocationId,
                    x.LocationCode,
                    x.LocationName,
                    x.LocationType,
                    x.City,
                    x.District,
                    x.Address,
                    x.PlusCode,
                    x.Latitude,
                    x.Longitude,
                    Math.Round(
                        V170LocationPickerRules
                            .CalculateDistanceKm(
                                latitude,
                                longitude,
                                x.Latitude,
                                x.Longitude),
                        2)))
            .OrderBy(x =>
                x.DistanceKm)
            .ThenBy(x =>
                x.LocationName)
            .ThenBy(x =>
                x.LocationId)
            .Take(spec.Limit)
            .ToList();
    }

    private IQueryable<Location> AccessibleLocations(
        CurrentUserDto user)
    {
        var q =
            db.Locations
                .AsNoTracking()
                .Where(x =>
                    x.IsActive
                    && x.ApprovalStatus == "Approved");

        if (!user.OrganizationId.HasValue)
            return q.Where(_ => false);

        var organizationId =
            user.OrganizationId.Value;

        q = q.Where(x =>
            x.OrganizationId == organizationId
            || x.OrganizationId == null);

        var isAdmin =
            user.Roles.Contains(
                "admin",
                StringComparer.OrdinalIgnoreCase);

        if (isAdmin)
            return q;

        var isLeader =
            user.Roles.Contains(
                "leader",
                StringComparer.OrdinalIgnoreCase);

        var isVisitor =
            user.Roles.Contains(
                "visitor",
                StringComparer.OrdinalIgnoreCase);

        if (!isLeader && !isVisitor)
            return q.Where(_ => false);

        var teamIds =
            user.TeamIds.ToArray();

        if (teamIds.Length == 0)
            return q.Where(_ => false);

        return q.Where(x =>
            x.TeamId == null
            || (x.TeamId.HasValue
                && teamIds.Contains(
                    x.TeamId.Value)));
    }
}
