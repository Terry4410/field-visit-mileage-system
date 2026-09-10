namespace FieldVisit.Application;

public static class V180TeamLocationNoteStates
{
    public const string NeverExisted = "NeverExisted";
    public const string Active = "Active";
    public const string Cleared = "Cleared";
}

public sealed record V180TeamLocationNoteDto(
    string State,
    long? TeamLocationNoteId,
    int TeamId,
    int LocationId,
    string? Note,
    string? RowVersion,
    DateTime? UpdatedAt);

public sealed record V180CreateTeamLocationNoteRequest(
    int LocationId,
    string? Note,
    string? ChangeReason = null);

public sealed record V180UpdateTeamLocationNoteRequest(
    string? Note,
    string? ChangeReason,
    string RowVersion);

public sealed record V180ClearTeamLocationNoteRequest(
    string? ChangeReason,
    string RowVersion);

public interface IV180TeamLocationNoteService
{
    Task<V180TeamLocationNoteDto> GetAsync(
        CurrentUserDto actor, int teamId, int locationId, CancellationToken ct);

    Task<IReadOnlyList<TeamDto>> GetWritableTeamsAsync(
        CurrentUserDto actor, CancellationToken ct);

    Task<V180TeamLocationNoteDto> CreateAsync(
        CurrentUserDto actor, int teamId, V180CreateTeamLocationNoteRequest request, CancellationToken ct);

    Task<V180TeamLocationNoteDto> UpdateAsync(
        CurrentUserDto actor, long teamLocationNoteId, V180UpdateTeamLocationNoteRequest request, CancellationToken ct);

    Task<V180TeamLocationNoteDto> ClearAsync(
        CurrentUserDto actor, long teamLocationNoteId, V180ClearTeamLocationNoteRequest request, CancellationToken ct);
}
