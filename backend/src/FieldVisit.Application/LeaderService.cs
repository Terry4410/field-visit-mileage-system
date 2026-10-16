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
    TripService tripService,
    IUserRepository users,
    IV170AccessControl access,
    IV180GoogleMileageGovernanceRepository? mileageGovernance = null)
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
        if (!mode.Equals("AllPending", StringComparison.OrdinalIgnoreCase)
            && !mode.Equals("DateRange", StringComparison.OrdinalIgnoreCase)
            && !mode.Equals("Selected", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("里程計算範圍不正確。");

        var selectedIds = request.SelectedTripIds?.Distinct().ToArray();
        if (mode.Equals("Selected", StringComparison.OrdinalIgnoreCase)
            && selectedIds!.Length != request.SelectedTripIds!.Count)
            throw new InvalidOperationException("MILEAGE_BATCH_SELECTION_DUPLICATE：選取清單含重複行程。");

        var rows = await trips.GetPendingMileageAsync(
            user.TeamIds,
            mode.Equals("DateRange", StringComparison.OrdinalIgnoreCase) ? request.StartDate : null,
            mode.Equals("DateRange", StringComparison.OrdinalIgnoreCase) ? request.EndDate : null,
            mode.Equals("Selected", StringComparison.OrdinalIgnoreCase) ? selectedIds : null,
            ct);
        var eligibleRows = new List<VisitTrip>();
        foreach (var trip in rows)
        {
            var submitted = await snapshots.GetLatestAsync(trip.VisitTripId, "Submitted", ct);
            List<RouteCalculationAttempt> attempts = mileageGovernance is null
                ? new List<RouteCalculationAttempt>()
                : await mileageGovernance.GetRouteCalculationAttemptsAsync(trip.VisitTripId, ct);
            if (submitted is not null && V180MileageCanonicalization.HasRetryEvidence(submitted, attempts))
                eligibleRows.Add(trip);
        }
        if (mode.Equals("Selected", StringComparison.OrdinalIgnoreCase) && eligibleRows.Count != selectedIds!.Length)
            throw new InvalidOperationException("MILEAGE_BATCH_SELECTION_STALE_OR_UNAUTHORIZED：選取行程已不可計算或不在授權範圍。");
        rows = eligibleRows;
        if (rows.Count == 0)
            throw new InvalidOperationException("MILEAGE_BATCH_NO_CURRENT_ROUTE_FAILURE：沒有具目前路線 Google 失敗證據的可重試行程。");

        var items = new List<MileageBatchItem>();
        int ok = 0, failed = 0, skipped = 0;

        foreach (var trip in rows)
        {
            if (trip.Stops.Count < V170TripMileageRules.MinimumVisitStopCount)
            {
                skipped++;
                items.Add(new MileageBatchItem(trip.VisitTripId, trip.TripNo, "Skipped", null, "STOP_INSUFFICIENT", V170TripMileageRules.ApprovalMinimumStopsMessage));
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
        // Serialize live-state validation and the decision/history/snapshot
        // write so concurrent approvals observe the first committed decision.
        await using var approvalTransaction = await uow.BeginSerializableTransactionAsync(ct);
        // Live HR, role and team reads occur inside the serializable transaction;
        // revocation cannot commit between authorization and the decision.
        var user = await RequireLiveLeaderAsync(ct);
        var trip = await trips.GetForApprovalAsync(tripId, ct) ?? throw new KeyNotFoundException("找不到行程。");
        if (!trip.TeamId.HasValue || !user.TeamIds.Contains(trip.TeamId.Value))
            throw new UnauthorizedAccessException("無權處理未授權小組資料。");
        V180MileageGovernanceRules.EnsureLeaderApprovalState(trip.Status);
        if (trip.UserId == user.UserId)
            throw new UnauthorizedAccessException("TRIP_SELF_APPROVAL_DENIED：小組長不得核准自己的行程。");
        EnsureRowVersion(trip.RowVersion, request.RowVersion);

        V170TripMileageRules.EnsureReadyForApproval(trip.Stops.Count);

        if (request.ApprovedDistanceKm is null or <= 0)
            throw new InvalidOperationException("含至少一個拜訪地點的行程必須填寫大於 0 的核定里程。");

        var calc = await mileage.GetByTripAsync(tripId, true, ct);
        if (calc is null)
        {
            calc = new MileageCalculation { VisitTripId = tripId, CreatedAt = DateTime.UtcNow };
            await mileage.AddAsync(calc, ct);
        }

        var decisionSource =
            V180MileageGovernanceRules.RequireLeaderApprovalDecisionSource(
                request.DistanceDecisionSource);
        var adjustmentReason = decisionSource == "LeaderAdjusted"
            ? V180MileageGovernanceRules.RequireLeaderAdjustmentReason(request.AdjustmentReason)
            : null;
        var submittedBasis = await snapshots.GetLatestAsync(trip.VisitTripId, "Submitted", ct)
            ?? throw new InvalidOperationException("SUBMITTED_SNAPSHOT_REQUIRED：F-B 核准必須使用最新 Submitted Snapshot。");
        var basis = V180MileageCanonicalization.BuildSubmittedSnapshotBasis(submittedBasis);
        var approvalBasisHash = V180MileageCanonicalization.HashRoute(basis);
        RouteCalculationAttempt? selectedAttempt = null;
        Guid governanceCorrelationId;

        if (decisionSource == "ManualFallback")
        {
            if (request.RouteCalculationAttemptId.HasValue)
                throw new InvalidOperationException("F_B_MANUAL_FALLBACK_ATTEMPT：ManualFallback 不得選取 route attempt。");
            if (mileageGovernance is null)
                throw new InvalidOperationException("F_B_GOVERNANCE_REPOSITORY_REQUIRED：治理 repository 尚未設定。");
            var attempts = await mileageGovernance.GetRouteCalculationAttemptsAsync(trip.VisitTripId, ct);
            var hasCurrentSuccessfulEvidence = attempts.Any(attempt =>
                attempt.Status == "Succeeded"
                && V180MileageCanonicalization.IsAuthoritativeCurrentRouteAttempt(attempt, submittedBasis));
            if (hasCurrentSuccessfulEvidence)
                throw new InvalidOperationException(
                    "F_B_MANUAL_FALLBACK_GOOGLE_SUCCESS_EXISTS：目前路線已有有效 Google 成功結果，不得以 ManualFallback 取代。");
            var hasCurrentFailedEvidence = attempts.Any(attempt =>
                attempt.Status == "Failed"
                && V180MileageCanonicalization.IsAuthoritativeCurrentRouteAttempt(attempt, submittedBasis));
            if (!hasCurrentFailedEvidence)
                throw new InvalidOperationException(
                    "F_B_MANUAL_FALLBACK_EVIDENCE_REQUIRED：ManualFallback 必須有目前 Submitted 路線的 Google 失敗證據。");
            governanceCorrelationId = Guid.NewGuid();
        }
        else
        {
            if (!request.RouteCalculationAttemptId.HasValue)
                throw new InvalidOperationException("F_B_ROUTE_ATTEMPT_REQUIRED：此決策模式必須選取成功的 route attempt。");
            if (mileageGovernance is null)
                throw new InvalidOperationException("F_B_GOVERNANCE_REPOSITORY_REQUIRED：治理 repository 尚未設定。");
            selectedAttempt = await mileageGovernance.GetRouteCalculationAttemptAsync(
                request.RouteCalculationAttemptId.Value, ct)
                ?? throw new InvalidOperationException("F_B_ROUTE_ATTEMPT_NOT_FOUND：找不到指定的 route attempt。");
            var canonicalVehicle = V180MileageCanonicalization.CanonicalVehicleType(basis.VehicleType);
            if (selectedAttempt.Status != "Succeeded"
                || selectedAttempt.VisitTripId != trip.VisitTripId
                || (selectedAttempt.BasisType == "SubmittedSnapshot"
                    ? selectedAttempt.BasisVisitTripSnapshotId != submittedBasis.VisitTripSnapshotId
                    : selectedAttempt.BasisType != "Draft" || selectedAttempt.BasisVisitTripSnapshotId is not null)
                || !selectedAttempt.RequestBasisHash.SequenceEqual(approvalBasisHash)
                || selectedAttempt.RequestedVehicleType != V180MileageCanonicalization.ToDbRequestedVehicleType(canonicalVehicle)
                || selectedAttempt.TravelMode != V180MileageCanonicalization.ToTravelMode(canonicalVehicle))
                throw new InvalidOperationException(
                    "F_B_ROUTE_ATTEMPT_STALE：route attempt 必須成功且對應目前最新 Submitted Snapshot 與 canonical basis。");
            governanceCorrelationId = selectedAttempt.CorrelationId;
        }

        var rate = await mileage.GetEffectiveRateAsync(
            trip.OrganizationId,
            trip.VehicleType ?? "Motorcycle",
            trip.VisitDate,
            ct)
            ?? throw new InvalidOperationException("找不到行程日期適用的補助費率。");

        var previous = trip.Status;
        var actionAt = DateTime.UtcNow;
        trip.Status = TripStatuses.Approved;
        trip.ApprovedAt = actionAt;
        trip.ReturnReason = null;
        trip.UpdatedAt = actionAt;
        trip.UpdatedByUserId = user.UserId;

        calc.MileageRateRuleId = rate.MileageRateRuleId;
        calc.ApprovedDistanceKm = request.ApprovedDistanceKm;
        calc.RatePerKmSnapshot = rate.RatePerKm;
        calc.ClaimedAmount = calc.ClaimedDistanceKm.HasValue
            ? decimal.Round(calc.ClaimedDistanceKm.Value * rate.RatePerKm, 2)
            : null;
        calc.ApprovedAmount = decimal.Round(request.ApprovedDistanceKm.Value * rate.RatePerKm, 2);
        calc.UpdatedAt = DateTime.UtcNow;

        calc.SelectedRouteCalculationAttemptId = selectedAttempt?.RouteCalculationAttemptId;
        calc.ManualFallbackUsed = decisionSource == "ManualFallback";
        calc.DistanceDecisionGovernanceVersion = V180MileageGovernanceRules.GovernanceVersion;
        calc.ApprovedDistanceSource = decisionSource;
        calc.ApprovalBasisCode = V180MileageGovernanceRules.SubmittedSnapshotBasisCode;
        calc.ApprovalBasisHash = approvalBasisHash;
        calc.DistanceApprovedAt = actionAt;
        calc.DistanceApprovedByUserId = user.UserId;
        calc.InvalidatedAt = null;
        calc.InvalidatedByUserId = null;
        calc.InvalidationReason = null;

        var approval = new ApprovalRecord
        {
            VisitTripId = trip.VisitTripId,
            ApprovalStep = 1,
            ApproverUserId = user.UserId,
            Action = "Approved",
            Comments = request.Comments,
            ActionAt = actionAt
        };
        await workflow.AddApprovalAsync(approval, ct);
        await workflow.AddStatusHistoryAsync(new VisitTripStatusHistory
        {
            VisitTripId = trip.VisitTripId,
            PreviousStatus = previous,
            NewStatus = TripStatuses.Approved,
            Action = "Approve",
            ActionByUserId = user.UserId,
            Comments = $"ApprovedKm={request.ApprovedDistanceKm};Rate={rate.RatePerKm};Amount={calc.ApprovedAmount}",
            ActionAt = actionAt
        }, ct);
        await workflow.AddAuditAsync(Audit(
            user.UserId,
            trip.VisitTripId,
            "TripApprove",
            new
            {
                ApprovedDistanceKm = request.ApprovedDistanceKm,
                DecisionSource = decisionSource,
                AdjustmentReason = adjustmentReason,
                RatePerKm = rate.RatePerKm,
                calc.ApprovedAmount
            }), ct);

        if (mileageGovernance is null)
            throw new InvalidOperationException("F_B_GOVERNANCE_REPOSITORY_REQUIRED：治理 repository 尚未設定。");
        if (decisionSource == "ManualFallback")
        {
            await mileageGovernance.AddGovernanceEventAsync(
                new V180MileageGovernanceEventRequest(
                    trip.VisitTripId, submittedBasis.VisitTripSnapshotId, null,
                    "ManualFallback", "LEADER_MANUAL_FALLBACK",
                    "Leader approved company mileage using manual fallback.",
                    governanceCorrelationId, actionAt, user.UserId), ct);
        }
        await mileageGovernance.AddGovernanceEventAsync(
            new V180MileageGovernanceEventRequest(
                trip.VisitTripId, submittedBasis.VisitTripSnapshotId,
                selectedAttempt?.RouteCalculationAttemptId, "Approved", decisionSource,
                decisionSource == "LeaderAdjusted"
                    ? $"Leader adjusted company mileage: {adjustmentReason}"
                    : "Current company mileage decision approved.", governanceCorrelationId,
                actionAt, user.UserId), ct);

        await snapshots.AddApprovedSnapshotAsync(trip, user, ct);
        await uow.SaveChangesAsync(ct);
        await approvalTransaction.CommitAsync(ct);

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
                await ApproveAsync(
                    item.VisitTripId,
                    new ApproveTripRequest(
                        item.ApprovedDistanceKm,
                        item.RowVersion,
                        null,
                        item.DistanceDecisionSource,
                        item.RouteCalculationAttemptId,
                        item.AdjustmentReason),
                    ct);
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

    private async Task<CurrentUserDto> RequireLiveLeaderAsync(CancellationToken ct)
    {
        var tokenUser = RequireLeader();
        var liveUser = await users.GetProfileAsync(tokenUser.UserId, ct)
            ?? throw new UnauthorizedAccessException("LEADER_LIVE_PROFILE_REQUIRED：找不到目前人員授權資料。");
        var eligibility = await access.EvaluateLoginAsync(liveUser.UserId, adminEnabled: false, ct);
        if (!eligibility.IsAllowed)
            throw new UnauthorizedAccessException("LEADER_HR_ACCESS_REVOKED：目前人事狀態不允許核准行程。");
        if (!liveUser.Roles.Any(x => x.Equals("leader", StringComparison.OrdinalIgnoreCase))
            || liveUser.TeamIds.Count == 0)
            throw new UnauthorizedAccessException("LEADER_ROLE_OR_TEAM_REVOKED：目前已無有效小組長授權。");
        return liveUser;
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
