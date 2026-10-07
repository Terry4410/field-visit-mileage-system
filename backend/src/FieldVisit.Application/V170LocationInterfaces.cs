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

    Task<V170LocationMaintenanceDto> GetMaintenanceAsync(
        CurrentUserDto user,int locationId,int? teamId,CancellationToken ct);
    Task<V170LocationMaintenanceDto> UpdateMaintenanceAsync(
        CurrentUserDto user,int locationId,V170LocationMaintenanceUpdateRequest request,CancellationToken ct);
    Task<V170LocationMaintenanceDto> AddNoteAsync(
        CurrentUserDto user,int locationId,V170LocationNoteRequest request,CancellationToken ct);
    Task<PagedResult<V170LocationDuplicateReviewRowDto>> GetDuplicateReviewQueueAsync(
        CurrentUserDto admin,V170LocationDuplicateReviewSpec spec,CancellationToken ct);
    Task<IReadOnlyList<V170LocationDuplicateCandidateDto>> GetDuplicateCandidatesAsync(
        CurrentUserDto admin,int locationId,CancellationToken ct);
    Task ConfirmDistinctAsync(
        CurrentUserDto admin,int sourceLocationId,V170LocationDuplicateDistinctRequest request,CancellationToken ct);
    Task<V170LocationMergePreviewDto> PreviewMergeAsync(
        CurrentUserDto admin,int sourceLocationId,int survivorLocationId,CancellationToken ct);
    Task MergeAsync(
        CurrentUserDto admin,int sourceLocationId,V170LocationMergeRequest request,CancellationToken ct);
}
