namespace FieldVisit.Application;

public sealed class V170PeopleAdminService(
    ICurrentUserService current,
    IV170PeopleAdminRepository repository,
    IV170PeopleAdminWriter writer,
    IV170PeopleBulkWorkbookService bulk,
    IV180PersonnelBulkService personnelBulk,
    IV180PeopleManagementBulkService managementBulk)
{
    public Task<PagedResult<V170PeopleRowDto>> QueryAsync(V170PeopleQueryRequest request,CancellationToken ct)
    {
        var admin=RequireAdmin();
        return repository.QueryAsync(admin,V170PeopleQueryRules.Normalize(request),ct);
    }

    public Task<V170PersonDetailDto> GetAsync(int userId,CancellationToken ct)
    {
        if(userId<=0)throw new InvalidOperationException("UserId 不正確。");
        return repository.GetAsync(RequireAdmin(),userId,ct);
    }

    public async Task<V170PersonDetailDto> CreateExternalSupervisorAsync(SaveExternalSupervisorRequest request,CancellationToken ct)
    {
        var admin=RequireAdmin();request=V170ExternalSupervisorRules.Normalize(request);
        var userId=await writer.CreateExternalSupervisorAsync(admin,request,ct);
        return await repository.GetAsync(admin,userId,ct);
    }

    public async Task<V170PersonDetailDto> UpdateExternalSupervisorAsync(int userId,UpdateExternalSupervisorRequest request,CancellationToken ct)
    {
        if(userId<=0)throw new InvalidOperationException("UserId 不正確。");
        var admin=RequireAdmin();request=V170ExternalSupervisorUpdateRules.Normalize(request,BusinessTime.Today);
        await writer.UpdateExternalSupervisorAsync(admin,userId,request,ct);
        return await repository.GetAsync(admin,userId,ct);
    }

    public async Task<V170PersonDetailDto> UpdateInternalUserAccessAsync(int userId,UpdateInternalUserAccessRequest request,CancellationToken ct)
    {
        if(userId<=0)throw new InvalidOperationException("UserId 不正確。");
        var admin=RequireAdmin();request=V170InternalUserAccessRules.Normalize(request,BusinessTime.Today);
        await writer.UpdateInternalUserAccessAsync(admin,userId,request,ct);
        return await repository.GetAsync(admin,userId,ct);
    }

    public async Task<V170PersonDetailDto> UpdateInternalEmploymentAsync(int userId,UpdateInternalEmploymentRequest request,CancellationToken ct)
    {
        if(userId<=0)throw new InvalidOperationException("UserId 不正確。");
        if(string.IsNullOrWhiteSpace(request.EmployeeNo))throw new InvalidOperationException("工號為必填。");
        if(string.IsNullOrWhiteSpace(request.DisplayName))throw new InvalidOperationException("姓名為必填。");
        if(request.TerminationDate.HasValue&&request.HireDate.HasValue&&request.TerminationDate.Value<request.HireDate.Value)throw new InvalidOperationException("離職日不可早於入職日。");
        var admin=RequireAdmin();
        await writer.UpdateInternalEmploymentAsync(admin,userId,request with{EmployeeNo=request.EmployeeNo.Trim(),DisplayName=request.DisplayName.Trim(),Email=string.IsNullOrWhiteSpace(request.Email)?null:request.Email.Trim()},ct);
        return await repository.GetAsync(admin,userId,ct);
    }

    public Task<ReportExportContext> ExportBulkCurrentAsync(CancellationToken ct)=>bulk.ExportCurrentAsync(RequireAdmin(),ct);
    public Task<ReportExportContext> CreateBulkTemplateAsync(CancellationToken ct)=>bulk.CreateTemplateAsync(RequireAdmin(),ct);
    public Task<V170PeopleBulkPreviewDto> PreviewBulkAsync(byte[] content,CancellationToken ct)=>bulk.PreviewAsync(RequireAdmin(),content,ct);
    public Task<ReportExportContext> BulkErrorReportAsync(Guid importBatchId,CancellationToken ct)=>bulk.CreateErrorReportAsync(RequireAdmin(),importBatchId,ct);
    public Task<V170PeopleBulkConfirmResultDto> ConfirmBulkAsync(Guid importBatchId,V170PeopleBulkConfirmRequest request,CancellationToken ct)=>bulk.ConfirmAsync(RequireAdmin(),importBatchId,request,ct);

    public Task<ReportExportContext> CreatePersonnelBulkTemplateAsync(CancellationToken ct)=>personnelBulk.CreateTemplateAsync(RequireAdmin(),ct);
    public Task<V180SimpleBulkPreviewDto> PreviewPersonnelBulkAsync(byte[] content,CancellationToken ct)=>personnelBulk.PreviewAsync(RequireAdmin(),content,ct);
    public Task<V180SimpleBulkConfirmResultDto> ConfirmPersonnelBulkAsync(byte[] content,CancellationToken ct)=>personnelBulk.ConfirmAsync(RequireAdmin(),content,ct);
    public Task<ReportExportContext> CreateTeamMembershipBulkTemplateAsync(CancellationToken ct)=>managementBulk.CreateTeamMembershipTemplateAsync(RequireAdmin(),ct);
    public Task<V180SimpleBulkPreviewDto> PreviewTeamMembershipBulkAsync(byte[] content,CancellationToken ct)=>managementBulk.PreviewTeamMembershipAsync(RequireAdmin(),content,ct);
    public Task<V180SimpleBulkConfirmResultDto> ConfirmTeamMembershipBulkAsync(byte[] content,CancellationToken ct)=>managementBulk.ConfirmTeamMembershipAsync(RequireAdmin(),content,ct);
    public Task<V180BatchAddTeamMembersResult> BatchAddTeamMembersAsync(int teamId,V180BatchAddTeamMembersRequest request,CancellationToken ct)=>managementBulk.BatchAddTeamMembersAsync(RequireAdmin(),teamId,request,ct);

    private CurrentUserDto RequireAdmin()
    {
        var user=current.GetRequired();
        if(!user.Roles.Any(x=>x.Equals("admin",StringComparison.OrdinalIgnoreCase)))throw new UnauthorizedAccessException("只有管理者可以維護人員與權限。");
        if(!user.OrganizationId.HasValue)throw new InvalidOperationException("目前管理者缺少 OrganizationId。");
        return user;
    }
}
