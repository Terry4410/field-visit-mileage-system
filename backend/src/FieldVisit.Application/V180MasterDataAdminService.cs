namespace FieldVisit.Application;

public sealed class V180MasterDataAdminService(
    ICurrentUserService current,
    IV180MasterDataAdminRepository repository,
    IV180MasterDataWorkbookService workbook)
{
    public Task<V180MasterDataWorkspaceDto> GetWorkspaceAsync(CancellationToken ct)
        => repository.GetWorkspaceAsync(RequireAdmin(),BusinessTime.Today,ct);
    public Task<V180MasterDataSaveResultDto> SaveEmploymentStatusAsync(SaveV180EmploymentStatusRequest r,CancellationToken ct)
        => repository.SaveEmploymentStatusAsync(RequireAdmin(),r,ct);
    public Task<V180MasterDataSaveResultDto> SaveCenterAsync(SaveV180CenterRequest r,CancellationToken ct)
        => repository.SaveCenterAsync(RequireAdmin(),r,ct);
    public Task<V180MasterDataSaveResultDto> SaveTeamCenterAsync(SaveV180TeamCenterRequest r,CancellationToken ct)
        => repository.SaveTeamCenterAsync(RequireAdmin(),r,ct);
    public Task<V180MasterDataSaveResultDto> SaveDeploymentSiteAsync(SaveV180DeploymentSiteRequest r,CancellationToken ct)
        => repository.SaveDeploymentSiteAsync(RequireAdmin(),r,ct);
    public Task<V180MasterDataSaveResultDto> SaveTeamSiteAsync(SaveV180TeamSiteRequest r,CancellationToken ct)
        => repository.SaveTeamSiteAsync(RequireAdmin(),r,ct);
    public Task<V180MasterDataSaveResultDto> SaveEmploymentSiteAsync(SaveV180EmploymentSiteRequest r,CancellationToken ct)
        => repository.SaveEmploymentSiteAsync(RequireAdmin(),r,ct);
    public Task<ReportExportContext> CreateTemplateAsync(CancellationToken ct)
        => workbook.CreateTemplateAsync(RequireAdmin(),ct);
    public Task<V180MasterDataBulkPreviewDto> PreviewBulkAsync(byte[] content,CancellationToken ct)
        => workbook.PreviewAsync(RequireAdmin(),content,ct);
    public Task<ReportExportContext> BulkErrorReportAsync(Guid id,CancellationToken ct)
        => workbook.CreateErrorReportAsync(RequireAdmin(),id,ct);
    public Task<V180MasterDataBulkConfirmResultDto> ConfirmBulkAsync(Guid id,V180MasterDataBulkConfirmRequest r,CancellationToken ct)
        => workbook.ConfirmAsync(RequireAdmin(),id,r,ct);

    private CurrentUserDto RequireAdmin()
    {
        var user=current.GetRequired();
        if(!user.Roles.Any(x=>x.Equals("admin",StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("只有 Business Admin 可以維護 UAT 主檔。");
        if(!user.OrganizationId.HasValue)
            throw new InvalidOperationException("目前 Business Admin 缺少 OrganizationId。");
        return user;
    }
}
