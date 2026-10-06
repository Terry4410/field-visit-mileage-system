using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FieldVisit.Infrastructure;

public sealed class BackgroundJobService(
    AppDbContext db,
    IRouteCalculationService route,
    IGeocodingService geocoding,
    V180GoogleMileageOrchestrationService googleMileage,
    IConfiguration configuration) : IBackgroundJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly bool useGoogleRoutes =
        (configuration["Providers:Route"] ?? "Mock").Equals("Google", StringComparison.OrdinalIgnoreCase);

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
        var job = await db.BackgroundJobs
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(x => x.Status == "Waiting", ct);
        if (job is null) return false;

        var jobId = job.BackgroundJobId;
        var jobType = job.JobType;
        job.Status = "Processing";
        job.StartedAt = DateTime.UtcNow;
        job.ErrorMessage = null;
        await db.SaveChangesAsync(ct);

        try
        {
            if (jobType == "Mileage")
            {
                await ProcessMileageAsync(jobId, ct);
            }
            else if (jobType == "Geocoding")
            {
                db.ChangeTracker.Clear();
                var geocodingJob = await db.BackgroundJobs
                    .SingleAsync(x => x.BackgroundJobId == jobId, ct);
                await ProcessGeocodingAsync(geocodingJob, ct);
            }
            else
            {
                throw new InvalidOperationException($"未知 JobType：{jobType}");
            }

            db.ChangeTracker.Clear();
            var terminalJob = await db.BackgroundJobs
                .SingleAsync(x => x.BackgroundJobId == jobId, ct);
            terminalJob.Status = terminalJob.FailedCount > 0 && terminalJob.SuccessCount > 0
                ? "PartiallySucceeded"
                : terminalJob.FailedCount > 0
                    ? "Failed"
                    : "Succeeded";
            terminalJob.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            var failedJob = await db.BackgroundJobs
                .SingleAsync(x => x.BackgroundJobId == jobId, ct);
            failedJob.Status = "Failed";
            failedJob.ErrorMessage = ex.Message;
            failedJob.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    private async Task ProcessMileageAsync(Guid jobId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var jobState = await db.BackgroundJobs.AsNoTracking()
            .Where(x => x.BackgroundJobId == jobId)
            .Select(x => new { x.BackgroundJobId, x.PayloadJson, x.TeamScopeJson, x.RequestedByUserId })
            .SingleAsync(ct);

        var request = JsonSerializer.Deserialize<MileageBatchRequest>(
            jobState.PayloadJson ?? "{}", JsonOptions)
            ?? new MileageBatchRequest("AllPending", null, null, null);
        var teamIds = ParseTeamIds(jobState.TeamScopeJson);

        var q = db.VisitTrips.AsNoTracking()
            .Where(x => x.TeamId.HasValue
                && teamIds.Contains(x.TeamId.Value)
                && (x.Status == TripStatuses.Submitted || x.Status == TripStatuses.RoutePending)
                && x.Stops.Count >= V170TripMileageRules.MinimumVisitStopCount
                && (x.MileageCalculation == null || x.MileageCalculation.SystemDistanceKm == null));

        if (request.Mode.Equals("DateRange", StringComparison.OrdinalIgnoreCase))
        {
            if (request.StartDate.HasValue) q = q.Where(x => x.VisitDate >= request.StartDate.Value);
            if (request.EndDate.HasValue) q = q.Where(x => x.VisitDate <= request.EndDate.Value);
        }
        if (request.Mode.Equals("Selected", StringComparison.OrdinalIgnoreCase)
            && request.SelectedTripIds is { Count: > 0 })
            q = q.Where(x => request.SelectedTripIds.Contains(x.VisitTripId));

        var tripIds = await q.OrderBy(x => x.VisitDate)
            .ThenBy(x => x.VisitTripId)
            .Select(x => x.VisitTripId)
            .ToListAsync(ct);

        await db.BackgroundJobs
            .Where(x => x.BackgroundJobId == jobId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TotalCount, tripIds.Count), ct);

        foreach (var tripId in tripIds)
        {
            db.ChangeTracker.Clear();
            var item = new BackgroundJobItem
            {
                BackgroundJobId = jobId,
                EntityType = "VisitTrip",
                EntityId = tripId.ToString(),
                Status = "Processing",
                CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow
            };
            await db.BackgroundJobItems.AddAsync(item, ct);
            await db.SaveChangesAsync(ct);
            var itemId = item.BackgroundJobItemId;

            try
            {
                V180RouteOrchestrationResult? googleResult = null;
                RouteCalculationResult? mockResult = null;

                if (useGoogleRoutes)
                    googleResult = await googleMileage.CalculateSubmittedRouteForBackgroundAsync(
                        tripId, jobState.RequestedByUserId, ct);
                else
                {
                    var routeTrip = await db.VisitTrips
                        .Include(x => x.Stops)
                        .Include(x => x.MileageCalculation)
                        .SingleAsync(x => x.VisitTripId == tripId, ct);
                    mockResult = await route.CalculateAsync(routeTrip, ct);
                }

                db.ChangeTracker.Clear();
                var trip = await db.VisitTrips
                    .Include(x => x.Stops)
                    .Include(x => x.MileageCalculation)
                    .SingleAsync(x => x.VisitTripId == tripId, ct);
                var durableItem = await db.BackgroundJobItems
                    .SingleAsync(x => x.BackgroundJobItemId == itemId, ct);
                var durableJob = await db.BackgroundJobs
                    .SingleAsync(x => x.BackgroundJobId == jobId, ct);

                var calc = trip.MileageCalculation;
                if (calc is null)
                {
                    calc = new MileageCalculation { VisitTripId = trip.VisitTripId, CreatedAt = DateTime.UtcNow };
                    await db.MileageCalculations.AddAsync(calc, ct);
                }

                var previous = trip.Status;
                var completedAt = DateTime.UtcNow;

                if (useGoogleRoutes && googleResult is { Status: "Succeeded", SuggestedDistanceKm: > 0 })
                {
                    trip.Status = TripStatuses.PendingApproval;
                    trip.ReturnReason = null;
                    trip.UpdatedAt = completedAt;
                    trip.UpdatedByUserId = jobState.RequestedByUserId;
                    calc.SystemDistanceKm = googleResult.SuggestedDistanceKm;
                    calc.CalculationSource = "GoogleMapsAPI";
                    calc.CalculatedAt = completedAt;
                    calc.UpdatedAt = completedAt;

                    db.VisitTripStatusHistories.Add(new VisitTripStatusHistory
                    {
                        VisitTripId = trip.VisitTripId,
                        PreviousStatus = previous,
                        NewStatus = TripStatuses.PendingApproval,
                        Action = "GoogleMapsMileageCalculated",
                        ActionByUserId = jobState.RequestedByUserId,
                        Comments = $"GoogleMapsAPI={googleResult.SuggestedDistanceKm:0.###} km; RouteAttempt={googleResult.RouteCalculationAttemptId}",
                        ActionAt = completedAt
                    });

                    durableItem.Status = "Succeeded";
                    durableItem.ResultJson = JsonSerializer.Serialize(new
                    {
                        source = "GoogleMapsAPI",
                        distanceKm = googleResult.SuggestedDistanceKm,
                        routeCalculationAttemptId = googleResult.RouteCalculationAttemptId
                    }, JsonOptions);
                    durableJob.SuccessCount++;
                }
                else if (useGoogleRoutes && calc.ClaimedDistanceKm is > 0)
                {
                    trip.Status = TripStatuses.PendingApproval;
                    trip.ReturnReason = null;
                    trip.UpdatedAt = completedAt;
                    trip.UpdatedByUserId = jobState.RequestedByUserId;
                    calc.SystemDistanceKm = null;
                    calc.CalculationSource = "ManualFallback";
                    calc.CalculatedAt = completedAt;
                    calc.UpdatedAt = completedAt;

                    db.VisitTripStatusHistories.Add(new VisitTripStatusHistory
                    {
                        VisitTripId = trip.VisitTripId,
                        PreviousStatus = previous,
                        NewStatus = TripStatuses.PendingApproval,
                        Action = "GoogleMapsFailedManualFallback",
                        ActionByUserId = jobState.RequestedByUserId,
                        Comments = $"Google Maps API 無法取得可用里程；使用外訪員人工備援 {calc.ClaimedDistanceKm:0.###} km 等待小組長核准。",
                        ActionAt = completedAt
                    });

                    durableItem.Status = "Succeeded";
                    durableItem.ResultJson = JsonSerializer.Serialize(new
                    {
                        source = "ManualFallback",
                        distanceKm = calc.ClaimedDistanceKm,
                        googleErrorCode = googleResult?.ErrorCode
                    }, JsonOptions);
                    durableJob.SuccessCount++;
                }
                else if (useGoogleRoutes)
                {
                    trip.Status = TripStatuses.Returned;
                    trip.ReturnReason = "Google Maps API 無法取得可用里程，請填寫人工備援里程後重新送出。";
                    trip.UpdatedAt = completedAt;
                    trip.UpdatedByUserId = jobState.RequestedByUserId;
                    calc.SystemDistanceKm = null;
                    calc.CalculationSource = null;
                    calc.CalculatedAt = null;
                    calc.UpdatedAt = completedAt;

                    db.VisitTripStatusHistories.Add(new VisitTripStatusHistory
                    {
                        VisitTripId = trip.VisitTripId,
                        PreviousStatus = previous,
                        NewStatus = TripStatuses.Returned,
                        Action = "GoogleMapsFailedReturnForManualFallback",
                        ActionByUserId = jobState.RequestedByUserId,
                        Comments = trip.ReturnReason,
                        ActionAt = completedAt
                    });

                    durableItem.Status = "Failed";
                    durableItem.ErrorCode = googleResult?.ErrorCode ?? "GOOGLE_ROUTE_FAILED";
                    durableItem.ErrorMessage = googleResult?.ErrorMessage ?? trip.ReturnReason;
                    durableJob.FailedCount++;
                }
                else
                {
                    if (mockResult is not { Success: true, DistanceKm: > 0 })
                        throw new InvalidOperationException(
                            mockResult?.ErrorMessage ?? mockResult?.ErrorCode ?? "里程計算失敗。");

                    trip.Status = TripStatuses.PendingApproval;
                    trip.UpdatedAt = completedAt;
                    trip.UpdatedByUserId = jobState.RequestedByUserId;
                    calc.SystemDistanceKm = mockResult.DistanceKm;
                    calc.CalculationSource = "MockRoute/UAT";
                    calc.CalculatedAt = completedAt;
                    calc.UpdatedAt = completedAt;

                    db.VisitTripStatusHistories.Add(new VisitTripStatusHistory
                    {
                        VisitTripId = trip.VisitTripId,
                        PreviousStatus = previous,
                        NewStatus = TripStatuses.PendingApproval,
                        Action = "MileageCalculatedJob",
                        ActionByUserId = jobState.RequestedByUserId,
                        Comments = $"SystemDistanceKm={mockResult.DistanceKm:0.00}",
                        ActionAt = completedAt
                    });

                    var submittedSnapshotId = await db.VisitTripSnapshots.AsNoTracking()
                        .Where(x => x.VisitTripId == tripId && x.SnapshotType == "Submitted")
                        .OrderByDescending(x => x.SnapshotVersion)
                        .Select(x => (long?)x.VisitTripSnapshotId)
                        .FirstOrDefaultAsync(ct);
                    db.MileageGovernanceEvents.Add(new MileageGovernanceEvent
                    {
                        VisitTripId = tripId,
                        VisitTripSnapshotId = submittedSnapshotId,
                        EventType = "Calculated",
                        ReasonCode = "MockRoute/UAT",
                        Message = "Mock route distance calculated; no company approval evidence created.",
                        CorrelationId = Guid.NewGuid(),
                        OccurredAt = completedAt,
                        ActorUserId = jobState.RequestedByUserId
                    });

                    durableItem.Status = "Succeeded";
                    durableItem.ResultJson = JsonSerializer.Serialize(new { source = "MockRoute/UAT", distanceKm = mockResult.DistanceKm }, JsonOptions);
                    durableJob.SuccessCount++;
                }

                durableItem.CompletedAt = completedAt;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                var errorMessage = ex.Message;
                db.ChangeTracker.Clear();
                var failedItem = await db.BackgroundJobItems.SingleAsync(x => x.BackgroundJobItemId == itemId, ct);
                var durableJob = await db.BackgroundJobs.SingleAsync(x => x.BackgroundJobId == jobId, ct);
                failedItem.Status = "Failed";
                failedItem.ErrorCode = "MILEAGE_JOB_FAILED";
                failedItem.ErrorMessage = errorMessage;
                failedItem.CompletedAt = DateTime.UtcNow;
                durableJob.FailedCount++;

                var failedTripSnapshotId = await db.VisitTripSnapshots.AsNoTracking()
                    .Where(x => x.VisitTripId == tripId && x.SnapshotType == "Submitted")
                    .OrderByDescending(x => x.SnapshotVersion)
                    .Select(x => (long?)x.VisitTripSnapshotId)
                    .FirstOrDefaultAsync(ct);
                db.MileageGovernanceEvents.Add(new MileageGovernanceEvent
                {
                    VisitTripId = tripId,
                    VisitTripSnapshotId = failedTripSnapshotId,
                    EventType = "CalculationFailed",
                    ReasonCode = "MILEAGE_JOB_FAILED",
                    Message = errorMessage.Length <= 1000 ? errorMessage : errorMessage[..1000],
                    CorrelationId = Guid.NewGuid(),
                    OccurredAt = DateTime.UtcNow,
                    ActorUserId = durableJob.RequestedByUserId
                });
                await db.SaveChangesAsync(ct);
            }
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
            if (location.DuplicateOfLocationId.HasValue
                || string.Equals(location.DuplicateReason, V170LocationDuplicateRules.SuspectedReason, StringComparison.Ordinal))
            {
                item.Status = "Skipped";
                item.ErrorCode = "DUPLICATE_REVIEW_REQUIRED";
                item.ErrorMessage = "疑似重複地點必須先完成管理者人工覆核。";
                item.CompletedAt = DateTime.UtcNow;
                job.SkippedCount++;
                await db.SaveChangesAsync(ct);
                continue;
            }

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
