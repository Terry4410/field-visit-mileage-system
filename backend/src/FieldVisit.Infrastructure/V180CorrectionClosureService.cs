using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Final correction close guard for v1.8 mileage governance.
/// It delegates the established correction/snapshot lifecycle to V160FinalRepository,
/// then, inside the same transaction, replaces route evidence when the final
/// CorrectionProposal changes the canonical route basis or approved distance.
/// </summary>
public sealed class V180CorrectionClosureService(
    AppDbContext db,
    ICurrentUserService current,
    IV160FinalRepository repository,
    IV180RouteProvider routeProvider)
    : IV180CorrectionClosureService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CorrectionRequestDto> CloseAsync(
        long correctionRequestId,
        CloseCorrectionRequest request,
        CancellationToken ct)
    {
        var admin = current.GetRequired();
        if (!admin.Roles.Any(x => x.Equals("admin", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("目前角色無權結案更正申請。");

        var context = await LoadContextAsync(admin, correctionRequestId, ct);
        var distanceChanged = context.Proposal.ApprovedDistanceKm != context.BaseSnapshot.ApprovedDistanceKmSnapshot;
        var routeChanged = RouteBasisChanged(context.BaseSnapshot, context.Proposal);
        var decisionRequired = distanceChanged || routeChanged;

        if (!request.Approve || !decisionRequired)
            return await repository.CloseCorrectionAsync(admin, correctionRequestId, request, ct);

        var evidence = await ValidateDecisionAsync(context, request, ct);
        CorrectionRequestDto? result = null;
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var fresh = await LoadContextAsync(admin, correctionRequestId, ct);
            if (!fresh.ProposalHash.SequenceEqual(context.ProposalHash)
                || fresh.RowVersionBase64 != context.RowVersionBase64)
                throw new InvalidOperationException("ROWVERSION_CONFLICT：更正內容已變更，請重新載入後再操作。");

            result = await repository.CloseCorrectionAsync(admin, correctionRequestId, request, ct);

            var closed = await db.CorrectionRequests
                .SingleAsync(x => x.CorrectionRequestId == correctionRequestId, ct);
            if (!closed.ResultSnapshotId.HasValue || !closed.AdminClosedAt.HasValue || !closed.AdminClosedByUserId.HasValue)
                throw new InvalidOperationException("CORRECTION_CLOSE_EVIDENCE_MISSING：更正結案未產生完整 Snapshot evidence。");

            var snapshot = await db.VisitTripSnapshots
                .SingleAsync(x => x.VisitTripSnapshotId == closed.ResultSnapshotId.Value, ct);

            snapshot.SystemDistanceKmSnapshot = evidence.Source == "ProviderSuggested"
                ? fresh.Proposal.ApprovedDistanceKm
                : null;
            snapshot.MileageRouteAttemptIdSnapshot = evidence.SucceededAttempt?.RouteCalculationAttemptId;
            snapshot.RouteTravelModeSnapshot = evidence.TravelMode;
            snapshot.RouteCalculatedAtSnapshot = evidence.SucceededAttempt?.CompletedAt;
            snapshot.RouteCalculationStatusSnapshot = evidence.Source == "ProviderSuggested" ? "Succeeded" : "ManualFallback";
            snapshot.RouteErrorCodeSnapshot = null;
            snapshot.RouteCorrelationIdSnapshot = evidence.SucceededAttempt?.CorrelationId
                ?? evidence.FailedAttempt?.CorrelationId
                ?? Guid.NewGuid();
            snapshot.RouteProviderSnapshot = evidence.Source == "ProviderSuggested"
                ? evidence.SucceededAttempt!.Provider
                : "ManualFallback";
            snapshot.ApprovedDistanceSourceSnapshot = evidence.Source;
            snapshot.ApprovalBasisCodeSnapshot = V180MileageGovernanceRules.CorrectionProposalBasisCode;
            snapshot.ApprovalBasisHashSnapshot = fresh.ProposalHash.ToArray();
            snapshot.DistanceApprovedAtSnapshot = closed.AdminClosedAt;
            snapshot.ApproverUserId = closed.AdminClosedByUserId;
            snapshot.ApproverNameSnapshot = admin.DisplayName;

            var freshDistanceChanged = fresh.Proposal.ApprovedDistanceKm != fresh.BaseSnapshot.ApprovedDistanceKmSnapshot;
            if (!freshDistanceChanged)
            {
                db.MileageGovernanceEvents.Add(new MileageGovernanceEvent
                {
                    VisitTripId = closed.VisitTripId,
                    VisitTripSnapshotId = snapshot.VisitTripSnapshotId,
                    RouteCalculationAttemptId = evidence.SucceededAttempt?.RouteCalculationAttemptId,
                    EventType = "Approved",
                    ReasonCode = evidence.Source,
                    Message = "Correction route-basis decision finalized by Admin close.",
                    CorrelationId = snapshot.RouteCorrelationIdSnapshot ?? Guid.NewGuid(),
                    OccurredAt = closed.AdminClosedAt.Value,
                    ActorUserId = closed.AdminClosedByUserId
                });
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return result ?? throw new InvalidOperationException("CORRECTION_CLOSE_RESULT_MISSING");
    }

    private async Task<DecisionEvidence> ValidateDecisionAsync(
        CorrectionContext context,
        CloseCorrectionRequest request,
        CancellationToken ct)
    {
        var canonicalVehicle = V180MileageCanonicalization.CanonicalVehicleType(
            context.BaseSnapshot.VehicleTypeSnapshot ?? "Motorcycle");
        var expectedVehicle = V180MileageCanonicalization.ToDbRequestedVehicleType(canonicalVehicle);
        var expectedTravelMode = V180MileageCanonicalization.ToTravelMode(canonicalVehicle);

        var source = V180MileageGovernanceRules.ResolveCorrectionDecisionSource(
            request.DistanceDecisionSource,
            request.RouteCalculationAttemptId.HasValue);

        if (request.RouteCalculationAttemptId.HasValue)
        {
            var attempt = await db.RouteCalculationAttempts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.RouteCalculationAttemptId == request.RouteCalculationAttemptId.Value, ct)
                ?? throw new InvalidOperationException("F_B_CORRECTION_ROUTE_ATTEMPT_NOT_FOUND：找不到指定的 route attempt。");

            if (attempt.VisitTripId != context.Row.VisitTripId
                || attempt.CalculationReason != "CorrectionRecalculate"
                || attempt.Status != "Succeeded"
                || !attempt.RequestBasisHash.SequenceEqual(context.ProposalHash)
                || attempt.RequestedVehicleType != expectedVehicle
                || attempt.TravelMode != expectedTravelMode
                || !string.Equals(attempt.Provider, routeProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("F_B_CORRECTION_ROUTE_ATTEMPT_STALE：CorrectionRecalculate attempt 與 final proposal 不一致。");

            if (context.Proposal.ApprovedDistanceKm is not > 0)
                throw new InvalidOperationException("CORRECTION_APPROVED_DISTANCE_REQUIRED：Google route decision 必須有大於 0 的核定里程。");

            return new DecisionEvidence(source, expectedTravelMode, attempt, null);
        }

        if (context.Proposal.ClaimedDistanceKm is not > 0)
            throw new InvalidOperationException("CORRECTION_MANUAL_FALLBACK_REQUIRED：Google 無法取得可用里程時，必須填寫大於 0 的人工備援里程。");

        var matchingAttempts = await db.RouteCalculationAttempts.AsNoTracking()
            .Where(x => x.VisitTripId == context.Row.VisitTripId
                && x.CalculationReason == "CorrectionRecalculate"
                && x.RequestedVehicleType == expectedVehicle
                && x.TravelMode == expectedTravelMode
                && x.Provider == routeProvider.ProviderName)
            .OrderByDescending(x => x.RequestedAt)
            .ToListAsync(ct);
        matchingAttempts = matchingAttempts
            .Where(x => x.RequestBasisHash.SequenceEqual(context.ProposalHash))
            .ToList();

        if (matchingAttempts.Any(x => x.Status == "Succeeded"))
            throw new InvalidOperationException("CORRECTION_GOOGLE_SUCCESS_EXISTS：Google Maps API 已成功取得與更正案一致的里程，必須採 ProviderSuggested，不可改用人工備援。");
        if (matchingAttempts.Any(x => string.Equals(x.ErrorCode, "CORRECTION_DISTANCE_MISMATCH", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("CORRECTION_DISTANCE_MISMATCH：Google Maps API 已成功取得不同里程，不能改用人工備援；請依 Google 結果重新提出更正。");

        var matchingFailure = matchingAttempts.FirstOrDefault(x => x.Status == "Failed");
        if (matchingFailure is null)
            throw new InvalidOperationException("CORRECTION_GOOGLE_FAILURE_REQUIRED：人工備援前必須先對 final proposal 執行 Google Maps API，且確實無法取得可用里程。");

        return new DecisionEvidence(source, expectedTravelMode, null, matchingFailure);
    }

    private async Task<CorrectionContext> LoadContextAsync(
        CurrentUserDto admin,
        long correctionRequestId,
        CancellationToken ct)
    {
        var row = await db.CorrectionRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CorrectionRequestId == correctionRequestId, ct)
            ?? throw new KeyNotFoundException("找不到更正申請。");
        if (row.Status != "PendingAdminClose")
            throw new InvalidOperationException("此更正申請目前不需要管理者結案。");

        var trip = await db.VisitTrips.AsNoTracking()
            .SingleAsync(x => x.VisitTripId == row.VisitTripId, ct);
        if (admin.OrganizationId.HasValue && trip.OrganizationId != admin.OrganizationId.Value)
            throw new UnauthorizedAccessException("無權處理其他 Organization 資料。");

        var baseSnapshot = await db.VisitTripSnapshots.AsNoTracking()
            .Include(x => x.Stops)
            .SingleAsync(x => x.VisitTripSnapshotId == row.BaseSnapshotId, ct);
        var proposal = JsonSerializer.Deserialize<CorrectionProposal>(
            row.ProposedChangesJson ?? "{}", JsonOptions)
            ?? throw new InvalidOperationException("更正內容無法解析。");
        var proposalHash = V180MileageCanonicalization.HashCorrectionProposal(baseSnapshot, proposal);

        return new CorrectionContext(
            row,
            baseSnapshot,
            proposal,
            proposalHash,
            Convert.ToBase64String(row.RowVersion ?? []));
    }

    private static bool RouteBasisChanged(VisitTripSnapshot baseSnapshot, CorrectionProposal proposal)
    {
        var before = V180MileageCanonicalization.HashRoute(
            V180MileageCanonicalization.BuildSubmittedSnapshotBasis(baseSnapshot));
        var after = V180MileageCanonicalization.HashRoute(
            V180MileageCanonicalization.BuildCorrectionProposalBasis(baseSnapshot, proposal));
        return !before.SequenceEqual(after);
    }

    private sealed record CorrectionContext(
        CorrectionRequest Row,
        VisitTripSnapshot BaseSnapshot,
        CorrectionProposal Proposal,
        byte[] ProposalHash,
        string RowVersionBase64);

    private sealed record DecisionEvidence(
        string Source,
        string TravelMode,
        RouteCalculationAttempt? SucceededAttempt,
        RouteCalculationAttempt? FailedAttempt);
}
