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
