using System.Text.Json;
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
            AccessibleLocations(
                user,
                spec.TeamId);

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
                || (x.TaxId != null
                    && x.TaxId.Contains(keyword))
                || (x.MasterNote != null
                    && x.MasterNote.Contains(keyword))
                || db.TeamLocationNotes.AsNoTracking().Any(n =>
                    n.LocationId == x.LocationId
                    && n.Note != null
                    && n.Note.Contains(keyword))
                || db.TeamLocationNoteHistories.AsNoTracking().Any(h =>
                    h.LocationId == x.LocationId
                    && ((h.NewNote != null && h.NewNote.Contains(keyword))
                        || (h.OldNote != null && h.OldNote.Contains(keyword))))
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
            int? teamId,
            CancellationToken ct)
    {
        var accessible =
            AccessibleLocations(
                user,
                teamId);

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

            // Explicitly mark SortOrder as modified so the reorder
            // cannot silently depend on query tracking behavior.
            db.Entry(row)
                .Property(x => x.SortOrder)
                .IsModified = true;

            changed = true;
        }

        return changed;
    }

    public async Task<
        IReadOnlyList<V170LocationRecentDto>>
        GetRecentAsync(
            CurrentUserDto user,
            int limit,
            int? teamId,
            CancellationToken ct)
    {
        var accessible =
            AccessibleLocations(
                user,
                teamId);

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
                && (!teamId.HasValue
                    || trip.TeamId == teamId.Value)
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
            AccessibleLocations(
                user,
                spec.TeamId)
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


    public async Task<V170LocationMaintenanceDto> GetMaintenanceAsync(
        CurrentUserDto user,int locationId,int? teamId,CancellationToken ct)
    {
        var row=await AccessibleLocations(user,teamId)
            .FirstOrDefaultAsync(x=>x.LocationId==locationId,ct)
            ?? throw new KeyNotFoundException("找不到可維護的正式地點。");

        var teamName=row.TeamId.HasValue
            ?await db.Teams.AsNoTracking().Where(x=>x.TeamId==row.TeamId.Value).Select(x=>x.TeamName).FirstOrDefaultAsync(ct)
            :null;

        var allowedTeamIds=user.Roles.Contains("admin",StringComparer.OrdinalIgnoreCase)
            ?null
            :user.TeamIds.ToArray();

        var noteQuery=
            from h in db.TeamLocationNoteHistories.AsNoTracking()
            join t in db.Teams.AsNoTracking() on h.TeamId equals t.TeamId
            join u in db.Users.AsNoTracking() on h.ChangedByUserId equals u.UserId
            where h.LocationId==locationId
            select new {h,t.TeamName,u.DisplayName};

        if(teamId.HasValue) noteQuery=noteQuery.Where(x=>x.h.TeamId==teamId.Value);
        else if(allowedTeamIds is not null) noteQuery=noteQuery.Where(x=>allowedTeamIds.Contains(x.h.TeamId));

        var notes=await noteQuery
            .OrderByDescending(x=>x.h.ChangedAt)
            .ThenByDescending(x=>x.h.TeamLocationNoteHistoryId)
            .Take(100)
            .Select(x=>new V170LocationNoteEntryDto(
                x.h.TeamLocationNoteHistoryId,
                x.h.TeamId,
                x.TeamName,
                x.h.NewNote??x.h.OldNote??"",
                x.h.Action,
                x.h.ChangeReason,
                x.h.ChangedAt,
                x.h.ChangedByUserId,
                x.DisplayName))
            .ToListAsync(ct);

        var audits=await (
            from a in db.AuditLogs.AsNoTracking()
            join u0 in db.Users.AsNoTracking() on a.UserId equals (int?)u0.UserId into users
            from u in users.DefaultIfEmpty()
            where a.EntityType=="Location"
                && a.EntityId==locationId.ToString()
                && (a.Action=="LocationMaintenanceUpdate"||a.Action=="LocationMerge")
            orderby a.CreatedAt descending,a.AuditLogId descending
            select new V170LocationAuditDto(
                a.AuditLogId,a.Action,a.OldValues,a.NewValues,a.CreatedAt,a.UserId,u!=null?u.DisplayName:null))
            .Take(50)
            .ToListAsync(ct);

        return new V170LocationMaintenanceDto(
            row.LocationId,row.LocationCode,row.LocationName,row.LocationType,row.TeamId,teamName,
            row.City,row.District,row.Address,row.PlusCode,row.TaxId,row.MasterNote,row.IsActive,
            row.DuplicateOfLocationId,row.DuplicateReason,notes,audits,B64(row.RowVersion));
    }

    public async Task<V170LocationMaintenanceDto> UpdateMaintenanceAsync(
        CurrentUserDto user,int locationId,V170LocationMaintenanceUpdateRequest request,CancellationToken ct)
    {
        var accessible=await AccessibleLocations(user).AnyAsync(x=>x.LocationId==locationId,ct);
        if(!accessible)throw new KeyNotFoundException("找不到可維護的正式地點。");

        var row=await db.Locations.SingleAsync(x=>x.LocationId==locationId,ct);
        EnsureRowVersion(row.RowVersion,request.RowVersion);

        var before=new
        {
            row.City,row.District,row.Address,row.PlusCode,row.TaxId,row.MasterNote,
            row.SelectedGeocodingAttemptId,row.GeocodingStatus
        };

        var nextCity=TrimToNull(request.City);
        var nextDistrict=TrimToNull(request.District);
        var nextAddress=TrimToNull(request.Address);
        var nextPlus=TrimToNull(request.PlusCode);
        var nextTax=TrimToNull(request.TaxId);
        var nextMasterNote=TrimToNull(request.MasterNote);

        if(nextTax is {Length:>20})throw new InvalidOperationException("統一編號不可超過 20 個字元。");
        if(nextMasterNote is {Length:>1000})throw new InvalidOperationException("主檔備註不可超過 1000 個字元。");

        var addressChanged=!string.Equals(row.Address,nextAddress,StringComparison.Ordinal)
            ||!string.Equals(row.PlusCode,nextPlus,StringComparison.Ordinal);

        row.City=nextCity;
        row.District=nextDistrict;
        row.Address=nextAddress;
        row.PlusCode=nextPlus;
        row.TaxId=nextTax;
        row.MasterNote=nextMasterNote;
        row.UpdatedAt=DateTime.UtcNow;

        if(addressChanged)
        {
            row.GeocodingStatus="Pending";
            row.GeocodedAt=null;
            row.SelectedGeocodingAttemptId=null;
        }

        var now=DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLog
        {
            UserId=user.UserId,EntityType="Location",EntityId=locationId.ToString(),
            Action="LocationMaintenanceUpdate",
            OldValues=JsonSerializer.Serialize(before),
            NewValues=JsonSerializer.Serialize(new
            {
                row.City,row.District,row.Address,row.PlusCode,row.TaxId,row.MasterNote,
                AddressChanged=addressChanged
            }),
            CreatedAt=now
        });

        await db.SaveChangesAsync(ct);
        return await GetMaintenanceAsync(user,locationId,null,ct);
    }

    public async Task<V170LocationMaintenanceDto> AddNoteAsync(
        CurrentUserDto user,int locationId,V170LocationNoteRequest request,CancellationToken ct)
    {
        if(!user.Roles.Contains("admin",StringComparer.OrdinalIgnoreCase)
            &&!user.TeamIds.Contains(request.TeamId))
            throw new UnauthorizedAccessException("無權新增其他小組的地點備註。");

        var accessible=await AccessibleLocations(user,request.TeamId).AnyAsync(x=>x.LocationId==locationId,ct);
        if(!accessible)throw new KeyNotFoundException("找不到可維護的正式地點。");

        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var row=await db.TeamLocationNotes
            .SingleOrDefaultAsync(x=>x.TeamId==request.TeamId&&x.LocationId==locationId,ct);
        var now=DateTime.UtcNow;
        var old=row?.Note;
        var action=row is null?"Created":"Updated";

        if(row is null)
        {
            row=new TeamLocationNote
            {
                TeamId=request.TeamId,LocationId=locationId,Note=request.Note,
                CreatedAt=now,CreatedByUserId=user.UserId
            };
            db.TeamLocationNotes.Add(row);
            await db.SaveChangesAsync(ct);
        }
        else
        {
            row.Note=request.Note;
            row.UpdatedAt=now;
            row.UpdatedByUserId=user.UserId;
            await db.SaveChangesAsync(ct);
        }

        db.TeamLocationNoteHistories.Add(new TeamLocationNoteHistory
        {
            TeamLocationNoteId=row.TeamLocationNoteId,
            TeamId=request.TeamId,
            LocationId=locationId,
            Action=action,
            OldNote=old,
            NewNote=request.Note,
            ChangeReason=TrimToNull(request.ChangeReason),
            ChangedAt=now,
            ChangedByUserId=user.UserId
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await GetMaintenanceAsync(user,locationId,request.TeamId,ct);
    }

    public async Task<IReadOnlyList<V170LocationDuplicateCandidateDto>> GetDuplicateCandidatesAsync(
        CurrentUserDto admin,int locationId,CancellationToken ct)
    {
        var org=admin.OrganizationId??throw new UnauthorizedAccessException("管理者缺少 Organization scope。");
        var source=await db.Locations.AsNoTracking()
            .SingleOrDefaultAsync(x=>x.LocationId==locationId&&x.OrganizationId==org,ct)
            ??throw new KeyNotFoundException("找不到地點。");

        var candidates=await db.Locations.AsNoTracking()
            .Where(x=>x.OrganizationId==org&&x.LocationId!=locationId&&x.DuplicateOfLocationId==null)
            .Where(x=>
                (source.TaxId!=null&&source.TaxId!=""&&x.TaxId==source.TaxId)
                ||x.LocationName==source.LocationName
                ||(source.Address!=null&&source.Address!=""&&x.Address==source.Address)
                ||(source.PlusCode!=null&&source.PlusCode!=""&&x.PlusCode==source.PlusCode))
            .OrderByDescending(x=>x.IsActive)
            .ThenBy(x=>x.LocationName)
            .Take(50)
            .ToListAsync(ct);

        return candidates.Select(x=>
        {
            var reasons=new List<string>();
            if(!string.IsNullOrWhiteSpace(source.TaxId)&&string.Equals(source.TaxId,x.TaxId,StringComparison.OrdinalIgnoreCase))reasons.Add("統一編號");
            if(string.Equals(source.LocationName,x.LocationName,StringComparison.OrdinalIgnoreCase))reasons.Add("名稱");
            if(!string.IsNullOrWhiteSpace(source.Address)&&string.Equals(source.Address,x.Address,StringComparison.OrdinalIgnoreCase))reasons.Add("地址");
            if(!string.IsNullOrWhiteSpace(source.PlusCode)&&string.Equals(source.PlusCode,x.PlusCode,StringComparison.OrdinalIgnoreCase))reasons.Add("Plus Code");
            return new V170LocationDuplicateCandidateDto(
                x.LocationId,x.LocationCode,x.LocationName,x.Address,x.PlusCode,x.TaxId,reasons);
        }).ToList();
    }

    public async Task<V170LocationMergePreviewDto> PreviewMergeAsync(
        CurrentUserDto admin,int sourceLocationId,int survivorLocationId,CancellationToken ct)
    {
        if(sourceLocationId==survivorLocationId)throw new InvalidOperationException("來源地點與保留地點不可相同。");
        var org=admin.OrganizationId??throw new UnauthorizedAccessException("管理者缺少 Organization scope。");
        var source=await db.Locations.AsNoTracking().SingleOrDefaultAsync(x=>x.LocationId==sourceLocationId&&x.OrganizationId==org,ct)
            ??throw new KeyNotFoundException("找不到來源地點。");
        var survivor=await db.Locations.AsNoTracking().SingleOrDefaultAsync(x=>x.LocationId==survivorLocationId&&x.OrganizationId==org,ct)
            ??throw new KeyNotFoundException("找不到保留地點。");

        var today=BusinessTime.Today;
        var tripRefs=await db.VisitTripStops.AsNoTracking().CountAsync(x=>x.LocationId==sourceLocationId,ct);
        var projectRefs=await db.ProjectLocations.AsNoTracking().CountAsync(x=>x.LocationId==sourceLocationId,ct);
        var favoriteRefs=await db.UserFavoriteLocations.AsNoTracking().CountAsync(x=>x.LocationId==sourceLocationId,ct);
        var noteRefs=await db.TeamLocationNoteHistories.AsNoTracking().CountAsync(x=>x.LocationId==sourceLocationId,ct);
        var deploymentRefs=await db.DeploymentSiteLocationAssignments.AsNoTracking()
            .CountAsync(x=>x.LocationId==sourceLocationId&&(!x.EffectiveTo.HasValue||x.EffectiveTo.Value>=today),ct);

        string? blocking=null;
        if(!survivor.IsActive)blocking="保留地點必須為啟用狀態。";
        else if(source.DuplicateOfLocationId.HasValue)blocking="來源地點已標記為其他地點的 duplicate。";
        else if(deploymentRefs>0)blocking="來源地點仍有 current/future 派駐據點關聯，必須先由管理者調整該關聯。";

        return new V170LocationMergePreviewDto(
            sourceLocationId,survivorLocationId,blocking is null,blocking,
            tripRefs,projectRefs,favoriteRefs,noteRefs,deploymentRefs);
    }

    public async Task MergeAsync(
        CurrentUserDto admin,int sourceLocationId,V170LocationMergeRequest request,CancellationToken ct)
    {
        var preview=await PreviewMergeAsync(admin,sourceLocationId,request.SurvivorLocationId,ct);
        if(!preview.CanMerge)throw new InvalidOperationException(preview.BlockingReason??"此地點目前不可合併。");

        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var source=await db.Locations.SingleAsync(x=>x.LocationId==sourceLocationId,ct);
        var survivor=await db.Locations.SingleAsync(x=>x.LocationId==request.SurvivorLocationId,ct);
        EnsureRowVersion(source.RowVersion,request.SourceRowVersion);

        var before=new
        {
            source.LocationId,source.LocationCode,source.LocationName,source.Address,source.PlusCode,source.TaxId,
            source.IsActive,source.DuplicateOfLocationId,source.DuplicateReason
        };

        // Current operational references follow the survivor.
        // Historical VisitTripStop / Snapshot references intentionally remain on source.
        var sourceProjectRows=await db.ProjectLocations
            .Where(x=>x.LocationId==sourceLocationId&&x.IsActive)
            .ToListAsync(ct);
        var projectRebound=0;
        foreach(var sourceProject in sourceProjectRows)
        {
            var survivorProject=await db.ProjectLocations
                .FirstOrDefaultAsync(x=>x.ProjectId==sourceProject.ProjectId&&x.LocationId==survivor.LocationId,ct);

            if(survivorProject is null)
            {
                sourceProject.LocationId=survivor.LocationId;
                projectRebound++;
            }
            else
            {
                survivorProject.IsActive=true;
                survivorProject.IsPrimary=survivorProject.IsPrimary||sourceProject.IsPrimary;
                sourceProject.IsActive=false;
                projectRebound++;
            }
        }

        var sourceFavorites=await db.UserFavoriteLocations
            .Where(x=>x.LocationId==sourceLocationId)
            .ToListAsync(ct);
        var favoriteRebound=0;
        foreach(var sourceFavorite in sourceFavorites)
        {
            var alreadyExists=await db.UserFavoriteLocations
                .AnyAsync(x=>x.UserId==sourceFavorite.UserId&&x.LocationId==survivor.LocationId,ct);
            if(alreadyExists)
                db.UserFavoriteLocations.Remove(sourceFavorite);
            else
                sourceFavorite.LocationId=survivor.LocationId;
            favoriteRebound++;
        }

        var governmentRebound=await db.GovernmentLocationMasters
            .Where(x=>x.MatchedLocationId==sourceLocationId)
            .ExecuteUpdateAsync(setters=>setters
                .SetProperty(x=>x.MatchedLocationId,survivor.LocationId)
                .SetProperty(x=>x.UpdatedAt,DateTime.UtcNow),ct);

        source.IsActive=false;
        source.InactivatedAt=DateTime.UtcNow;
        source.InactivatedByUserId=admin.UserId;
        source.DuplicateOfLocationId=survivor.LocationId;
        source.DuplicateReason=request.Reason;
        source.UpdatedAt=DateTime.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            UserId=admin.UserId,EntityType="Location",EntityId=sourceLocationId.ToString(),
            Action="LocationMerge",
            OldValues=JsonSerializer.Serialize(before),
            NewValues=JsonSerializer.Serialize(new
            {
                SurvivorLocationId=survivor.LocationId,
                SurvivorLocationCode=survivor.LocationCode,
                SurvivorLocationName=survivor.LocationName,
                request.Reason,
                HistoricalTripAndSnapshotReferencesPreserved=true,
                NotesAndNoteHistoryPreservedOnSource=true,
                CurrentProjectReferencesRebound=projectRebound,
                CurrentFavoriteReferencesRebound=favoriteRebound,
                GovernmentMatchesRebound=governmentRebound
            }),
            CreatedAt=DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value)?null:value.Trim();

    private static string B64(byte[] value)
        => Convert.ToBase64String(value??[]);

    private static void EnsureRowVersion(byte[] actual,string expected)
    {
        byte[] parsed;
        try{parsed=Convert.FromBase64String(expected);}
        catch{throw new InvalidOperationException("RowVersion 格式不正確，請重新載入資料。");}
        if(!actual.SequenceEqual(parsed))
            throw new InvalidOperationException("資料已被其他使用者更新，請重新載入後再操作。");
    }

    private IQueryable<Location> AccessibleLocations(
        CurrentUserDto user,
        int? requestedTeamId = null)
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

        if (requestedTeamId.HasValue)
        {
            var selectedTeamId =
                requestedTeamId.Value;

            q = q.Where(x =>
                x.TeamId == null
                || x.TeamId == selectedTeamId);
        }

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
