using FieldVisit.Domain;
using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public sealed class TripService(
    ICurrentUserService current,
    IUserRepository users,
    ITripRepository trips,
    IMasterRepository masters,
    IMileageRepository mileage,
    IWorkflowRepository workflow,
    IUnitOfWork uow)
{
    public async Task<TripDto> CreateAsync(SaveTripRequest request, CancellationToken ct)
    {
        var user = RequireRole("visitor");
        ValidateRequest(request);

        var overlap = await CheckOverlapAsync(
            new TimeOverlapRequest(request.VisitDate, request.StartTime, request.EndTime, null), ct);
var now = DateTime.UtcNow;
        var trip = new VisitTrip
        {
            TripNo = BuildTripNo(request.VisitDate),
            UserId = user.UserId,
            OrganizationId = user.OrganizationId ?? throw new InvalidOperationException("使用者未設定 Organization。"),
            TeamId = user.TeamId,
            VisitDate = request.VisitDate,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            HasTimeOverlapWarning = overlap.HasOverlap,
            TimeOverlapConfirmed = overlap.HasOverlap && request.TimeOverlapConfirmed,
            Status = TripStatuses.Draft,
            VehicleType = "Motorcycle",
            Purpose = request.Purpose,
            Notes = request.Notes,
            CreatedAt = now,
            CreatedByUserId = user.UserId,
            UpdatedAt = now,
            UpdatedByUserId = user.UserId
        };

        await BuildStopsAsync(trip, request.Stops, user, ct);
        await trips.AddAsync(trip, ct);
        await uow.SaveChangesAsync(ct);

        await mileage.AddAsync(new MileageCalculation
        {
            VisitTripId = trip.VisitTripId,
            ClaimedDistanceKm = request.ClaimedDistanceKm,
            CreatedAt = now,
            UpdatedAt = now
        }, ct);

        await AddHistoryAsync(trip, null, TripStatuses.Draft, "CreateDraft", user.UserId, null, ct);
        await AuditAsync(user.UserId, "Trip", trip.VisitTripId.ToString(), "TripCreateDraft", null, new { trip.TripNo }, ct);
        await uow.SaveChangesAsync(ct);

        return await GetDtoAsync(trip.VisitTripId, ct);
    }

    public async Task<TripDto> UpdateAsync(long tripId, SaveTripRequest request, string rowVersion, CancellationToken ct)
    {
        var user = RequireRole("visitor");
        ValidateRequest(request);

        var trip = await trips.GetAsync(tripId, true, ct)
            ?? throw new KeyNotFoundException("找不到行程。");

        EnsureVisitorOwns(user, trip);
        if (trip.Status is not (TripStatuses.Draft or TripStatuses.Returned))
            throw new InvalidOperationException("只有草稿或已退回行程可以修改。");

        EnsureRowVersion(trip.RowVersion, rowVersion);

        var overlap = await CheckOverlapAsync(
            new TimeOverlapRequest(request.VisitDate, request.StartTime, request.EndTime, tripId), ct);
trip.VisitDate = request.VisitDate;
        trip.StartTime = request.StartTime;
        trip.EndTime = request.EndTime;
        trip.HasTimeOverlapWarning = overlap.HasOverlap;
        trip.TimeOverlapConfirmed = overlap.HasOverlap && request.TimeOverlapConfirmed;
        trip.Purpose = request.Purpose;
        trip.Notes = request.Notes;
        trip.ReturnReason = null;
        trip.UpdatedAt = DateTime.UtcNow;
        trip.UpdatedByUserId = user.UserId;

        trip.Stops.Clear();
        await BuildStopsAsync(trip, request.Stops, user, ct);

        var calc = await mileage.GetByTripAsync(trip.VisitTripId, true, ct);
        if (calc is null)
        {
            calc = new MileageCalculation { VisitTripId = trip.VisitTripId, CreatedAt = DateTime.UtcNow };
            await mileage.AddAsync(calc, ct);
        }
        calc.ClaimedDistanceKm = request.ClaimedDistanceKm;
        calc.SystemDistanceKm = null;
        calc.ApprovedDistanceKm = null;
        calc.MileageRateRuleId = null;
        calc.RatePerKmSnapshot = null;
        calc.ClaimedAmount = null;
        calc.ApprovedAmount = null;
        calc.CalculationSource = null;
        calc.CalculatedAt = null;
        calc.UpdatedAt = DateTime.UtcNow;

        await AddHistoryAsync(trip, trip.Status, trip.Status, "Update", user.UserId, null, ct);
        await AuditAsync(user.UserId, "Trip", trip.VisitTripId.ToString(), "TripUpdate", null, new { request.VisitDate, request.StartTime, request.EndTime }, ct);
        await uow.SaveChangesAsync(ct);
        return await GetDtoAsync(tripId, ct);
    }

    public async Task<TripDto> SubmitAsync(long tripId, SubmitTripRequest request, string rowVersion, CancellationToken ct)
    {
        var user = RequireRole("visitor");
        var trip = await trips.GetAsync(tripId, true, ct)
            ?? throw new KeyNotFoundException("找不到行程。");

        EnsureVisitorOwns(user, trip);
        if (trip.Status is not (TripStatuses.Draft or TripStatuses.Returned))
            throw new InvalidOperationException("此狀態不能送出。");
        EnsureRowVersion(trip.RowVersion, rowVersion);

        if (trip.StartTime is null || trip.EndTime is null || trip.Stops.Count < 2)
            throw new InvalidOperationException("送出前必須填寫起訖時間，且至少兩個公務地點。");

        var calc = await mileage.GetByTripAsync(trip.VisitTripId, true, ct);
        if (calc?.ClaimedDistanceKm is null or <= 0)
            throw new InvalidOperationException("送出前必須填寫外訪員自算里程。");

        var overlap = await CheckOverlapAsync(new TimeOverlapRequest(
            trip.VisitDate, trip.StartTime.Value, trip.EndTime.Value, trip.VisitTripId), ct);
        if (overlap.HasOverlap && !request.ConfirmTimeOverlap)
            throw new InvalidOperationException("TIME_OVERLAP_WARNING：請確認時間重疊後再送出。");

        var previous = trip.Status;
        trip.Status = TripStatuses.Submitted;
        trip.HasTimeOverlapWarning = overlap.HasOverlap;
        trip.TimeOverlapConfirmed = overlap.HasOverlap && request.ConfirmTimeOverlap;
        trip.SubmittedAt = DateTime.UtcNow;
        trip.ReturnReason = null;
        trip.UpdatedAt = DateTime.UtcNow;
        trip.UpdatedByUserId = user.UserId;

        await AddHistoryAsync(trip, previous, TripStatuses.Submitted, previous == TripStatuses.Returned ? "Resubmit" : "Submit", user.UserId,
            overlap.HasOverlap ? "使用者已確認時間重疊" : null, ct);
        await AuditAsync(user.UserId, "Trip", trip.VisitTripId.ToString(), "TripSubmit", null, new { trip.TripNo }, ct);
        await uow.SaveChangesAsync(ct);
        return await GetDtoAsync(tripId, ct);
    }

    public async Task<TimeOverlapResult> CheckOverlapAsync(TimeOverlapRequest request, CancellationToken ct)
    {
        var user = RequireRole("visitor");
        if (request.EndTime <= request.StartTime)
            throw new InvalidOperationException("結束時間必須晚於出發時間。");

        var rows = await trips.FindOverlapsAsync(user.UserId, request.VisitDate, request.StartTime, request.EndTime, request.ExcludeVisitTripId, ct);
        return new TimeOverlapResult(
            rows.Count > 0,
            rows.Count > 0 ? "TIME_OVERLAP_WARNING" : null,
            rows.Count > 0 ? "此時間與既有行程重疊，請確認時間是否正確。" : null,
            rows.Select(x => new TimeOverlapItem(x.VisitTripId, x.TripNo, x.StartTime, x.EndTime, TripStatuses.Display(x.Status))).ToList());
    }

    public async Task<List<TripDto>> HistoryAsync(DateOnly? start, DateOnly? end, string? locationKeyword, CancellationToken ct)
    {
        var user = RequireRole("visitor");
        var rows = await trips.GetVisitorHistoryAsync(user.UserId, start, end, locationKeyword, ct);
        var result = new List<TripDto>();
        foreach (var row in rows) result.Add(await MapAsync(row, ct));
        return result;
    }

    public async Task<TripDto> GetDtoAsync(long tripId, CancellationToken ct)
    {
        var user = current.GetRequired();
        var trip = await trips.GetAsync(tripId, false, ct) ?? throw new KeyNotFoundException("找不到行程。");
        if (HasRole(user, "visitor") && trip.UserId != user.UserId)
            throw new UnauthorizedAccessException("無權查看其他外訪員行程。");
        if (HasRole(user, "leader") && trip.TeamId != user.TeamId)
            throw new UnauthorizedAccessException("無權查看其他小組行程。");
        return await MapAsync(trip, ct);
    }

    public async Task<TripDto> MapAsync(VisitTrip trip, CancellationToken ct)
    {
        var profile = await users.GetProfileAsync(trip.UserId, ct);
        var calc = trip.MileageCalculation ?? await mileage.GetByTripAsync(trip.VisitTripId, false, ct);

        return new TripDto(
            trip.VisitTripId,
            trip.TripNo,
            trip.UserId,
            profile?.DisplayName ?? $"User {trip.UserId}",
            trip.TeamId,
            profile?.TeamName,
            trip.VisitDate,
            trip.StartTime,
            trip.EndTime,
            trip.Status,
            TripStatuses.Display(trip.Status),
            trip.Purpose,
            trip.Notes,
            trip.ReturnReason,
            calc?.ClaimedDistanceKm,
            calc?.SystemDistanceKm,
            calc?.ApprovedDistanceKm,
            calc?.RatePerKmSnapshot,
            calc?.ApprovedAmount,
            trip.Stops.OrderBy(x => x.StopSequence).Select(x => new TripStopInput(
                x.LocationId, x.ProjectId, x.VisitTypeId,
                x.LocationId.HasValue ? "Master" : "Temporary",
                x.LocationNameSnapshot ?? "", x.AddressSnapshot, x.VisitPurpose, x.Notes)).ToList(),
            Convert.ToBase64String(trip.RowVersion ?? []));
    }

    private async Task BuildStopsAsync(VisitTrip trip, IReadOnlyList<TripStopInput> inputs, CurrentUserDto user, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var seq = 1;
        foreach (var input in inputs)
        {
            int? locationId = input.LocationId;
            Location? pendingLocation = null;
            if (!locationId.HasValue && input.SourceType.Equals("Temporary", StringComparison.OrdinalIgnoreCase))
            {
                pendingLocation = new Location
                {
                    OrganizationId = user.OrganizationId, TeamId = user.TeamId, LocationName = input.LocationName.Trim(),
                    LocationType = input.ProjectId.HasValue ? "Project" : "Official", Address = input.Address, IsTemporary = true,
                    ApprovalStatus = "Pending", GeocodingStatus = "Pending", CreatedByUserId = user.UserId, IsActive = false, CreatedAt = now
                };
                await masters.AddLocationAsync(pendingLocation, ct);
            }

            trip.Stops.Add(new VisitTripStop
            {
                StopSequence = seq++,
                LocationId = locationId,
                ProjectId = input.ProjectId,
                VisitTypeId = input.VisitTypeId,
                LocationNameSnapshot = input.LocationName.Trim(),
                AddressSnapshot = input.Address,
                VisitPurpose = input.VisitPurpose,
                Notes = input.Notes,
                CreatedAt = now,
                Location = pendingLocation
            });
        }
    }

    private CurrentUserDto RequireRole(string role)
    {
        var user = current.GetRequired();
        if (!HasRole(user, role)) throw new UnauthorizedAccessException("目前角色無權執行此操作。");
        return user;
    }

    private static bool HasRole(CurrentUserDto user, string role) =>
        user.Roles.Any(x => x.Equals(role, StringComparison.OrdinalIgnoreCase));

    private static void EnsureVisitorOwns(CurrentUserDto user, VisitTrip trip)
    {
        if (trip.UserId != user.UserId) throw new UnauthorizedAccessException("只能處理自己的行程。");
    }

    private static void ValidateRequest(SaveTripRequest request)
    {
        if (request.EndTime <= request.StartTime) throw new InvalidOperationException("結束時間必須晚於出發時間。");
        if (request.ClaimedDistanceKm < 0) throw new InvalidOperationException("自算里程不可小於 0。");
        if (request.Stops.Any(x => string.IsNullOrWhiteSpace(x.LocationName))) throw new InvalidOperationException("地點名稱不可空白。");
    }

    private static void EnsureRowVersion(byte[] currentValue, string expectedBase64)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(expectedBase64); }
        catch { throw new InvalidOperationException("RowVersion 格式不正確。"); }
        if (!currentValue.SequenceEqual(expected))
            throw new InvalidOperationException("ROWVERSION_CONFLICT：資料已被其他使用者修改，請重新整理。");
    }

    private static string BuildTripNo(DateOnly date) => $"T{date:yyyyMMdd}{DateTime.UtcNow:HHmmssfff}";

    private async Task AddHistoryAsync(VisitTrip trip, string? previous, string next, string action, int userId, string? comments, CancellationToken ct)
    {
        await workflow.AddStatusHistoryAsync(new VisitTripStatusHistory
        {
            VisitTripId = trip.VisitTripId,
            PreviousStatus = previous,
            NewStatus = next,
            Action = action,
            ActionByUserId = userId,
            Comments = comments,
            ActionAt = DateTime.UtcNow
        }, ct);
    }

    private async Task AuditAsync(int? userId, string entityType, string entityId, string action, object? oldValue, object? newValue, CancellationToken ct)
    {
        await workflow.AddAuditAsync(new AuditLog
        {
            UserId = userId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            OldValues = oldValue is null ? null : System.Text.Json.JsonSerializer.Serialize(oldValue),
            NewValues = newValue is null ? null : System.Text.Json.JsonSerializer.Serialize(newValue),
            CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        }, ct);
    }
}
