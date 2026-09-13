using FieldVisit.Domain;
using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

/// <summary>
/// F-B synchronous orchestration. A durable Pending attempt is flushed before
/// the provider is invoked. Provider calls are deliberately outside transaction
/// boundaries and are never enclosed by an EF execution strategy.
/// </summary>
public sealed class V180GoogleMileageOrchestrationService(
    ICurrentUserService current,
    ITripRepository trips,
    IMasterRepository masters,
    IV180TripContextReader tripContext,
    ITripSnapshotRepository snapshots,
    IV180GoogleMileageGovernanceRepository governance,
    IV180RouteProvider routeProvider,
    IV180GeocodingProvider geocodingProvider,
    IUnitOfWork uow)
{
    public async Task<V180RouteOrchestrationResult> PreviewRouteAsync(long tripId, CancellationToken ct)
    {
        var user = RequireRole("visitor");
        var trip = await trips.GetAsync(tripId, false, ct)
            ?? throw new KeyNotFoundException("找不到行程。");
        if (trip.UserId != user.UserId)
            throw new UnauthorizedAccessException("只能預覽自己的行程路線。");
        if (trip.Status is not (TripStatuses.Draft or TripStatuses.Returned))
            throw new InvalidOperationException("F_B_ROUTE_PREVIEW_STATUS：只有草稿或已退回行程可以預覽路線。");
        if (!trip.EmploymentId.HasValue)
            throw new InvalidOperationException("F_B_ROUTE_PREVIEW_CONTEXT：v1.8 路線預覽需要明確的 Employment context。");

        var context = await tripContext.ResolveAsync(user, trip.VisitDate, trip.TeamId, ct);
        V180TripPersistenceRules.EnsureReadyForSubmit(
            context, trip.EmploymentId.Value, trip.TeamId,
            trip.StartDeploymentSiteId, trip.EndDeploymentSiteId);
        var start = context.EligibleDeploymentSites.Single(x => x.DeploymentSiteId == trip.StartDeploymentSiteId);
        var end = context.EligibleDeploymentSites.Single(x => x.DeploymentSiteId == trip.EndDeploymentSiteId);
        var basis = V180MileageCanonicalization.BuildDraftBasis(
            trip,
            new V180DeploymentSiteBasis(start.SiteCode, start.Address),
            new V180DeploymentSiteBasis(end.SiteCode, end.Address));

        return await CalculateRouteAsync(
            trip, basis, "Draft", null, "VisitorCalculate", user.UserId, false, ct);
    }

    public async Task<V180RouteOrchestrationResult> RetryRouteAsync(long tripId, CancellationToken ct)
    {
        var user = RequireRole("leader");
        var trip = await trips.GetAsync(tripId, false, ct)
            ?? throw new KeyNotFoundException("找不到行程。");
        if (!trip.TeamId.HasValue || !user.TeamIds.Contains(trip.TeamId.Value))
            throw new UnauthorizedAccessException("無權重試未授權小組的行程路線。");
        if (trip.Status is not (TripStatuses.Submitted or TripStatuses.RoutePending or TripStatuses.RouteCalculated or TripStatuses.PendingApproval))
            throw new InvalidOperationException("F_B_ROUTE_RETRY_STATUS：此行程狀態不可重試路線。");

        var submitted = await snapshots.GetLatestAsync(tripId, "Submitted", ct)
            ?? throw new InvalidOperationException("SUBMITTED_SNAPSHOT_REQUIRED：路線重試必須使用最新 Submitted Snapshot。");
        var basis = V180MileageCanonicalization.BuildSubmittedSnapshotBasis(submitted);
        return await CalculateRouteAsync(
            trip, basis, "SubmittedSnapshot", submitted.VisitTripSnapshotId,
            "LeaderRetry", user.UserId, true, ct);
    }

    public async Task<V180GeocodingOrchestrationResult> GeocodeLocationAsync(int locationId, CancellationToken ct)
    {
        var user = RequireAny("leader", "admin");
        var location = await masters.GetLocationAsync(locationId, false, ct)
            ?? throw new KeyNotFoundException("找不到地點。");
        EnsureLocationScope(user, location);

        var addressBasis = V180MileageCanonicalization.BuildAddressBasis(location);
        if (addressBasis.InputKind is null || addressBasis.InputValue is null)
            throw new InvalidOperationException("F_B_GEOCODING_INPUT_REQUIRED：地點缺少可解析的地址或 Plus Code。");
        var addressHash = V180MileageCanonicalization.HashAddress(location);
        var correlationId = Guid.NewGuid();
        var requestedAt = DateTime.UtcNow;
        var attempt = await governance.AddGeocodingAttemptAsync(
            new V180GeocodingAttemptRequest(
                location.LocationId, geocodingProvider.ProviderName, addressHash,
                correlationId, requestedAt, user.UserId), ct);

        // The attempt identity and Pending state must be durable before provider I/O.
        await uow.SaveChangesAsync(ct);

        V180GeocodingProviderResult providerResult;
        try
        {
            providerResult = await geocodingProvider.GeocodeAsync(
                new V180GeocodingProviderRequest(correlationId, addressBasis.InputKind, addressBasis.InputValue), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            providerResult = new(false, null, null, "PROVIDER_EXCEPTION", "Provider operation failed.");
        }

        var validSuccess = providerResult.Success
            && providerResult.Latitude is >= -90 and <= 90
            && providerResult.Longitude is >= -180 and <= 180;
        var completedAt = DateTime.UtcNow;
        var failure = validSuccess
            ? (Code: (string?)null, Message: (string?)null)
            : V180MileageGovernanceRules.SanitizeProviderFailure(
                providerResult.Success ? "PROVIDER_INVALID_RESULT" : providerResult.ErrorCode,
                providerResult.Success ? "Provider returned invalid coordinates." : providerResult.ErrorMessage);
        var finalized = await governance.TryFinalizeGeocodingAttemptAsync(
            attempt.GeocodingAttemptId, validSuccess ? "Succeeded" : "Failed",
            failure.Code, failure.Message, completedAt, ct);
        if (!finalized)
            throw new InvalidOperationException("F_B_GEOCODING_ATTEMPT_TERMINAL：該 geocoding attempt 已結案，不得再次變更。");

        var selected = false;
        if (validSuccess)
        {
            // Fresh tracked read provides rowversion protection against an address
            // edit racing with the provider call. A stale hash is never selected.
            var currentLocation = await masters.GetLocationAsync(locationId, true, ct)
                ?? throw new KeyNotFoundException("找不到地點。");
            EnsureLocationScope(user, currentLocation);
            if (V180MileageCanonicalization.HashAddress(currentLocation).SequenceEqual(addressHash))
            {
                var finalizedAttempt = await governance.GetGeocodingAttemptAsync(
                    attempt.GeocodingAttemptId, ct)
                    ?? throw new InvalidOperationException("F_B_GEOCODING_ATTEMPT_NOT_FOUND：找不到已完成的 geocoding attempt。");
                V180MileageGovernanceRules.EnsureCurrentGeocodingSelection(currentLocation, finalizedAttempt);
                currentLocation.SelectedGeocodingAttemptId = attempt.GeocodingAttemptId;
                currentLocation.UpdatedAt = completedAt;
                await uow.SaveChangesAsync(ct);
                selected = true;
            }
        }

        return new V180GeocodingOrchestrationResult(
            attempt.GeocodingAttemptId, correlationId,
            validSuccess ? "Succeeded" : "Failed", selected,
            validSuccess ? providerResult.Latitude : null,
            validSuccess ? providerResult.Longitude : null,
            failure.Code, failure.Message);
    }

    private async Task<V180RouteOrchestrationResult> CalculateRouteAsync(
        VisitTrip trip,
        V180RouteBasis basis,
        string basisType,
        long? basisSnapshotId,
        string reason,
        int actorUserId,
        bool leaderRetry,
        CancellationToken ct)
    {
        if (basis.Stops.Count < 2)
            throw new InvalidOperationException("F_B_ROUTE_STOPS_REQUIRED：路線至少需要兩個拜訪地點。");
        var canonicalVehicle = V180MileageCanonicalization.CanonicalVehicleType(basis.VehicleType);
        var travelMode = V180MileageCanonicalization.ToTravelMode(canonicalVehicle);
        var basisHash = V180MileageCanonicalization.HashRoute(basis);
        var correlationId = Guid.NewGuid();
        var requestedAt = DateTime.UtcNow;
        var attempt = await governance.AddRouteCalculationAttemptAsync(
            new V180RouteCalculationAttemptRequest(
                trip.VisitTripId, basisType, basisSnapshotId, reason,
                V180MileageCanonicalization.ToDbRequestedVehicleType(canonicalVehicle),
                travelMode, routeProvider.ProviderName, basis.Stops.Count,
                basisHash, correlationId, requestedAt, actorUserId), ct);
        // The Pending attempt commits before provider I/O. This service never
        // opens a transaction around I/O.
        await uow.SaveChangesAsync(ct);

        V180RouteProviderResult providerResult;
        try
        {
            providerResult = await routeProvider.CalculateAsync(
                new V180RouteProviderRequest(
                    correlationId, travelMode, basis.Start?.Address,
                    basis.Stops.OrderBy(x => x.StopSequence).Select(x => x.AddressSnapshot).ToArray(),
                    basis.End?.Address), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            providerResult = new(false, null, null, null, "PROVIDER_EXCEPTION", "Provider operation failed.");
        }

        var validSuccess = providerResult.Success && providerResult.SuggestedDistanceKm is > 0;
        var completedAt = DateTime.UtcNow;
        var failure = validSuccess
            ? (Code: (string?)null, Message: (string?)null)
            : V180MileageGovernanceRules.SanitizeProviderFailure(
                providerResult.Success ? "PROVIDER_INVALID_RESULT" : providerResult.ErrorCode,
                providerResult.Success ? "Provider returned an invalid route result." : providerResult.ErrorMessage);
        var finalized = await governance.TryFinalizeRouteCalculationAttemptAsync(
            attempt.RouteCalculationAttemptId, validSuccess ? "Succeeded" : "Failed",
            failure.Code, failure.Message, completedAt, ct);
        if (!finalized)
            throw new InvalidOperationException("F_B_ROUTE_ATTEMPT_TERMINAL：該 route attempt 已結案，不得再次變更。");

        if (leaderRetry)
        {
            await governance.AddGovernanceEventAsync(
                new V180MileageGovernanceEventRequest(
                    trip.VisitTripId, basisSnapshotId, attempt.RouteCalculationAttemptId,
                    "LeaderRetry", null, "Leader requested a new route calculation attempt.",
                    correlationId, requestedAt, actorUserId), ct);
        }
        await governance.AddGovernanceEventAsync(
            new V180MileageGovernanceEventRequest(
                trip.VisitTripId, basisSnapshotId, attempt.RouteCalculationAttemptId,
                validSuccess ? "Calculated" : "CalculationFailed",
                failure.Code, failure.Message, correlationId, completedAt, actorUserId), ct);
        await uow.SaveChangesAsync(ct);

        return new V180RouteOrchestrationResult(
            attempt.RouteCalculationAttemptId, correlationId,
            validSuccess ? "Succeeded" : "Failed",
            validSuccess ? providerResult.SuggestedDistanceKm : null,
            validSuccess ? providerResult.DurationSeconds : null,
            validSuccess ? providerResult.EncodedPolyline : null,
            failure.Code, failure.Message);
    }

    private CurrentUserDto RequireRole(string role)
    {
        var user = current.GetRequired();
        if (!HasRole(user, role))
            throw new UnauthorizedAccessException("目前角色無權執行此操作。");
        return user;
    }

    private CurrentUserDto RequireAny(params string[] roles)
    {
        var user = current.GetRequired();
        if (!roles.Any(role => HasRole(user, role)))
            throw new UnauthorizedAccessException("目前角色無權執行此操作。");
        return user;
    }

    private static bool HasRole(CurrentUserDto user, string role) =>
        user.Roles.Any(x => x.Equals(role, StringComparison.OrdinalIgnoreCase));

    private static void EnsureLocationScope(CurrentUserDto user, Location location)
    {
        if (HasRole(user, "admin") && user.OrganizationId.HasValue
            && location.OrganizationId.HasValue && location.OrganizationId != user.OrganizationId)
            throw new UnauthorizedAccessException("無權處理其他 Organization 地點。");
        if (!HasRole(user, "admin") && HasRole(user, "leader")
            && (!location.TeamId.HasValue || !user.TeamIds.Contains(location.TeamId.Value)))
            throw new UnauthorizedAccessException("無權處理未授權小組地點。");
    }
}
