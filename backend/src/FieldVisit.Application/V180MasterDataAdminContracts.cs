namespace FieldVisit.Application;

public sealed record V180MasterDataReadinessDto(int EmploymentStatusMissingCount, int TeamCenterMissingCount, int DeploymentSiteCount, int TeamSiteMissingCount, int EmploymentSiteMissingCount, bool IsUatReady);
public sealed record V180EmploymentStatusInput(string EmployeeNo, string Status, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? RowVersion = null);
public sealed record V180CenterInput(string CenterCode, string CenterName, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, string? RowVersion = null);
public sealed record V180TeamCenterInput(string TeamCode, string CenterCode, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? RowVersion = null);
public sealed record V180DeploymentSiteInput(string CenterCode, string SiteCode, string SiteName, string LocationCode, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, string? RowVersion = null);
public sealed record V180LocationOfficialSiteInput(int LocationId, string CenterCode, string? SiteName, DateOnly EffectiveFrom);
public sealed record V180LocationOfficialSiteDto(
    int LocationId,
    string LocationCode,
    string LocationName,
    bool IsOfficialSite,
    int? DeploymentSiteId,
    string? SiteCode,
    string? SiteName,
    string? CenterCode,
    string? CenterName,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    bool? IsActive);

public sealed record V180TeamSiteInput(string TeamCode, string SiteCode, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? RowVersion = null);
public sealed record V180EmploymentSiteInput(string EmployeeNo, string SiteCode, bool IsPrimary, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string? RowVersion = null);
public sealed record V180MasterDataRow(long Id, string Key, string? ParentKey, string? Detail, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool? IsActive, bool? IsPrimary, string? RowVersion, string? ReferenceKey = null);

public interface IV180MasterDataBulkWorkbookService
{
    Task<ReportExportContext> CreateTemplateAsync(CurrentUserDto admin, CancellationToken ct);
    Task<ImportPreviewDto> PreviewAsync(CurrentUserDto admin, byte[] content, CancellationToken ct);
    Task<ImportConfirmResultDto> ConfirmAsync(CurrentUserDto admin, Guid importBatchId, CancellationToken ct);
}

public interface IV180MasterDataAdminRepository
{
    Task<V180MasterDataReadinessDto> GetReadinessAsync(CurrentUserDto admin, CancellationToken ct);
    Task<IReadOnlyList<V180MasterDataRow>> ListAsync(CurrentUserDto admin, string kind, CancellationToken ct);
    Task<V180MasterDataRow> SaveEmploymentStatusAsync(CurrentUserDto admin, long? id, V180EmploymentStatusInput input, CancellationToken ct);
    Task<V180MasterDataRow> SaveCenterAsync(CurrentUserDto admin, int? id, V180CenterInput input, CancellationToken ct);
    Task<V180MasterDataRow> SaveTeamCenterAsync(CurrentUserDto admin, long? id, V180TeamCenterInput input, CancellationToken ct);
    Task<V180MasterDataRow> SaveDeploymentSiteAsync(CurrentUserDto admin, int? id, V180DeploymentSiteInput input, CancellationToken ct);
    Task<V180LocationOfficialSiteDto> GetLocationOfficialSiteAsync(CurrentUserDto admin, int locationId, CancellationToken ct);
    Task<V180LocationOfficialSiteDto> EnsureLocationOfficialSiteAsync(CurrentUserDto admin, V180LocationOfficialSiteInput input, CancellationToken ct);
    Task<V180MasterDataRow> SaveTeamSiteAsync(CurrentUserDto admin, long? id, V180TeamSiteInput input, CancellationToken ct);
    Task<V180MasterDataRow> SaveEmploymentSiteAsync(CurrentUserDto admin, long? id, V180EmploymentSiteInput input, CancellationToken ct);
}
