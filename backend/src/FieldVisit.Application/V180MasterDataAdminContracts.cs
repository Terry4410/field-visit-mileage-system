using System.Globalization;

namespace FieldVisit.Application;

public sealed record V180MasterDataReadinessIssueDto(string Code,string Message,int Count);

public sealed record V180MasterDataReadinessDto(
    DateOnly AsOfDate,bool Ready,int EmploymentCount,int MissingEmploymentStatusCount,
    int TeamCount,int MissingTeamCenterCount,int ActiveDeploymentSiteCount,
    int MissingTeamSiteCount,int MissingEmploymentPrimarySiteCount,bool MileageRateReady,
    IReadOnlyList<V180MasterDataReadinessIssueDto> Issues);

public sealed record V180EmploymentMasterDataDto(
    long EmploymentId,string EmployeeNo,string DisplayName,string? EmploymentStatus,
    DateOnly? StatusEffectiveFrom,DateOnly? StatusEffectiveTo,
    string? PrimaryTeamCode,string? PrimaryTeamName,string? PrimarySiteCode,string? PrimarySiteName);

public sealed record V180TeamMasterDataDto(
    int TeamId,string TeamCode,string TeamName,bool IsActive,DateOnly? EffectiveFrom,DateOnly? EffectiveTo);

public sealed record V180LocationMasterDataDto(
    int LocationId,string LocationCode,string LocationName,string? Address,bool IsActive,string ApprovalStatus);

public sealed record V180CenterMasterDataDto(
    int CenterId,string CenterCode,string CenterName,DateOnly EffectiveFrom,DateOnly? EffectiveTo,bool IsActive,string? Notes);

public sealed record V180TeamCenterMasterDataDto(
    long TeamCenterAssignmentId,string TeamCode,string TeamName,string CenterCode,string CenterName,
    DateOnly EffectiveFrom,DateOnly? EffectiveTo,string? ChangeReason);

public sealed record V180DeploymentSiteMasterDataDto(
    int DeploymentSiteId,string CenterCode,string SiteCode,string SiteName,string? LocationCode,string? LocationName,
    DateOnly EffectiveFrom,DateOnly? EffectiveTo,bool IsActive,string? Notes);

public sealed record V180TeamSiteMasterDataDto(
    long TeamDeploymentSiteAssignmentId,string TeamCode,string SiteCode,string SiteName,
    DateOnly EffectiveFrom,DateOnly? EffectiveTo);

public sealed record V180EmploymentSiteMasterDataDto(
    long EmploymentDeploymentSiteAssignmentId,string EmployeeNo,string DisplayName,string SiteCode,string SiteName,
    bool IsPrimary,DateOnly EffectiveFrom,DateOnly? EffectiveTo);

public sealed record V180MasterDataWorkspaceDto(
    V180MasterDataReadinessDto Readiness,
    IReadOnlyList<V180EmploymentMasterDataDto> Employments,
    IReadOnlyList<V180TeamMasterDataDto> Teams,
    IReadOnlyList<V180LocationMasterDataDto> Locations,
    IReadOnlyList<V180CenterMasterDataDto> Centers,
    IReadOnlyList<V180TeamCenterMasterDataDto> TeamCenters,
    IReadOnlyList<V180DeploymentSiteMasterDataDto> DeploymentSites,
    IReadOnlyList<V180TeamSiteMasterDataDto> TeamSites,
    IReadOnlyList<V180EmploymentSiteMasterDataDto> EmploymentSites);

public sealed record SaveV180EmploymentStatusRequest(
    string EmployeeNo,string EmploymentStatus,DateOnly EffectiveFrom,DateOnly? EffectiveTo);

public sealed record SaveV180CenterRequest(
    string CenterCode,string CenterName,DateOnly EffectiveFrom,DateOnly? EffectiveTo,
    bool IsActive=true,string? Notes=null);

public sealed record SaveV180TeamCenterRequest(
    string TeamCode,string CenterCode,DateOnly EffectiveFrom,DateOnly? EffectiveTo,string? ChangeReason=null);

public sealed record SaveV180DeploymentSiteRequest(
    string CenterCode,string SiteCode,string SiteName,string LocationCode,
    DateOnly EffectiveFrom,DateOnly? EffectiveTo,bool IsActive=true,string? Notes=null,string? ChangeReason=null);

public sealed record SaveV180TeamSiteRequest(
    string TeamCode,string SiteCode,DateOnly EffectiveFrom,DateOnly? EffectiveTo);

public sealed record SaveV180EmploymentSiteRequest(
    string EmployeeNo,string SiteCode,bool IsPrimary,DateOnly EffectiveFrom,DateOnly? EffectiveTo);

public sealed record V180MasterDataSaveResultDto(string EntityType,string Action,string DisplayKey);

public sealed record V180MasterDataBulkPreviewItemDto(
    int RowNumber,string Sheet,string EntityType,string Action,string DisplayKey,string Status,string? Message,bool IsRetroactive);

public sealed record V180MasterDataBulkPreviewDto(
    Guid ImportBatchId,int TotalCount,int ValidCount,int ErrorCount,bool RequiresRetroactiveConfirmation,
    IReadOnlyList<V180MasterDataBulkPreviewItemDto> Items);

public sealed record V180MasterDataBulkConfirmRequest(bool ConfirmRetroactive=false);

public sealed record V180MasterDataBulkConfirmResultDto(
    Guid ImportBatchId,int Created,int Updated,int Unchanged,int Failed,IReadOnlyList<string> Errors);

public interface IV180MasterDataAdminRepository
{
    Task<V180MasterDataWorkspaceDto> GetWorkspaceAsync(CurrentUserDto admin,DateOnly asOfDate,CancellationToken ct);
    Task<V180MasterDataSaveResultDto> SaveEmploymentStatusAsync(CurrentUserDto admin,SaveV180EmploymentStatusRequest request,CancellationToken ct);
    Task<V180MasterDataSaveResultDto> SaveCenterAsync(CurrentUserDto admin,SaveV180CenterRequest request,CancellationToken ct);
    Task<V180MasterDataSaveResultDto> SaveTeamCenterAsync(CurrentUserDto admin,SaveV180TeamCenterRequest request,CancellationToken ct);
    Task<V180MasterDataSaveResultDto> SaveDeploymentSiteAsync(CurrentUserDto admin,SaveV180DeploymentSiteRequest request,CancellationToken ct);
    Task<V180MasterDataSaveResultDto> SaveTeamSiteAsync(CurrentUserDto admin,SaveV180TeamSiteRequest request,CancellationToken ct);
    Task<V180MasterDataSaveResultDto> SaveEmploymentSiteAsync(CurrentUserDto admin,SaveV180EmploymentSiteRequest request,CancellationToken ct);
}

public interface IV180MasterDataWorkbookService
{
    Task<ReportExportContext> CreateTemplateAsync(CurrentUserDto admin,CancellationToken ct);
    Task<V180MasterDataBulkPreviewDto> PreviewAsync(CurrentUserDto admin,byte[] content,CancellationToken ct);
    Task<ReportExportContext> CreateErrorReportAsync(CurrentUserDto admin,Guid importBatchId,CancellationToken ct);
    Task<V180MasterDataBulkConfirmResultDto> ConfirmAsync(
        CurrentUserDto admin,Guid importBatchId,V180MasterDataBulkConfirmRequest request,CancellationToken ct);
}

public static class V180MasterDataRules
{
    private static readonly HashSet<string> EmploymentStatuses =
        new(StringComparer.OrdinalIgnoreCase){"Active","Leave","Terminated","PreHire"};
    private static readonly string[] DateFormats=["yyyy-MM-dd","yyyy/M/d","yyyy/MM/dd"];

    public static string NormalizeCode(string? value,string fieldName)
    {
        var result=(value??"").Trim();
        if(result.Length==0)throw new InvalidOperationException($"{fieldName} 必填。");
        if(result.Length>50)throw new InvalidOperationException($"{fieldName} 不可超過 50 個字元。");
        return result.ToUpperInvariant();
    }

    public static string NormalizeName(string? value,string fieldName,int maxLength=200)
    {
        var result=(value??"").Trim();
        if(result.Length==0)throw new InvalidOperationException($"{fieldName} 必填。");
        if(result.Length>maxLength)throw new InvalidOperationException($"{fieldName} 不可超過 {maxLength} 個字元。");
        return result;
    }

    public static string? NormalizeOptionalText(string? value,int maxLength,string fieldName)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        var result=value.Trim();
        if(result.Length>maxLength)throw new InvalidOperationException($"{fieldName} 不可超過 {maxLength} 個字元。");
        return result;
    }

    public static string NormalizeEmploymentStatus(string? value)
    {
        var result=(value??"").Trim();
        var canonical=EmploymentStatuses.FirstOrDefault(x=>x.Equals(result,StringComparison.OrdinalIgnoreCase));
        return canonical??throw new InvalidOperationException(
            "EmploymentStatus 只允許 Active、Leave、Terminated、PreHire。");
    }

    public static void ValidatePeriod(DateOnly effectiveFrom,DateOnly? effectiveTo,string label)
    {
        if(effectiveTo.HasValue&&effectiveTo.Value<effectiveFrom)
            throw new InvalidOperationException($"{label} 的 EffectiveTo 不可早於 EffectiveFrom。");
    }

    public static bool Overlaps(DateOnly leftFrom,DateOnly? leftTo,DateOnly rightFrom,DateOnly? rightTo)
    {
        var leftEnd=leftTo??DateOnly.MaxValue;
        var rightEnd=rightTo??DateOnly.MaxValue;
        return leftFrom<=rightEnd&&rightFrom<=leftEnd;
    }

    public static bool Covers(DateOnly outerFrom,DateOnly? outerTo,DateOnly innerFrom,DateOnly? innerTo)
    {
        if(outerFrom>innerFrom)return false;
        if(!innerTo.HasValue)return !outerTo.HasValue;
        return !outerTo.HasValue||outerTo.Value>=innerTo.Value;
    }

    public static bool IsEffective(DateOnly effectiveFrom,DateOnly? effectiveTo,DateOnly asOf)
        =>effectiveFrom<=asOf&&(!effectiveTo.HasValue||effectiveTo.Value>=asOf);

    public static DateOnly ParseDate(string? value,string fieldName)
    {
        var raw=(value??"").Trim();
        foreach(var format in DateFormats)
            if(DateOnly.TryParseExact(raw,format,CultureInfo.InvariantCulture,DateTimeStyles.None,out var result))
                return result;
        throw new InvalidOperationException($"{fieldName} 日期格式必須為 yyyy-MM-dd。");
    }

    public static DateOnly? ParseOptionalDate(string? value,string fieldName)
        =>string.IsNullOrWhiteSpace(value)?null:ParseDate(value,fieldName);

    public static bool ParseBoolean(string? value,string fieldName)
    {
        var raw=(value??"").Trim().ToLowerInvariant();
        return raw switch
        {
            "y" or "yes" or "true" or "1" or "是" or "啟用"=>true,
            "n" or "no" or "false" or "0" or "否" or "停用"=>false,
            _=>throw new InvalidOperationException(
                $"{fieldName} 只允許 Yes/No、True/False、1/0、是/否。")
        };
    }
}
