using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class BackgroundJobService(
    AppDbContext db,
    IRouteCalculationService route,
    IGeocodingService geocoding) : IBackgroundJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BackgroundJobDto> EnqueueMileageAsync(CurrentUserDto user, MileageBatchRequest request, CancellationToken ct)
    {
        if (!HasRole(user, "leader") || user.TeamIds.Count == 0) throw new UnauthorizedAccessException("只有具有效小組授權的小組長可以建立里程工作。");
        var mode = request.Mode?.Trim() ?? "AllPending";
        if (!(mode.Equals("AllPending", StringComparison.OrdinalIgnoreCase) || mode.Equals("DateRange", StringComparison.OrdinalIgnoreCase) || mode.Equals("Selected", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("里程工作 Mode 只支援 AllPending、DateRange 或 Selected。");
        if (mode.Equals("DateRange", StringComparison.OrdinalIgnoreCase) && (request.StartDate is null || request.EndDate is null || request.EndDate < request.StartDate)) throw new InvalidOperationException("日期區間不正確。");
        if (mode.Equals("Selected", StringComparison.OrdinalIgnoreCase) && (request.SelectedTripIds is null || request.SelectedTripIds.Count == 0)) throw new InvalidOperationException("請先勾選行程。");
        var row = NewJob("Mileage", mode, user, JsonSerializer.Serialize(request, JsonOptions));
        await db.BackgroundJobs.AddAsync(row, ct);
        AddAudit(user.UserId, "BackgroundJob", row.BackgroundJobId.ToString(), "MileageJobEnqueued", new { request.Mode, user.TeamIds });
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<BackgroundJobDto> EnqueueGeocodingAsync(CurrentUserDto user, CreateGeocodingJobRequest request, CancellationToken ct)
    {
        if (!HasRole(user, "leader") && !HasRole(user, "admin")) throw new UnauthorizedAccessException("目前角色無權建立地點解析工作。");
        var mode = request.Mode?.Trim() ?? "Selected";
        if (!(mode.Equals("AllPending", StringComparison.OrdinalIgnoreCase)
              || mode.Equals("DateRange", StringComparison.OrdinalIgnoreCase)
              || mode.Equals("Selected", StringComparison.OrdinalIgnoreCase)
              || mode.Equals("Filtered", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("地點解析 Mode 只支援 AllPending、DateRange、Selected 或 Filtered。");
        if (mode.Equals("DateRange", StringComparison.OrdinalIgnoreCase) && (request.StartDate is null || request.EndDate is null || request.EndDate < request.StartDate))
            throw new InvalidOperationException("日期區間不正確。");
        if (mode.Equals("Selected", StringComparison.OrdinalIgnoreCase) && (request.LocationIds is null || request.LocationIds.Count == 0))
            throw new InvalidOperationException("請先選擇地點。");
        var normalized = request with { Mode = mode };
        var row = NewJob("Geocoding", mode, user, JsonSerializer.Serialize(normalized, JsonOptions));

        // Geocoding scope must match the formal-location access rules:
        // - admin: organization-wide (no TeamId restriction)
        // - leader: authorized teams + organization-wide locations (TeamId = null)
        if (HasRole(user, "admin"))
            row.TeamScopeJson = JsonSerializer.Serialize(Array.Empty<int>(), JsonOptions);

        await db.BackgroundJobs.AddAsync(row, ct);
        AddAudit(
            user.UserId,
            "BackgroundJob",
            row.BackgroundJobId.ToString(),
            "GeocodingJobEnqueued",
            new
            {
                Mode = mode,
                Count = request.LocationIds?.Count ?? 0,
                request.StartDate,
                request.EndDate,
                Scope = HasRole(user, "admin") ? "Organization" : "TeamsAndGlobal",
                TeamIds = HasRole(user, "admin") ? Array.Empty<int>() : user.TeamIds
            });
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<BackgroundJobDto> GetAsync(CurrentUserDto user, Guid jobId, CancellationToken ct)
    {
        var row = await db.BackgroundJobs.AsNoTracking().FirstOrDefaultAsync(x => x.BackgroundJobId == jobId, ct) ?? throw new KeyNotFoundException("找不到背景工作。");
        var privileged = HasRole(user, "admin") || HasRole(user, "supervisor");
        if (!privileged && row.RequestedByUserId != user.UserId) throw new UnauthorizedAccessException("只能查看自己建立的背景工作。");
        if (privileged && user.OrganizationId.HasValue && row.OrganizationId != user.OrganizationId) throw new UnauthorizedAccessException("無權查看其他 Organization 工作。");
        return Map(row);
    }

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var job = await db.BackgroundJobs.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(x => x.Status == "Waiting", ct);
        if (job is null) return false;
        job.Status = "Processing"; job.StartedAt = DateTime.UtcNow; job.ErrorMessage = null;
        await db.SaveChangesAsync(ct);
        try
        {
            if (job.JobType == "Mileage") await ProcessMileageAsync(job, ct);
            else if (job.JobType == "Geocoding") await ProcessGeocodingAsync(job, ct);
            else throw new InvalidOperationException($"未知 JobType：{job.JobType}");
            job.Status = job.FailedCount > 0 && job.SuccessCount > 0 ? "PartiallySucceeded" : job.FailedCount > 0 ? "Failed" : "Succeeded";
        }
        catch (Exception ex)
        {
            job.Status = "Failed"; job.ErrorMessage = ex.Message;
        }
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task ProcessMileageAsync(BackgroundJob job, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<MileageBatchRequest>(job.PayloadJson ?? "{}", JsonOptions) ?? new MileageBatchRequest("AllPending", null, null, null);
        var teamIds = ParseTeamIds(job.TeamScopeJson);
        var q = db.VisitTrips.Include(x => x.Stops).Include(x => x.MileageCalculation).Where(x => x.TeamId.HasValue && teamIds.Contains(x.TeamId.Value) && (x.Status == TripStatuses.Submitted || x.Status == TripStatuses.RoutePending) && x.Stops.Count >= 2 && (x.MileageCalculation == null || x.MileageCalculation.SystemDistanceKm == null));
        if (request.Mode.Equals("DateRange", StringComparison.OrdinalIgnoreCase))
        {
            if (request.StartDate.HasValue) q = q.Where(x => x.VisitDate >= request.StartDate.Value);
            if (request.EndDate.HasValue) q = q.Where(x => x.VisitDate <= request.EndDate.Value);
        }
        if (request.Mode.Equals("Selected", StringComparison.OrdinalIgnoreCase) && request.SelectedTripIds is { Count: > 0 }) q = q.Where(x => request.SelectedTripIds.Contains(x.VisitTripId));
        var rows = await q.OrderBy(x => x.VisitDate).ThenBy(x => x.VisitTripId).ToListAsync(ct);
        job.TotalCount = rows.Count;
        foreach (var trip in rows)
        {
            var item = new BackgroundJobItem { BackgroundJobId = job.BackgroundJobId, EntityType = "VisitTrip", EntityId = trip.VisitTripId.ToString(), Status = "Processing", CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow };
            await db.BackgroundJobItems.AddAsync(item, ct);
            try
            {
                var result = await route.CalculateAsync(trip, ct);
                if (!result.Success || !result.DistanceKm.HasValue) throw new InvalidOperationException(result.ErrorMessage ?? result.ErrorCode ?? "里程計算失敗。");
                var calc = trip.MileageCalculation;
                if (calc is null)
                {
                    calc = new MileageCalculation { VisitTripId = trip.VisitTripId, CreatedAt = DateTime.UtcNow };
                    await db.MileageCalculations.AddAsync(calc, ct);
                }
                var previous = trip.Status;
                trip.Status = TripStatuses.PendingApproval; trip.UpdatedAt = DateTime.UtcNow; trip.UpdatedByUserId = job.RequestedByUserId;
                calc.SystemDistanceKm = result.DistanceKm; calc.ApprovedDistanceKm = result.DistanceKm; calc.CalculationSource = "MockRoute/UAT"; calc.CalculatedAt = DateTime.UtcNow; calc.UpdatedAt = DateTime.UtcNow;
                db.VisitTripStatusHistories.Add(new VisitTripStatusHistory { VisitTripId = trip.VisitTripId, PreviousStatus = previous, NewStatus = TripStatuses.PendingApproval, Action = "MileageCalculatedJob", ActionByUserId = job.RequestedByUserId, Comments = $"SystemDistanceKm={result.DistanceKm:0.00}", ActionAt = DateTime.UtcNow });
                item.Status = "Succeeded"; item.ResultJson = JsonSerializer.Serialize(new { result.DistanceKm }, JsonOptions); job.SuccessCount++;
            }
            catch (Exception ex)
            {
                item.Status = "Failed"; item.ErrorCode = "MILEAGE_JOB_FAILED"; item.ErrorMessage = ex.Message; job.FailedCount++;
            }
            item.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task ProcessGeocodingAsync(BackgroundJob job, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<CreateGeocodingJobRequest>(job.PayloadJson ?? "{}", JsonOptions) ?? new CreateGeocodingJobRequest();
        var teamIds = ParseTeamIds(job.TeamScopeJson);
        var q = db.Locations.Where(x => (x.OrganizationId == job.OrganizationId || x.OrganizationId == null) &&
            (x.ApprovalStatus == "Pending" || x.GeocodingStatus == "Pending" || x.GeocodingStatus == "Failed"));
        if (teamIds.Count > 0)
            q = q.Where(
                x => x.TeamId == null
                     || (x.TeamId.HasValue
                         && teamIds.Contains(x.TeamId.Value)));
        if (request.Mode.Equals("Selected", StringComparison.OrdinalIgnoreCase) && request.LocationIds is { Count: > 0 })
            q = q.Where(x => request.LocationIds.Contains(x.LocationId));

        if (request.Mode.Equals("Filtered", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(request.Q))
            {
                var keyword = request.Q.Trim();
                q = q.Where(x =>
                    x.LocationCode != null && x.LocationCode.Contains(keyword)
                    || x.LocationName.Contains(keyword)
                    || (x.Address != null && x.Address.Contains(keyword))
                    || (x.PlusCode != null && x.PlusCode.Contains(keyword)));
            }
            if (request.TeamId.HasValue) q = q.Where(x => x.TeamId == request.TeamId.Value);
            if (!string.IsNullOrWhiteSpace(request.City))
            {
                var city = request.City.Trim();
                q = q.Where(x => x.City == city);
            }
            if (!string.IsNullOrWhiteSpace(request.District))
            {
                var district = request.District.Trim();
                q = q.Where(x => x.District == district);
            }
            if (!string.IsNullOrWhiteSpace(request.GeocodingStatus)
                && !request.GeocodingStatus.Equals("NeedsProcessing", StringComparison.OrdinalIgnoreCase))
            {
                var status = request.GeocodingStatus.Trim();
                q = q.Where(x => x.GeocodingStatus == status);
            }
            if (request.IsActive.HasValue) q = q.Where(x => x.IsActive == request.IsActive.Value);
        }

        if (request.Mode.Equals("DateRange", StringComparison.OrdinalIgnoreCase))
        {
            if (request.StartDate.HasValue)
            {
                var fromUtc = BusinessTime.ToUtc(request.StartDate.Value, TimeOnly.MinValue);
                q = q.Where(x => x.CreatedAt >= fromUtc);
            }
            if (request.EndDate.HasValue)
            {
                var toUtcExclusive = BusinessTime.ToUtc(request.EndDate.Value.AddDays(1), TimeOnly.MinValue);
                q = q.Where(x => x.CreatedAt < toUtcExclusive);
            }
        }
        var rows = await q.ToListAsync(ct);
        job.TotalCount = rows.Count;
        foreach (var location in rows)
        {
            var item = new BackgroundJobItem { BackgroundJobId = job.BackgroundJobId, EntityType = "Location", EntityId = location.LocationId.ToString(), Status = "Processing", CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow };
            await db.BackgroundJobItems.AddAsync(item, ct);
            try
            {
                var result = await geocoding.ResolveAsync(location.Address, location.PlusCode, ct);
                if (!result.Success || !result.Latitude.HasValue || !result.Longitude.HasValue) throw new InvalidOperationException(result.ErrorMessage ?? result.ErrorCode ?? "地址解析失敗。");
                location.LocationCode ??= NewLocationCode(); location.Latitude = result.Latitude; location.Longitude = result.Longitude; location.GeocodingStatus = "Completed"; location.GeocodedAt = DateTime.UtcNow; location.ApprovalStatus = "Approved"; location.IsActive = true; location.UpdatedAt = DateTime.UtcNow;
                db.LocationApprovalHistories.Add(new LocationApprovalHistory { LocationId = location.LocationId, Action = "Approved", ReviewedByUserId = job.RequestedByUserId, Comments = "Background geocoding/publish", ActionAt = DateTime.UtcNow });
                item.Status = "Succeeded"; item.ResultJson = JsonSerializer.Serialize(new { result.Latitude, result.Longitude }, JsonOptions); job.SuccessCount++;
            }
            catch (Exception ex)
            {
                location.GeocodingStatus = "Failed"; location.IsActive = false; item.Status = "Failed"; item.ErrorCode = "GEOCODING_JOB_FAILED"; item.ErrorMessage = ex.Message; job.FailedCount++;
            }
            item.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    private BackgroundJob NewJob(string type, string mode, CurrentUserDto user, string payload) => new()
    {
        BackgroundJobId = Guid.NewGuid(), JobType = type, Status = "Waiting", Mode = mode, OrganizationId = user.OrganizationId,
        TeamScopeJson = JsonSerializer.Serialize(user.TeamIds, JsonOptions), RequestedByUserId = user.UserId, PayloadJson = payload, CreatedAt = DateTime.UtcNow
    };

    private static List<int> ParseTeamIds(string? json) => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<int>>(json, JsonOptions) ?? [];
    private static bool HasRole(CurrentUserDto user, string role) => user.Roles.Any(x => x.Equals(role, StringComparison.OrdinalIgnoreCase));
    private static BackgroundJobDto Map(BackgroundJob x) => new(x.BackgroundJobId, x.JobType, x.Status, x.Mode, x.TotalCount, x.SuccessCount, x.FailedCount, x.SkippedCount, x.ErrorMessage, x.CreatedAt, x.StartedAt, x.CompletedAt);
    private void AddAudit(int userId, string entityType, string? entityId, string action, object value) => db.AuditLogs.Add(new AuditLog { UserId = userId, EntityType = entityType, EntityId = entityId, Action = action, NewValues = JsonSerializer.Serialize(value, JsonOptions), CorrelationId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow });
    private static string NewLocationCode() => $"LOC-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

}
