using FieldVisit.Domain;
using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public sealed class LeaderService(
    ICurrentUserService current,
    ITripRepository trips,
    IMileageRepository mileage,
    IRouteCalculationService route,
    IWorkflowRepository workflow,
    ITripSnapshotRepository snapshots,
    IUnitOfWork uow,
    TripService tripService)
{
    public async Task<List<TripDto>> ReviewQueueAsync(CancellationToken ct)
    {
        var user = RequireLeader();
        var rows = await trips.GetTeamQueueAsync(user.TeamIds, ct);
        var result = new List<TripDto>();
        foreach (var row in rows) result.Add(await tripService.MapAsync(row, ct));
        return result;
    }

    public async Task<MileageBatchResult> CalculateBatchAsync(MileageBatchRequest request, CancellationToken ct)
    {
        var user = RequireLeader();
        var mode = request.Mode?.Trim() ?? "AllPending";
        if (mode.Equals("DateRange", StringComparison.OrdinalIgnoreCase) &&
            (request.StartDate is null || request.EndDate is null || request.EndDate < request.StartDate))
            throw new InvalidOperationException("日期區間不正確。");
        if (mode.Equals("Selected", StringComparison.OrdinalIgnoreCase) &&
            (request.SelectedTripIds is null || request.SelectedTripIds.Count == 0))
            throw new InvalidOperationException("請先勾選要計算里程的行程。");

        var rows = await trips.GetPendingMileageAsync(
            user.TeamIds,
            mode.Equals("DateRange", StringComparison.OrdinalIgnoreCase) ? request.StartDate : null,
            mode.Equals("DateRange", StringComparison.OrdinalIgnoreCase) ? request.EndDate : null,
            mode.Equals("Selected", StringComparison.OrdinalIgnoreCase) ? request.SelectedTripIds : null,
            ct);

        var items = new List<MileageBatchItem>();
        int ok = 0, failed = 0, skipped = 0;

        foreach (var trip in rows)
        {
            if (trip.Stops.Count < 2)
            {
                skipped++;
                items.Add(new MileageBatchItem(trip.VisitTripId, trip.TripNo, "Skipped", null, "STOP_INSUFFICIENT", "站點不足。"));
                continue;
            }

            var result = await route.CalculateAsync(trip, ct);
            if (!result.Success || result.DistanceKm is null)
            {
                failed++;
                items.Add(new MileageBatchItem(trip.VisitTripId, trip.TripNo, "Failed", null, result.ErrorCode, result.ErrorMessage));
                continue;
            }

            var calc = await mileage.GetByTripAsync(trip.VisitTripId, true, ct);
            if (calc is null)
            {
                calc = new MileageCalculation { VisitTripId = trip.VisitTripId, CreatedAt = DateTime.UtcNow };
                await mileage.AddAsync(calc, ct);
            }

            var previous = trip.Status;
            trip.Status = TripStatuses.PendingApproval;
            trip.UpdatedAt = DateTime.UtcNow;
            trip.UpdatedByUserId = user.UserId;
            calc.SystemDistanceKm = result.DistanceKm;
            calc.ApprovedDistanceKm = result.DistanceKm;
            calc.CalculationSource = "MockRoute/UAT";
            calc.CalculatedAt = DateTime.UtcNow;
            calc.UpdatedAt = DateTime.UtcNow;

            await workflow.AddStatusHistoryAsync(new VisitTripStatusHistory
            {
                VisitTripId = trip.VisitTripId,
                PreviousStatus = previous,
                NewStatus = TripStatuses.PendingApproval,
                Action = "MileageCalculated",
                ActionByUserId = user.UserId,
                Comments = $"SystemDistanceKm={result.DistanceKm:0.00}",
                ActionAt = DateTime.UtcNow
            }, ct);
            await workflow.AddAuditAsync(Audit(user.UserId, trip.VisitTripId, "MileageCalculated", new { result.DistanceKm }), ct);
            ok++;
            items.Add(new MileageBatchItem(trip.VisitTripId, trip.TripNo, "Success", result.DistanceKm, null, null));
        }

        await uow.SaveChangesAsync(ct);
        return new MileageBatchResult(rows.Count, ok, failed, skipped, items);
    }

    public async Task<TripDto> ApproveAsync(long tripId, ApproveTripRequest request, CancellationToken ct)
    {
        var user = RequireLeader();
        var trip = await GetScopedAsync(tripId, user, ct);
        if (trip.Status != TripStatuses.PendingApproval)
            throw new InvalidOperationException("只有待核准行程可以核准。");
        EnsureRowVersion(trip.RowVersion, request.RowVersion);

        V170TripMileageRules.EnsureReadyForApproval(trip.Stops.Count);

        if (request.ApprovedDistanceKm is null or <= 0)
            throw new InvalidOperationException("兩個以上地點的行程必須填寫大於 0 的核定里程。");

        var calc = await mileage.GetByTripAsync(tripId, true, ct);
        if (calc is null)
        {
            calc = new MileageCalculation { VisitTripId = tripId, CreatedAt = DateTime.UtcNow };
            await mileage.AddAsync(calc, ct);
        }

        var rate = await mileage.GetEffectiveRateAsync(
            trip.OrganizationId,
            trip.VehicleType ?? "Motorcycle",
            trip.VisitDate,
            ct)
            ?? throw new InvalidOperationException("找不到行程日期適用的補助費率。");

        var previous = trip.Status;
        trip.Status = TripStatuses.Approved;
        trip.ApprovedAt = DateTime.UtcNow;
        trip.ReturnReason = null;
        trip.UpdatedAt = DateTime.UtcNow;
        trip.UpdatedByUserId = user.UserId;

        calc.MileageRateRuleId = rate.MileageRateRuleId;
        calc.ApprovedDistanceKm = request.ApprovedDistanceKm;
        calc.RatePerKmSnapshot = rate.RatePerKm;
        calc.ClaimedAmount = calc.ClaimedDistanceKm.HasValue
            ? decimal.Round(calc.ClaimedDistanceKm.Value * rate.RatePerKm, 2)
            : null;
        calc.ApprovedAmount = decimal.Round(request.ApprovedDistanceKm.Value * rate.RatePerKm, 2);
        calc.UpdatedAt = DateTime.UtcNow;

        await workflow.AddApprovalAsync(new ApprovalRecord
        {
            VisitTripId = trip.VisitTripId,
            ApprovalStep = 1,
            ApproverUserId = user.UserId,
            Action = "Approved",
            Comments = request.Comments,
            ActionAt = DateTime.UtcNow
        }, ct);
        await workflow.AddStatusHistoryAsync(new VisitTripStatusHistory
        {
            VisitTripId = trip.VisitTripId,
            PreviousStatus = previous,
            NewStatus = TripStatuses.Approved,
            Action = "Approve",
            ActionByUserId = user.UserId,
            Comments = $"ApprovedKm={request.ApprovedDistanceKm};Rate={rate.RatePerKm};Amount={calc.ApprovedAmount}",
            ActionAt = DateTime.UtcNow
        }, ct);
        await workflow.AddAuditAsync(Audit(
            user.UserId,
            trip.VisitTripId,
            "TripApprove",
            new
            {
                ApprovedDistanceKm = request.ApprovedDistanceKm,
                RatePerKm = rate.RatePerKm,
                calc.ApprovedAmount
            }), ct);

        await snapshots.AddApprovedSnapshotAsync(trip, user, ct);
        await uow.SaveChangesAsync(ct);
        return await tripService.GetDtoAsync(tripId, ct);
    }

    public async Task<TripDto> ReturnAsync(long tripId, ReturnTripRequest request, CancellationToken ct)
    {
        var user = RequireLeader();
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("退回原因必填。");
        var trip = await GetScopedAsync(tripId, user, ct);
        if (trip.Status != TripStatuses.PendingApproval)
            throw new InvalidOperationException("只有待核准行程可以退回。");
        EnsureRowVersion(trip.RowVersion, request.RowVersion);

        var previous = trip.Status;
        trip.Status = TripStatuses.Returned;
        trip.ReturnReason = request.Reason.Trim();
        trip.UpdatedAt = DateTime.UtcNow;
        trip.UpdatedByUserId = user.UserId;

        await workflow.AddApprovalAsync(new ApprovalRecord
        {
            VisitTripId = trip.VisitTripId,
            ApprovalStep = 1,
            ApproverUserId = user.UserId,
            Action = "Returned",
            Comments = request.Reason.Trim(),
            ActionAt = DateTime.UtcNow
        }, ct);
        await workflow.AddStatusHistoryAsync(new VisitTripStatusHistory
        {
            VisitTripId = trip.VisitTripId,
            PreviousStatus = previous,
            NewStatus = TripStatuses.Returned,
            Action = "Return",
            ActionByUserId = user.UserId,
            Comments = request.Reason.Trim(),
            ActionAt = DateTime.UtcNow
        }, ct);
        await workflow.AddAuditAsync(Audit(user.UserId, trip.VisitTripId, "TripReturn", new { request.Reason }), ct);
        await uow.SaveChangesAsync(ct);
        return await tripService.GetDtoAsync(tripId, ct);
    }

    public async Task<BatchApproveResult> BatchApproveAsync(BatchApproveRequest request, CancellationToken ct)
    {
        int success = 0, failed = 0;
        var errors = new List<string>();
        foreach (var item in request.Items)
        {
            try
            {
                await ApproveAsync(item.VisitTripId, new ApproveTripRequest(item.ApprovedDistanceKm, item.RowVersion, "Batch approve"), ct);
                success++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
            {
                failed++;
                errors.Add($"{item.VisitTripId}: {ex.Message}");
            }
        }
        return new BatchApproveResult(success, failed, errors);
    }

    private CurrentUserDto RequireLeader()
    {
        var user = current.GetRequired();
        if (!user.Roles.Any(x => x.Equals("leader", StringComparison.OrdinalIgnoreCase)) || user.TeamIds.Count == 0)
            throw new UnauthorizedAccessException("只有具有效小組授權的小組長可以執行此操作。");
        return user;
    }

    private async Task<VisitTrip> GetScopedAsync(long id, CurrentUserDto user, CancellationToken ct)
    {
        var trip = await trips.GetAsync(id, true, ct) ?? throw new KeyNotFoundException("找不到行程。");
        if (!trip.TeamId.HasValue || !user.TeamIds.Contains(trip.TeamId.Value))
            throw new UnauthorizedAccessException("無權處理未授權小組資料。");
        return trip;
    }

    private static void EnsureRowVersion(byte[] currentValue, string expectedBase64)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(expectedBase64); }
        catch { throw new InvalidOperationException("RowVersion 格式不正確。"); }
        if (!currentValue.SequenceEqual(expected)) throw new InvalidOperationException("ROWVERSION_CONFLICT：資料已被其他使用者修改，請重新整理。");
    }

    private static AuditLog Audit(int userId, long tripId, string action, object value) => new()
    {
        UserId = userId,
        EntityType = "Trip",
        EntityId = tripId.ToString(),
        Action = action,
        NewValues = System.Text.Json.JsonSerializer.Serialize(value),
        CorrelationId = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow
    };
}
