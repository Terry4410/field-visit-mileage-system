namespace FieldVisit.Application;

public interface IV170LocationRepository
{
    Task<V170LocationSearchResult> SearchAsync(
        CurrentUserDto user,
        V170LocationSearchSpec spec,
        CancellationToken ct);

    Task<IReadOnlyList<V170LocationFavoriteDto>> GetFavoritesAsync(
        CurrentUserDto user,
        int? teamId,
        CancellationToken ct);

    Task<bool> AddFavoriteAsync(
        CurrentUserDto user,
        int locationId,
        CancellationToken ct);

    Task<bool> RemoveFavoriteAsync(
        CurrentUserDto user,
        int locationId,
        CancellationToken ct);

    Task<bool> ReorderFavoritesAsync(
        CurrentUserDto user,
        IReadOnlyList<int> locationIds,
        CancellationToken ct);

    Task<IReadOnlyList<V170LocationRecentDto>> GetRecentAsync(
        CurrentUserDto user,
        int limit,
        int? teamId,
        CancellationToken ct);

    Task<IReadOnlyList<V170LocationNearbyDto>> GetNearbyAsync(
        CurrentUserDto user,
        V170LocationNearbySpec spec,
        CancellationToken ct);
}
