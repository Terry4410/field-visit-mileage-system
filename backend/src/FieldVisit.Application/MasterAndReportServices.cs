using FieldVisit.Domain;
using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public sealed class MasterService(
    ICurrentUserService current,
    IMasterRepository masters,
    IMileageRepository mileage,
    IGeocodingService geocoding,
    IWorkflowRepository workflow,
    IUnitOfWork uow)
{
    public async Task<List<TeamDto>> TeamsAsync(CancellationToken ct)
    {
        var user = current.GetRequired();
        var rows = await masters.GetTeamsAsync(user, ct);
        return rows.Select(x => new TeamDto(x.TeamId, x.OrganizationId, x.TeamCode, x.TeamName)).ToList();
    }

    public async Task<List<LocationDto>> LocationsAsync(CancellationToken ct)
    {
        var user = current.GetRequired();
        return (await masters.GetLocationsAsync(user, true, ct)).Select(MapLocation).ToList();
    }

    public async Task<List<LocationDto>> PendingLocationsAsync(DateOnly? start, DateOnly? end, CancellationToken ct)
    {
        var user = RequireAny("leader", "admin");
        var startDt = start?.ToDateTime(TimeOnly.MinValue);
        var endDt = end?.ToDateTime(TimeOnly.MaxValue);
        return (await masters.GetPendingLocationsAsync(user, startDt, endDt, ct)).Select(MapLocation).ToList();
    }

    public async Task<LocationDto> UpdateLocationAsync(int id, UpdateLocationRequest request, CancellationToken ct)
    {
        var user = RequireAny("leader", "admin");
        var row = await masters.GetLocationAsync(id, true, ct) ?? throw new KeyNotFoundException("找不到地點。");
        if (!HasRole(user, "admin") && HasRole(user, "leader") && (!row.TeamId.HasValue || !user.TeamIds.Contains(row.TeamId.Value))) throw new UnauthorizedAccessException("無權維護未授權小組地點。");
        if (HasRole(user, "admin") && user.OrganizationId.HasValue && row.OrganizationId.HasValue && row.OrganizationId != user.OrganizationId) throw new UnauthorizedAccessException("無權維護其他 Organization 地點。");
        EnsureRowVersion(row.RowVersion, request.RowVersion);
        if (string.IsNullOrWhiteSpace(request.Address) && string.IsNullOrWhiteSpace(request.PlusCode))
            throw new InvalidOperationException("完整地址與 Plus Code 至少需要一項。");

        var normalizedAddress =
            V170LocationGeocodingRules.Normalize(request.Address);
        var normalizedPlusCode =
            V170LocationGeocodingRules.Normalize(request.PlusCode);

        var geocodingInputChanged =
            V170LocationGeocodingRules.GeocodingInputChanged(
                row.Address,
                row.PlusCode,
                normalizedAddress,
                normalizedPlusCode);

        row.LocationName = request.LocationName.Trim();
        row.City = request.City;
        row.District = request.District;
        row.Address = normalizedAddress;
        row.PlusCode = normalizedPlusCode;

        // Location name / descriptive edits must not invalidate coordinates.
        // Only inputs actually used by IGeocodingService require re-geocoding.
        if (geocodingInputChanged)
        {
            row.GeocodingStatus = "Pending";
        }

        row.UpdatedAt = DateTime.UtcNow;

        await workflow.AddAuditAsync(Audit(user.UserId, "Location", id.ToString(), "LocationUpdate", new { request.LocationName, request.Address, request.PlusCode }), ct);
        await uow.SaveChangesAsync(ct);
        return MapLocation(row);
    }

    public async Task<BatchPublishLocationsResult> BatchPublishAsync(BatchPublishLocationsRequest request, CancellationToken ct)
    {
        var user = RequireAny("leader", "admin");
        int success = 0, failed = 0;
        var errors = new List<string>();

        foreach (var id in request.LocationIds.Distinct())
        {
            var row = await masters.GetLocationAsync(id, true, ct);
            if (row is null) { failed++; errors.Add($"{id}: 找不到地點"); continue; }
            if (!HasRole(user, "admin") && HasRole(user, "leader") && (!row.TeamId.HasValue || !user.TeamIds.Contains(row.TeamId.Value))) { failed++; errors.Add($"{id}: 無權限"); continue; }
            if (HasRole(user, "admin") && user.OrganizationId.HasValue && row.OrganizationId.HasValue && row.OrganizationId != user.OrganizationId) { failed++; errors.Add($"{id}: 無權限"); continue; }

            var geo = await geocoding.ResolveAsync(row.Address, row.PlusCode, ct);
            if (!geo.Success || geo.Latitude is null || geo.Longitude is null)
            {
                row.GeocodingStatus = "Failed";
                failed++; errors.Add($"{id}: {geo.ErrorMessage}");
                continue;
            }

            row.Latitude = geo.Latitude;
            row.Longitude = geo.Longitude;
            row.GeocodingStatus = "Completed";
            row.GeocodedAt = DateTime.UtcNow;
            row.ApprovalStatus = "Approved";
            row.IsActive = true;
            row.UpdatedAt = DateTime.UtcNow;
            await workflow.AddLocationHistoryAsync(new LocationApprovalHistory
            {
                LocationId = row.LocationId,
                Action = "Approved",
                ReviewedByUserId = user.UserId,
                Comments = "UAT batch publish",
                ActionAt = DateTime.UtcNow
            }, ct);
            success++;
        }

        await workflow.AddAuditAsync(Audit(user.UserId, "Location", null, "LocationBatchPublish", new { success, failed }), ct);
        await uow.SaveChangesAsync(ct);
        return new BatchPublishLocationsResult(success, failed, errors);
    }

    public async Task<List<ProjectDto>> ProjectsAsync(CancellationToken ct)
    {
        var user = current.GetRequired();
        var includeInactive = HasRole(user, "admin");
        return (await masters.GetProjectsAsync(user, includeInactive, ct)).Select(MapProject).ToList();
    }

    public async Task<ProjectDto> CreateProjectAsync(SaveProjectRequest request, CancellationToken ct)
    {
        var user = RequireAny("admin");
        var orgId = user.OrganizationId ?? throw new InvalidOperationException("目前帳號缺少 OrganizationId。");
        ValidateProjectRequest(request);
        await EnsureTeamScopeAsync(user, request.TeamId, ct);
        if (await masters.ProjectCodeExistsAsync(orgId, request.ProjectCode, null, ct)) throw new InvalidOperationException("專案代碼已存在。");
        var row = new Project
        {
            OrganizationId = orgId,
            TeamId = request.TeamId,
            ProjectCode = request.ProjectCode.Trim(),
            ProjectName = request.ProjectName.Trim(),
            Description = request.Description?.Trim(),
            LocationMode = NormalizeLocationMode(request.LocationMode),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };
        await masters.AddProjectAsync(row, ct);
        await workflow.AddAuditAsync(Audit(user.UserId, "Project", null, "ProjectCreate", request), ct);
        await uow.SaveChangesAsync(ct);
        return MapProject(row);
    }

    public async Task<ProjectDto> UpdateProjectAsync(int projectId, SaveProjectRequest request, CancellationToken ct)
    {
        var user = RequireAny("admin");
        var orgId = user.OrganizationId ?? throw new InvalidOperationException("目前帳號缺少 OrganizationId。");
        ValidateProjectRequest(request);
        await EnsureTeamScopeAsync(user, request.TeamId, ct);
        var row = await masters.GetProjectAsync(projectId, true, ct) ?? throw new KeyNotFoundException("找不到專案。");
        if (row.OrganizationId != orgId) throw new UnauthorizedAccessException("無權維護其他組織專案。");
        if (await masters.ProjectCodeExistsAsync(orgId, request.ProjectCode, projectId, ct)) throw new InvalidOperationException("專案代碼已存在。");
        row.TeamId = request.TeamId;
        row.ProjectCode = request.ProjectCode.Trim();
        row.ProjectName = request.ProjectName.Trim();
        row.Description = request.Description?.Trim();
        row.LocationMode = NormalizeLocationMode(request.LocationMode);
        row.StartDate = request.StartDate;
        row.EndDate = request.EndDate;
        row.IsActive = request.IsActive;
        row.UpdatedAt = DateTime.UtcNow;
        await workflow.AddAuditAsync(Audit(user.UserId, "Project", projectId.ToString(), "ProjectUpdate", request), ct);
        await uow.SaveChangesAsync(ct);
        return MapProject(row);
    }

    public async Task DeleteProjectAsync(int projectId, CancellationToken ct)
    {
        var user = RequireAny("admin");
        var row = await masters.GetProjectAsync(projectId, true, ct) ?? throw new KeyNotFoundException("找不到專案。");
        if (user.OrganizationId.HasValue && row.OrganizationId != user.OrganizationId.Value) throw new UnauthorizedAccessException("無權維護其他組織專案。");
        row.IsActive = false;
        row.UpdatedAt = DateTime.UtcNow;
        await workflow.AddAuditAsync(Audit(user.UserId, "Project", projectId.ToString(), "ProjectDeactivate", new { projectId }), ct);
        await uow.SaveChangesAsync(ct);
    }

    public async Task<List<LocationDto>> ProjectLocationsAsync(int projectId, CancellationToken ct)
    {
        var user = current.GetRequired();
        return (await masters.GetProjectLocationsAsync(projectId, user, ct)).Select(MapLocation).ToList();
    }

    public async Task<List<VisitTypeDto>> VisitTypesAsync(CancellationToken ct)
    {
        var user = current.GetRequired();
        var includeInactive = HasRole(user, "admin");
        return (await masters.GetVisitTypesAsync(includeInactive, ct)).Select(MapVisitType).ToList();
    }

    public async Task<VisitTypeDto> CreateVisitTypeAsync(SaveVisitTypeRequest request, CancellationToken ct)
    {
        var user = RequireAny("admin");
        ValidateVisitTypeRequest(request);
        if (await masters.VisitTypeCodeExistsAsync(request.VisitTypeCode, null, ct)) throw new InvalidOperationException("拜訪形式代碼已存在。");
        var row = new VisitType
        {
            VisitTypeCode = request.VisitTypeCode.Trim(),
            VisitTypeName = request.VisitTypeName.Trim(),
            Description = request.Description?.Trim(),
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };
        await masters.AddVisitTypeAsync(row, ct);
        await workflow.AddAuditAsync(Audit(user.UserId, "VisitType", null, "VisitTypeCreate", request), ct);
        await uow.SaveChangesAsync(ct);
        return MapVisitType(row);
    }

    public async Task<VisitTypeDto> UpdateVisitTypeAsync(int visitTypeId, SaveVisitTypeRequest request, CancellationToken ct)
    {
        var user = RequireAny("admin");
        ValidateVisitTypeRequest(request);
        var row = await masters.GetVisitTypeAsync(visitTypeId, true, ct) ?? throw new KeyNotFoundException("找不到拜訪形式。");
        if (await masters.VisitTypeCodeExistsAsync(request.VisitTypeCode, visitTypeId, ct)) throw new InvalidOperationException("拜訪形式代碼已存在。");
        row.VisitTypeCode = request.VisitTypeCode.Trim();
        row.VisitTypeName = request.VisitTypeName.Trim();
        row.Description = request.Description?.Trim();
        row.SortOrder = request.SortOrder;
        row.IsActive = request.IsActive;
        row.UpdatedAt = DateTime.UtcNow;
        await workflow.AddAuditAsync(Audit(user.UserId, "VisitType", visitTypeId.ToString(), "VisitTypeUpdate", request), ct);
        await uow.SaveChangesAsync(ct);
        return MapVisitType(row);
    }

    public async Task DeleteVisitTypeAsync(int visitTypeId, CancellationToken ct)
    {
        var user = RequireAny("admin");
        var row = await masters.GetVisitTypeAsync(visitTypeId, true, ct) ?? throw new KeyNotFoundException("找不到拜訪形式。");
        row.IsActive = false;
        row.UpdatedAt = DateTime.UtcNow;
        await workflow.AddAuditAsync(Audit(user.UserId, "VisitType", visitTypeId.ToString(), "VisitTypeDeactivate", new { visitTypeId }), ct);
        await uow.SaveChangesAsync(ct);
    }

    public async Task<List<MileageRateDto>> RatesAsync(CancellationToken ct)
    {
        var user = current.GetRequired();
        return (await mileage.GetRatesAsync(user, ct)).Select(MapRate).ToList();
    }

    public async Task<MileageRateDto> CreateRateAsync(CreateMileageRateRequest request, CancellationToken ct)
    {
        var user = RequireAny("admin");
        ValidateRate(request.RuleName, request.RatePerKm, request.EffectiveFrom, null);
        var vehicle = string.IsNullOrWhiteSpace(request.VehicleType) ? "Motorcycle" : request.VehicleType.Trim();
        var series = await mileage.GetRateSeriesAsync(user.OrganizationId, vehicle, false, ct);
        if (series.Any(x => x.IsActive && x.EffectiveFrom == request.EffectiveFrom)) throw new InvalidOperationException("同一車種不可有兩個同日生效的費率版本。");
        var row = new MileageRateRule { OrganizationId=user.OrganizationId, RuleName=request.RuleName.Trim(), VehicleType=vehicle, RatePerKm=request.RatePerKm, EffectiveFrom=request.EffectiveFrom, EffectiveTo=null, IsActive=true, CreatedAt=DateTime.UtcNow };
        await mileage.AddRateAsync(row, ct); await uow.SaveChangesAsync(ct); await NormalizeRateSeriesAsync(user.OrganizationId, vehicle, ct);
        await workflow.AddAuditAsync(Audit(user.UserId,"MileageRateRule",row.MileageRateRuleId.ToString(),"MileageRateCreate",new{request.RuleName,request.RatePerKm,request.EffectiveFrom}),ct); await uow.SaveChangesAsync(ct); return MapRate(row);
    }

    public async Task<MileageRateDto> UpdateRateAsync(int mileageRateRuleId, UpdateMileageRateRequest request, CancellationToken ct)
    {
        var user=RequireAny("admin"); ValidateRate(request.RuleName,request.RatePerKm,request.EffectiveFrom,null);
        var row=await mileage.GetRateAsync(mileageRateRuleId,true,ct)??throw new KeyNotFoundException("找不到補助費率。");
        if(row.OrganizationId!=user.OrganizationId)throw new UnauthorizedAccessException("無權維護其他組織費率。");
        var oldVehicle=row.VehicleType;
        var vehicle=string.IsNullOrWhiteSpace(request.VehicleType)?"Motorcycle":request.VehicleType.Trim();
        var series=await mileage.GetRateSeriesAsync(user.OrganizationId,vehicle,false,ct);
        if(request.IsActive&&series.Any(x=>x.IsActive&&x.MileageRateRuleId!=mileageRateRuleId&&x.EffectiveFrom==request.EffectiveFrom))throw new InvalidOperationException("同一車種不可有兩個同日生效的費率版本。");
        row.RuleName=request.RuleName.Trim();row.VehicleType=vehicle;row.RatePerKm=request.RatePerKm;row.EffectiveFrom=request.EffectiveFrom;row.EffectiveTo=null;row.IsActive=request.IsActive;row.UpdatedAt=DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);await NormalizeRateSeriesAsync(user.OrganizationId,vehicle,ct);if(!string.Equals(oldVehicle,vehicle,StringComparison.OrdinalIgnoreCase))await NormalizeRateSeriesAsync(user.OrganizationId,oldVehicle,ct);await workflow.AddAuditAsync(Audit(user.UserId,"MileageRateRule",mileageRateRuleId.ToString(),"MileageRateUpdate",new{request.RuleName,request.RatePerKm,request.EffectiveFrom,request.IsActive}),ct);await uow.SaveChangesAsync(ct);return MapRate(row);
    }

    public async Task DeleteRateAsync(int mileageRateRuleId, CancellationToken ct)
    {
        var user=RequireAny("admin");var row=await mileage.GetRateAsync(mileageRateRuleId,true,ct)??throw new KeyNotFoundException("找不到補助費率。");if(row.OrganizationId!=user.OrganizationId)throw new UnauthorizedAccessException("無權維護其他組織費率。");var vehicle=row.VehicleType;row.IsActive=false;row.UpdatedAt=DateTime.UtcNow;await uow.SaveChangesAsync(ct);await NormalizeRateSeriesAsync(user.OrganizationId,vehicle,ct);await workflow.AddAuditAsync(Audit(user.UserId,"MileageRateRule",mileageRateRuleId.ToString(),"MileageRateDeactivate",new{mileageRateRuleId}),ct);await uow.SaveChangesAsync(ct);
    }

    private async Task NormalizeRateSeriesAsync(int? organizationId,string vehicleType,CancellationToken ct)
    {
        var series=(await mileage.GetRateSeriesAsync(organizationId,vehicleType,true,ct)).Where(x=>x.IsActive).OrderBy(x=>x.EffectiveFrom).ThenBy(x=>x.MileageRateRuleId).ToList();
        for(var i=0;i<series.Count;i++) series[i].EffectiveTo=i+1<series.Count?series[i+1].EffectiveFrom.AddDays(-1):null;
        await uow.SaveChangesAsync(ct);
    }

    private async Task EnsureTeamScopeAsync(CurrentUserDto user, int? teamId, CancellationToken ct)
    {
        if (!teamId.HasValue) return;
        var teams = await masters.GetTeamsAsync(user, ct);
        if (!teams.Any(x => x.TeamId == teamId.Value)) throw new InvalidOperationException("所選小組不存在或不屬於目前組織。");
    }

    private static void ValidateProjectRequest(SaveProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectCode)) throw new InvalidOperationException("專案代碼為必填。");
        if (string.IsNullOrWhiteSpace(request.ProjectName)) throw new InvalidOperationException("專案名稱為必填。");
        if (request.EndDate.HasValue && request.StartDate.HasValue && request.EndDate.Value < request.StartDate.Value) throw new InvalidOperationException("專案結束日不可早於開始日。");
        _ = NormalizeLocationMode(request.LocationMode);
    }

    private static string NormalizeLocationMode(string mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "list" or "清單" => "List",
        "selfmaintained" or "self-maintained" or "自行維護" => "SelfMaintained",
        _ => throw new InvalidOperationException("地點模式只允許 List 或 SelfMaintained。")
    };

    private static void ValidateVisitTypeRequest(SaveVisitTypeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VisitTypeCode)) throw new InvalidOperationException("拜訪形式代碼為必填。");
        if (string.IsNullOrWhiteSpace(request.VisitTypeName)) throw new InvalidOperationException("拜訪形式名稱為必填。");
        if (request.SortOrder < 0) throw new InvalidOperationException("排序不可小於 0。");
    }

    private static void ValidateRate(string ruleName, decimal ratePerKm, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (string.IsNullOrWhiteSpace(ruleName)) throw new InvalidOperationException("規則名稱為必填。");
        if (ratePerKm < 0) throw new InvalidOperationException("每公里補助不可小於 0。");
        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom) throw new InvalidOperationException("失效日不可早於生效日。");
    }

    private CurrentUserDto RequireAny(params string[] roles)
    {
        var user = current.GetRequired();
        if (!roles.Any(r => HasRole(user, r))) throw new UnauthorizedAccessException("目前角色無權執行此操作。");
        return user;
    }

    private static bool HasRole(CurrentUserDto user, string role) => user.Roles.Any(x => x.Equals(role, StringComparison.OrdinalIgnoreCase));
    private static void EnsureRowVersion(byte[] currentValue, string expectedBase64)
    {
        byte[] expected; try { expected = Convert.FromBase64String(expectedBase64); } catch { throw new InvalidOperationException("RowVersion 格式不正確。"); }
        if (!currentValue.SequenceEqual(expected)) throw new InvalidOperationException("ROWVERSION_CONFLICT：資料已被其他使用者修改。");
    }
    private static LocationDto MapLocation(Location x) => new(x.LocationId, x.TeamId, x.LocationName, x.LocationType, x.City, x.District, x.Address, x.PlusCode, x.Latitude, x.Longitude, x.IsTemporary, x.ApprovalStatus, x.GeocodingStatus, x.IsActive, x.CreatedAt, Convert.ToBase64String(x.RowVersion ?? []));
    private static ProjectDto MapProject(Project x) => new(x.ProjectId, x.TeamId, x.ProjectCode, x.ProjectName, x.Description, x.LocationMode, x.StartDate, x.EndDate, x.IsActive);
    private static VisitTypeDto MapVisitType(VisitType x) => new(x.VisitTypeId, x.VisitTypeCode, x.VisitTypeName, x.Description, x.SortOrder, x.IsActive);
    private static MileageRateDto MapRate(MileageRateRule x) => new(x.MileageRateRuleId, x.OrganizationId, x.RuleName, x.VehicleType, x.RatePerKm, x.EffectiveFrom, x.EffectiveTo, x.IsActive);
    private static AuditLog Audit(int userId, string entity, string? id, string action, object value) => new() { UserId = userId, EntityType = entity, EntityId = id, Action = action, NewValues = System.Text.Json.JsonSerializer.Serialize(value), CorrelationId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
}
