namespace FieldVisit.Application;

public sealed record V180SimpleBulkPreviewItem(
    int RowNumber,
    string Sheet,
    string Key,
    string Action,
    string Status,
    string? ErrorMessage);

public sealed record V180SimpleBulkPreviewDto(
    int TotalCount,
    int ValidCount,
    int ErrorCount,
    IReadOnlyList<V180SimpleBulkPreviewItem> Items);

public sealed record V180SimpleBulkConfirmResultDto(
    int AppliedCount,
    int NoChangeCount,
    int FailedCount,
    IReadOnlyList<string> Errors);

public sealed record V180BatchAddTeamMembersRequest(
    IReadOnlyList<int> UserIds,
    DateOnly EffectiveFrom);

public sealed record V180BatchAddTeamMembersResult(
    int AddedCount,
    int NoChangeCount);

public interface IV180PersonnelBulkService
{
    Task<ReportExportContext> CreateTemplateAsync(
        CurrentUserDto admin,
        CancellationToken ct);

    Task<V180SimpleBulkPreviewDto> PreviewAsync(
        CurrentUserDto admin,
        byte[] content,
        CancellationToken ct);

    Task<V180SimpleBulkConfirmResultDto> ConfirmAsync(
        CurrentUserDto admin,
        byte[] content,
        CancellationToken ct);
}

public interface IV180PeopleManagementBulkService
{
    Task<ReportExportContext> CreateTeamMembershipTemplateAsync(
        CurrentUserDto admin,
        CancellationToken ct);

    Task<V180SimpleBulkPreviewDto> PreviewTeamMembershipAsync(
        CurrentUserDto admin,
        byte[] content,
        CancellationToken ct);

    Task<V180SimpleBulkConfirmResultDto> ConfirmTeamMembershipAsync(
        CurrentUserDto admin,
        byte[] content,
        CancellationToken ct);

    Task<V180BatchAddTeamMembersResult> BatchAddTeamMembersAsync(
        CurrentUserDto admin,
        int teamId,
        V180BatchAddTeamMembersRequest request,
        CancellationToken ct);
}
