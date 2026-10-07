using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180CorrectionMileageService(
    AppDbContext db,
    ICurrentUserService current,
    IV180GoogleMileageGovernanceRepository governance,
    IV180RouteProvider routeProvider,
    IUnitOfWork uow)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan TerminalizationTimeout = TimeSpan.FromSeconds(30);
    private const decimal DistanceToleranceKm = 0.01m;

    public async Task<V180RouteOrchestrationResult> CalculateAsync(long correctionRequestId, CancellationToken ct)
    {
        var admin = current.GetRequired();
        if (!admin.Roles.Any(x => x.Equals("admin", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("目前角色無權執行更正路線計算。");

        var correction = await db.CorrectionRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CorrectionRequestId == correctionRequestId, ct)
            ?? throw new KeyNotFoundException("找不到更正申請。");
        if (correction.Status != "PendingAdminClose")
            throw new InvalidOperationException("CORRECTION_ROUTE_STATUS：只有待管理者結案的更正申請可以重新計算里程。");

        var trip = await db.VisitTrips.AsNoTracking()
            .SingleAsync(x => x.VisitTripId == correction.VisitTripId, ct);
        if (admin.OrganizationId.HasValue && trip.OrganizationId != admin.OrganizationId.Value)
            throw new UnauthorizedAccessException("無權處理其他 Organization 資料。");

        var baseSnapshot = await db.VisitTripSnapshots.AsNoTracking()
            .Include(x => x.Stops)
            .SingleAsync(x => x.VisitTripSnapshotId == correction.BaseSnapshotId, ct);
        var proposal = JsonSerializer.Deserialize<CorrectionProposal>(correction.ProposedChangesJson ?? "{}", JsonOptions)
            ?? throw new InvalidOperationException("更正內容無法解析。");
        if (proposal.Stops.Count < V170TripMileageRules.MinimumVisitStopCount)
            throw new InvalidOperationException(V170TripMileageRules.MinimumStopsMessage);
        if (proposal.ApprovedDistanceKm is not > 0)
            throw new InvalidOperationException("CORRECTION_APPROVED_DISTANCE_REQUIRED：更正路線重算前必須有大於 0 的核定里程。");

        var basis = V180MileageCanonicalization.BuildCorrectionProposalBasis(baseSnapshot, proposal);
        var vehicle = V180MileageCanonicalization.CanonicalVehicleType(baseSnapshot.VehicleTypeSnapshot ?? "Motorcycle");
        var travelMode = V180MileageCanonicalization.ToTravelMode(vehicle);
        var basisHash = V180MileageCanonicalization.HashCorrectionProposal(baseSnapshot, proposal);
        var correlationId = Guid.NewGuid();
        var requestedAt = DateTime.UtcNow;

        // The 1800_007 schema deliberately permits CorrectionRecalculate on Draft basis.
        // The immutable correction-proposal hash is the authoritative basis evidence.
        var attempt = await governance.AddRouteCalculationAttemptAsync(
            new V180RouteCalculationAttemptRequest(
                trip.VisitTripId,
                "Draft",
                null,
                "CorrectionRecalculate",
                V180MileageCanonicalization.ToDbRequestedVehicleType(vehicle),
                travelMode,
                routeProvider.ProviderName,
                basis.Stops.Count,
                basisHash,
                correlationId,
                requestedAt,
                admin.UserId),
            ct);
        await uow.SaveChangesAsync(ct);
        ct.ThrowIfCancellationRequested();

        V180RouteProviderResult providerResult;
        OperationCanceledException? providerCancellation = null;
        try
        {
            providerResult = await routeProvider.CalculateAsync(
                new V180RouteProviderRequest(
                    correlationId,
                    travelMode,
                    basis.Start?.Address,
                    basis.Stops.OrderBy(x => x.StopSequence).Select(x => x.AddressSnapshot).ToArray(),
                    basis.End?.Address),
                ct);
        }
        catch (OperationCanceledException ex)
        {
            providerCancellation = ex;
            providerResult = new(false, null, null, null, "PROVIDER_CANCELLED", "Provider operation was cancelled.");
        }
        catch
        {
            providerResult = new(false, null, null, null, "PROVIDER_EXCEPTION", "Provider operation failed.");
        }

        using var finalCts = new CancellationTokenSource(TerminalizationTimeout);
        var providerSuccess = providerResult.Success && providerResult.SuggestedDistanceKm is > 0;
        var distanceMatches = providerSuccess
            && Math.Abs(providerResult.SuggestedDistanceKm!.Value - proposal.ApprovedDistanceKm.Value) <= DistanceToleranceKm;
        var validSuccess = providerSuccess && distanceMatches;

        (string? Code, string? Message) failure;
        if (validSuccess)
        {
            failure = (null, null);
        }
        else if (providerSuccess)
        {
            failure = (
                "CORRECTION_DISTANCE_MISMATCH",
                $"Google Maps API route distance {providerResult.SuggestedDistanceKm:0.##} km does not match correction approved distance {proposal.ApprovedDistanceKm:0.##} km.");
        }
        else
        {
            failure = V180MileageGovernanceRules.SanitizeProviderFailure(
                providerResult.Success ? "PROVIDER_INVALID_RESULT" : providerResult.ErrorCode,
                providerResult.Success ? "Provider returned an invalid route result." : providerResult.ErrorMessage);
        }

        var completedAt = DateTime.UtcNow;
        var finalized = await governance.TryFinalizeRouteCalculationAttemptAsync(
            attempt.RouteCalculationAttemptId,
            validSuccess ? "Succeeded" : "Failed",
            failure.Code,
            failure.Message,
            completedAt,
            finalCts.Token);
        if (!finalized)
            throw new InvalidOperationException("CORRECTION_ROUTE_ATTEMPT_TERMINAL：更正 route attempt 已結案，不得再次變更。");

        await governance.AddGovernanceEventAsync(
            new V180MileageGovernanceEventRequest(
                trip.VisitTripId,
                correction.BaseSnapshotId,
                attempt.RouteCalculationAttemptId,
                validSuccess ? "Calculated" : "CalculationFailed",
                failure.Code,
                failure.Message,
                correlationId,
                completedAt,
                admin.UserId),
            finalCts.Token);
        await uow.SaveChangesAsync(finalCts.Token);

        if (providerCancellation is not null) throw providerCancellation;
        ct.ThrowIfCancellationRequested();

        return new V180RouteOrchestrationResult(
            attempt.RouteCalculationAttemptId,
            correlationId,
            validSuccess ? "Succeeded" : "Failed",
            validSuccess ? providerResult.SuggestedDistanceKm : null,
            validSuccess ? providerResult.DurationSeconds : null,
            validSuccess ? providerResult.EncodedPolyline : null,
            failure.Code,
            failure.Message);
    }
}
