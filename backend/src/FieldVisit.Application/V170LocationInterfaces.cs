namespace FieldVisit.Application;

public interface IV170LocationRepository
{
    Task<V170LocationSearchResult> SearchAsync(
        CurrentUserDto user,
        V170LocationSearchSpec spec,
        CancellationToken ct);
}
