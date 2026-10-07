namespace FieldVisit.Application;

public sealed class V170LocationService(
    ICurrentUserService current,
    IV170LocationRepository locations,
    IUnitOfWork uow)
{
    public async Task<V170LocationSearchResult> SearchAsync(
        V170LocationSearchRequest request,
        CancellationToken ct)
    {
        var user = RequirePickerUser();

        var spec =
            V170LocationSearchRules.Normalize(request);

        EnsureRequestedTeamAllowed(
            user,
            spec.TeamId);

        return await locations.SearchAsync(
            user,
            spec,
            ct);
    }

    public async Task<IReadOnlyList<V170LocationFavoriteDto>>
        GetFavoritesAsync(
            int? teamId,
            CancellationToken ct)
    {
        var user = RequirePickerUser();

        EnsureRequestedTeamAllowed(
            user,
            teamId);

        return await locations.GetFavoritesAsync(
            user,
            teamId,
            ct);
    }

    public async Task AddFavoriteAsync(
        int locationId,
        CancellationToken ct)
    {
        var user = RequirePickerUser();

        V170LocationPickerRules.EnsureLocationId(
            locationId);

        var changed =
            await locations.AddFavoriteAsync(
                user,
                locationId,
                ct);

        if (changed)
            await uow.SaveChangesAsync(ct);
    }

    public async Task RemoveFavoriteAsync(
        int locationId,
        CancellationToken ct)
    {
        var user = RequirePickerUser();

        V170LocationPickerRules.EnsureLocationId(
            locationId);

        var changed =
            await locations.RemoveFavoriteAsync(
                user,
                locationId,
                ct);

        if (changed)
            await uow.SaveChangesAsync(ct);
    }

    public async Task ReorderFavoritesAsync(
        V170LocationFavoriteOrderRequest request,
        CancellationToken ct)
    {
        var user = RequirePickerUser();

        var ids =
            V170LocationPickerRules
                .NormalizeFavoriteOrder(request);

        var changed =
            await locations.ReorderFavoritesAsync(
                user,
                ids,
                ct);

        if (changed)
            await uow.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<V170LocationRecentDto>>
        GetRecentAsync(
            int limit,
            int? teamId,
            CancellationToken ct)
    {
        var user = RequirePickerUser();

        EnsureRequestedTeamAllowed(
            user,
            teamId);

        var normalizedLimit =
            V170LocationPickerRules
                .NormalizeRecentLimit(limit);

        return await locations.GetRecentAsync(
            user,
            normalizedLimit,
            teamId,
            ct);
    }

    public async Task<V170LocationMaintenanceDto> GetMaintenanceAsync(
        int locationId,int? teamId,CancellationToken ct)
    {
        var user=RequirePickerUser();
        EnsureRequestedTeamAllowed(user,teamId);
        return await locations.GetMaintenanceAsync(user,V170LocationPickerRules.EnsureLocationId(locationId),teamId,ct);
    }

    public Task<V170LocationMaintenanceDto> UpdateMaintenanceAsync(
        int locationId,V170LocationMaintenanceUpdateRequest request,CancellationToken ct)
        => locations.UpdateMaintenanceAsync(RequirePickerUser(),V170LocationPickerRules.EnsureLocationId(locationId),request,ct);

    public async Task<V170LocationMaintenanceDto> AddNoteAsync(
        int locationId,V170LocationNoteRequest request,CancellationToken ct)
    {
        var user=RequirePickerUser();
        EnsureRequestedTeamAllowed(user,request.TeamId);
        if(string.IsNullOrWhiteSpace(request.Note))throw new InvalidOperationException("備註內容不可空白。");
        if(request.Note.Trim().Length>1000)throw new InvalidOperationException("備註內容不可超過 1000 個字元。");
        return await locations.AddNoteAsync(user,V170LocationPickerRules.EnsureLocationId(locationId),request with { Note=request.Note.Trim() },ct);
    }

    public Task<PagedResult<V170LocationDuplicateReviewRowDto>> GetDuplicateReviewQueueAsync(
        V170LocationDuplicateReviewRequest request,CancellationToken ct)
        => locations.GetDuplicateReviewQueueAsync(
            RequireAdmin(),
            V170LocationDuplicateReviewRules.Normalize(request),
            ct);

    public Task<IReadOnlyList<V170LocationDuplicateCandidateDto>> GetDuplicateCandidatesAsync(
        int locationId,CancellationToken ct)
        => locations.GetDuplicateCandidatesAsync(RequireAdmin(),V170LocationPickerRules.EnsureLocationId(locationId),ct);

    public Task ConfirmDistinctAsync(
        int sourceLocationId,V170LocationDuplicateDistinctRequest request,CancellationToken ct)
    {
        if(!request.Confirm)throw new InvalidOperationException("疑似重複地點人工覆核必須明確確認。");
        if(string.IsNullOrWhiteSpace(request.Reason))throw new InvalidOperationException("人工覆核原因為必填。");
        return locations.ConfirmDistinctAsync(
            RequireAdmin(),
            V170LocationPickerRules.EnsureLocationId(sourceLocationId),
            request with { Reason=request.Reason.Trim() },
            ct);
    }

    public Task<V170LocationMergePreviewDto> PreviewMergeAsync(
        int sourceLocationId,int survivorLocationId,CancellationToken ct)
        => locations.PreviewMergeAsync(RequireAdmin(),sourceLocationId,survivorLocationId,ct);

    public Task MergeAsync(
        int sourceLocationId,V170LocationMergeRequest request,CancellationToken ct)
    {
        if(!request.Confirm)throw new InvalidOperationException("地點合併必須明確確認。");
        if(string.IsNullOrWhiteSpace(request.Reason))throw new InvalidOperationException("地點合併原因為必填。");
        if(request.Mode is not ("UseExisting" or "MergeFields"))
            throw new InvalidOperationException("地點合併模式不正確。");
        if(request.Mode=="MergeFields"&&request.FinalMaster is null)
            throw new InvalidOperationException("合併主檔模式必須指定最後保留的主檔內容。");
        return locations.MergeAsync(RequireAdmin(),sourceLocationId,request with { Reason=request.Reason.Trim() },ct);
    }

    public async Task<IReadOnlyList<V170LocationNearbyDto>>
        GetNearbyAsync(
            decimal latitude,
            decimal longitude,
            int? projectId,
            int limit,
            int? teamId,
            CancellationToken ct)
    {
        var user = RequirePickerUser();

        var spec =
            V170LocationPickerRules
                .NormalizeNearby(
                    new V170LocationNearbyRequest(
                        latitude,
                        longitude,
                        projectId,
                        limit,
                        teamId));

        EnsureRequestedTeamAllowed(
            user,
            spec.TeamId);

        return await locations.GetNearbyAsync(
            user,
            spec,
            ct);
    }

    private CurrentUserDto RequireAdmin()
    {
        var user=RequirePickerUser();
        if(!user.Roles.Contains("admin",StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("只有管理者可以執行疑似重複判斷與地點合併。");
        return user;
    }

    private CurrentUserDto RequirePickerUser()
    {
        var user = current.GetRequired();

        V170LocationSearchRules.EnsurePickerRole(
            user.Roles);

        if (!user.OrganizationId.HasValue)
        {
            throw new UnauthorizedAccessException(
                "目前帳號缺少 Organization scope，無法使用地點選擇功能。");
        }

        return user;
    }

    private static void EnsureRequestedTeamAllowed(
        CurrentUserDto user,
        int? teamId)
    {
        if (!teamId.HasValue)
            return;

        var isAdmin =
            user.Roles.Contains(
                "admin",
                StringComparer.OrdinalIgnoreCase);

        if (isAdmin)
            return;

        if (!user.TeamIds.Contains(teamId.Value))
        {
            throw new UnauthorizedAccessException(
                "無權查詢未授權小組的地點。");
        }
    }
}
