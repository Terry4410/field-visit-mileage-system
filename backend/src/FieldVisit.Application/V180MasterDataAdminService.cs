namespace FieldVisit.Application;

public sealed class V180MasterDataAdminService(
    ICurrentUserService current,
    IV180MasterDataAdminRepository repository)
{
    public Task<V180MasterDataWorkspaceDto> GetWorkspaceAsync(CancellationToken ct)
        => repository.GetWorkspaceAsync(RequireAdmin(), BusinessTime.Today, ct);
    public Task<V180MasterDataSaveResultDto> SaveEmploymentStatusAsync(SaveV180EmploymentStatusRequest request,CancellationToken ct)
        => repository.SaveEmploymentStatusAsync(RequireAdmin(),request,ct);
    public Task<V180MasterDataSaveResultDto> SaveCenterAsync(SaveV180CenterRequest request,CancellationToken ct)
        => repository.SaveCenterAsync(RequireAdmin(),request,ct);
    public Task<V180MasterDataSaveResultDto> SaveTeamCenterAsync(SaveV180TeamCenterRequest request,CancellationToken ct)
        => repository.SaveTeamCenterAsync(RequireAdmin(),request,ct);
    public Task<V180MasterDataSaveResultDto> SaveDeploymentSiteAsync(SaveV180DeploymentSiteRequest request,CancellationToken ct)
        => repository.SaveDeploymentSiteAsync(RequireAdmin(),request,ct);
    public Task<V180MasterDataSaveResultDto> SaveTeamSiteAsync(SaveV180TeamSiteRequest request,CancellationToken ct)
        => repository.SaveTeamSiteAsync(RequireAdmin(),request,ct);
    public Task<V180MasterDataSaveResultDto> SaveEmploymentSiteAsync(SaveV180EmploymentSiteRequest request,CancellationToken ct)
        => repository.SaveEmploymentSiteAsync(RequireAdmin(),request,ct);

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
