namespace FieldVisit.Application;

public sealed class V180MasterDataAdminService(ICurrentUserService current, IV180MasterDataAdminRepository repository, IV180MasterDataBulkWorkbookService? bulk = null)
{
    private CurrentUserDto Admin() { var u=current.GetRequired(); if (!u.Roles.Any(x=>x.Equals("admin",StringComparison.OrdinalIgnoreCase))) throw new UnauthorizedAccessException("只有管理者可以維護 v1.8 主檔。"); if (!u.OrganizationId.HasValue) throw new InvalidOperationException("目前管理者缺少 OrganizationId。"); return u; }
    public Task<V180MasterDataReadinessDto> ReadinessAsync(CancellationToken ct)=>repository.GetReadinessAsync(Admin(),ct);
    public Task<IReadOnlyList<V180MasterDataRow>> ListAsync(string kind,CancellationToken ct)=>repository.ListAsync(Admin(),kind,ct);
    public Task<V180MasterDataRow> SaveEmploymentStatusAsync(long? id,V180EmploymentStatusInput x,CancellationToken ct)=>repository.SaveEmploymentStatusAsync(Admin(),id,x,ct);
    public Task<V180MasterDataRow> SaveCenterAsync(int? id,V180CenterInput x,CancellationToken ct)=>repository.SaveCenterAsync(Admin(),id,x,ct);
    public Task<V180MasterDataRow> SaveTeamCenterAsync(long? id,V180TeamCenterInput x,CancellationToken ct)=>repository.SaveTeamCenterAsync(Admin(),id,x,ct);
    public Task<V180MasterDataRow> SaveDeploymentSiteAsync(int? id,V180DeploymentSiteInput x,CancellationToken ct)=>repository.SaveDeploymentSiteAsync(Admin(),id,x,ct);
    public Task<V180MasterDataRow> SaveTeamSiteAsync(long? id,V180TeamSiteInput x,CancellationToken ct)=>repository.SaveTeamSiteAsync(Admin(),id,x,ct);
    public Task<V180MasterDataRow> SaveEmploymentSiteAsync(long? id,V180EmploymentSiteInput x,CancellationToken ct)=>repository.SaveEmploymentSiteAsync(Admin(),id,x,ct);
    public Task<ReportExportContext> CreateBulkTemplateAsync(CancellationToken ct)=>(bulk ?? throw new InvalidOperationException("V180_BULK_WORKBOOK_SERVICE_NOT_REGISTERED")).CreateTemplateAsync(Admin(),ct);
    public Task<ImportPreviewDto> PreviewBulkAsync(byte[] content,CancellationToken ct)=>(bulk ?? throw new InvalidOperationException("V180_BULK_WORKBOOK_SERVICE_NOT_REGISTERED")).PreviewAsync(Admin(),content,ct);
    public Task<ImportConfirmResultDto> ConfirmBulkAsync(Guid importBatchId,CancellationToken ct)=>(bulk ?? throw new InvalidOperationException("V180_BULK_WORKBOOK_SERVICE_NOT_REGISTERED")).ConfirmAsync(Admin(),importBatchId,ct);
}
