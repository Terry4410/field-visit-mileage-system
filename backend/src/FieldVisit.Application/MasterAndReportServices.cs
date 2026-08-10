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
        if (HasRole(user, "leader") && row.TeamId != user.TeamId) throw new UnauthorizedAccessException("無權維護其他小組地點。");
        EnsureRowVersion(row.RowVersion, request.RowVersion);
        if (string.IsNullOrWhiteSpace(request.Address) && string.IsNullOrWhiteSpace(request.PlusCode))
            throw new InvalidOperationException("完整地址與 Plus Code 至少需要一項。");

        row.LocationName = request.LocationName.Trim();
        row.City = request.City;
        row.District = request.District;
        row.Address = request.Address;
        row.PlusCode = request.PlusCode;
        row.GeocodingStatus = "Pending";
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
            if (HasRole(user, "leader") && row.TeamId != user.TeamId) { failed++; errors.Add($"{id}: 無權限"); continue; }

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
        return (await masters.GetProjectsAsync(user, ct)).Select(x => new ProjectDto(x.ProjectId, x.TeamId, x.ProjectCode, x.ProjectName, x.LocationMode, x.IsActive)).ToList();
    }

    public async Task<List<LocationDto>> ProjectLocationsAsync(int projectId, CancellationToken ct)
    {
        var user = current.GetRequired();
        return (await masters.GetProjectLocationsAsync(projectId, user, ct)).Select(MapLocation).ToList();
    }

    public async Task<List<VisitTypeDto>> VisitTypesAsync(CancellationToken ct) =>
        (await masters.GetVisitTypesAsync(ct)).Select(x => new VisitTypeDto(x.VisitTypeId, x.VisitTypeCode, x.VisitTypeName, x.SortOrder)).ToList();

    public async Task<List<MileageRateDto>> RatesAsync(CancellationToken ct)
    {
        var user = current.GetRequired();
        return (await mileage.GetRatesAsync(user, ct)).Select(MapRate).ToList();
    }

    public async Task<MileageRateDto> CreateRateAsync(CreateMileageRateRequest request, CancellationToken ct)
    {
        var user = RequireAny("admin");
        if (request.RatePerKm < 0) throw new InvalidOperationException("每公里補助不可小於 0。");
        if (request.EffectiveTo.HasValue && request.EffectiveTo.Value < request.EffectiveFrom) throw new InvalidOperationException("失效日不可早於生效日。");

        var row = new MileageRateRule
        {
            OrganizationId = user.OrganizationId,
            RuleName = request.RuleName.Trim(),
            VehicleType = string.IsNullOrWhiteSpace(request.VehicleType) ? "Motorcycle" : request.VehicleType.Trim(),
            RatePerKm = request.RatePerKm,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        await mileage.AddRateAsync(row, ct);
        await workflow.AddAuditAsync(Audit(user.UserId, "MileageRateRule", null, "MileageRateCreate", request), ct);
        await uow.SaveChangesAsync(ct);
        return MapRate(row);
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
    private static MileageRateDto MapRate(MileageRateRule x) => new(x.MileageRateRuleId, x.OrganizationId, x.RuleName, x.VehicleType, x.RatePerKm, x.EffectiveFrom, x.EffectiveTo, x.IsActive);
    private static AuditLog Audit(int userId, string entity, string? id, string action, object value) => new() { UserId = userId, EntityType = entity, EntityId = id, Action = action, NewValues = System.Text.Json.JsonSerializer.Serialize(value), CorrelationId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
}

public sealed class ReportService(ICurrentUserService current, ITripRepository trips, IUserRepository users)
{
    public async Task<List<MileageReportRow>> MileageAsync(DateOnly? start, DateOnly? end, CancellationToken ct)
    {
        var user = current.GetRequired();
        var rows = await trips.GetReportTripsAsync(user, start, end, ct);
        var result = new List<MileageReportRow>();
        foreach (var trip in rows)
        {
            var profile = await users.GetProfileAsync(trip.UserId, ct);
            var calc = trip.MileageCalculation;
            result.Add(new MileageReportRow(
                trip.TripNo, trip.VisitDate, profile?.DisplayName ?? $"User {trip.UserId}", profile?.TeamName,
                string.Join(" → ", trip.Stops.OrderBy(x => x.StopSequence).Select(x => x.LocationNameSnapshot)),
                calc?.ClaimedDistanceKm, calc?.SystemDistanceKm, calc?.ApprovedDistanceKm,
                calc?.RatePerKmSnapshot, calc?.ApprovedAmount, trip.Status, TripStatuses.Display(trip.Status)));
        }
        return result;
    }
}
