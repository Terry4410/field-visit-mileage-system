using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class TripSnapshotRepository(AppDbContext db) : ITripSnapshotRepository
{
    private sealed record SnapshotShadow(
        long? PersonId, long? EmploymentId,
        int? CenterId, string? CenterCode, string? CenterName, string? TeamCode,
        int? StartDeploymentSiteId, string? StartDeploymentSiteName, int? StartDeploymentLocationId,
        int? EndDeploymentSiteId, string? EndDeploymentSiteName, int? EndDeploymentLocationId);

    public Task<VisitTripSnapshot?> GetLatestAsync(long tripId, string snapshotType, CancellationToken ct) =>
        db.VisitTripSnapshots.AsNoTracking().Include(x => x.Stops)
            .Where(x => x.VisitTripId == tripId && x.SnapshotType == snapshotType)
            .OrderByDescending(x => x.SnapshotVersion).FirstOrDefaultAsync(ct);

    public async Task AddSubmittedSnapshotAsync(
        VisitTrip trip, CurrentUserDto submitter, V180TripContextDto context, CancellationToken ct)
    {
        if (!trip.EmploymentId.HasValue || trip.EmploymentId.Value != context.EmploymentId)
            throw new InvalidOperationException("TRIP_CONTEXT_EMPLOYMENT_CHANGED：無法建立 Submitted Snapshot。");
        V180TripPersistenceRules.EnsureReadyForSubmit(
            context, trip.EmploymentId.Value, trip.TeamId,
            trip.StartDeploymentSiteId, trip.EndDeploymentSiteId);

        var employment = await db.Employments.AsNoTracking()
            .SingleAsync(x => x.EmploymentId == context.EmploymentId, ct);
        var organizationName = await db.Organizations.AsNoTracking()
            .Where(x => x.OrganizationId == trip.OrganizationId)
            .Select(x => x.OrganizationName).SingleAsync(ct);
        var team = context.Teams.Single(x => x.TeamId == context.SelectedTeamId);
        var start = context.EligibleDeploymentSites.Single(x => x.DeploymentSiteId == trip.StartDeploymentSiteId);
        var end = context.EligibleDeploymentSites.Single(x => x.DeploymentSiteId == trip.EndDeploymentSiteId);
        var calc = trip.MileageCalculation ?? await db.MileageCalculations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.VisitTripId == trip.VisitTripId, ct);
        var canonicalVehicle = V180MileageCanonicalization.CanonicalVehicleType(trip.VehicleType ?? "Motorcycle");

        var snapshot = new VisitTripSnapshot
        {
            VisitTripId = trip.VisitTripId,
            SnapshotVersion = await NextVersionAsync(trip.VisitTripId, ct),
            SnapshotType = "Submitted",
            TripNo = trip.TripNo,
            UserId = trip.UserId,
            EmployeeNoSnapshot = employment.EmployeeNo ?? submitter.EmployeeNo,
            DisplayNameSnapshot = submitter.DisplayName,
            OrganizationId = trip.OrganizationId,
            OrganizationNameSnapshot = organizationName,
            TeamId = trip.TeamId,
            TeamNameSnapshot = team.Name,
            StartDeploymentSiteCodeSnapshot = start.SiteCode,
            StartDeploymentAddressSnapshot = start.Address,
            EndDeploymentSiteCodeSnapshot = end.SiteCode,
            EndDeploymentAddressSnapshot = end.Address,
            VisitDate = trip.VisitDate,
            StartTime = trip.StartTime,
            EndTime = trip.EndTime,
            StatusSnapshot = trip.Status,
            VehicleTypeSnapshot = V180MileageCanonicalization.ToDbRequestedVehicleType(canonicalVehicle),
            ClaimedDistanceKmSnapshot = calc?.ClaimedDistanceKm,
            RouteTravelModeSnapshot = V180MileageCanonicalization.ToTravelMode(canonicalVehicle),
            SubmittedAtSnapshot = trip.SubmittedAt,
            NotesSnapshot = trip.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = submitter.UserId
        };
        await AddCurrentStopsAsync(snapshot, trip, ct);
        await db.VisitTripSnapshots.AddAsync(snapshot, ct);
        SetShadow(snapshot, new SnapshotShadow(
            employment.PersonId, employment.EmploymentId,
            start.CenterId, start.CenterCode, start.CenterName, team.Code,
            start.DeploymentSiteId, start.SiteName, start.LocationId,
            end.DeploymentSiteId, end.SiteName, end.LocationId));
    }

    public async Task AddApprovedSnapshotAsync(VisitTrip trip, CurrentUserDto approver, CancellationToken ct)
    {
        if (trip.EmploymentId.HasValue)
        {
            var submitted = await GetLatestAsync(trip.VisitTripId, "Submitted", ct)
                ?? throw new InvalidOperationException("SUBMITTED_SNAPSHOT_REQUIRED：v1.8 行程缺少 Submitted Snapshot，禁止以目前主檔重建歷史。");
            var shadow = await LoadShadowAsync(submitted.VisitTripSnapshotId, ct);
            var snapshot = CopySnapshot(submitted, await NextVersionAsync(trip.VisitTripId, ct), "Approved", approver.UserId);
            await ApplyApprovalAsync(snapshot, trip, approver, ct);
            await db.VisitTripSnapshots.AddAsync(snapshot, ct);
            SetShadow(snapshot, shadow);
            return;
        }

        var legacy = await BuildLegacyApprovedSnapshotAsync(trip, approver, ct);
        await db.VisitTripSnapshots.AddAsync(legacy, ct);
    }

    private async Task<VisitTripSnapshot> BuildLegacyApprovedSnapshotAsync(VisitTrip trip, CurrentUserDto approver, CancellationToken ct)
    {
        var visitor = await db.Users.AsNoTracking().FirstAsync(x => x.UserId == trip.UserId, ct);
        var organizationName = await db.Organizations.AsNoTracking().Where(x => x.OrganizationId == trip.OrganizationId)
            .Select(x => x.OrganizationName).FirstOrDefaultAsync(ct) ?? $"Organization {trip.OrganizationId}";
        var teamName = trip.TeamId.HasValue ? await db.Teams.AsNoTracking().Where(x => x.TeamId == trip.TeamId.Value)
            .Select(x => x.TeamName).FirstOrDefaultAsync(ct) : null;
        var calc = trip.MileageCalculation ?? await db.MileageCalculations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.VisitTripId == trip.VisitTripId, ct);
        var snapshot = new VisitTripSnapshot
        {
            VisitTripId = trip.VisitTripId, SnapshotVersion = await NextVersionAsync(trip.VisitTripId, ct), SnapshotType = "Approved",
            TripNo = trip.TripNo, UserId = trip.UserId, EmployeeNoSnapshot = visitor.EmployeeNo ?? "",
            DisplayNameSnapshot = visitor.DisplayName, OrganizationId = trip.OrganizationId,
            OrganizationNameSnapshot = organizationName, TeamId = trip.TeamId, TeamNameSnapshot = teamName,
            VisitDate = trip.VisitDate, StartTime = trip.StartTime, EndTime = trip.EndTime, StatusSnapshot = trip.Status,
            VehicleTypeSnapshot = trip.VehicleType, ClaimedDistanceKmSnapshot = calc?.ClaimedDistanceKm,
            SystemDistanceKmSnapshot = calc?.SystemDistanceKm, ApprovedDistanceKmSnapshot = calc?.ApprovedDistanceKm,
            RatePerKmSnapshot = calc?.RatePerKmSnapshot, SubsidyAmountSnapshot = calc?.ApprovedAmount,
            RouteProviderSnapshot = calc?.CalculationSource, SubmittedAtSnapshot = trip.SubmittedAt,
            ApprovedAtSnapshot = trip.ApprovedAt, ApproverUserId = approver.UserId,
            ApproverNameSnapshot = approver.DisplayName, NotesSnapshot = trip.Notes,
            CreatedAt = DateTime.UtcNow, CreatedByUserId = approver.UserId
        };
        await AddCurrentStopsAsync(snapshot, trip, ct);
        return snapshot;
    }

    private async Task AddCurrentStopsAsync(VisitTripSnapshot snapshot, VisitTrip trip, CancellationToken ct)
    {
        var locationIds = trip.Stops.Where(x => x.LocationId.HasValue).Select(x => x.LocationId!.Value).Distinct().ToList();
        var projectIds = trip.Stops.Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId!.Value).Distinct().ToList();
        var visitTypeIds = trip.Stops.Where(x => x.VisitTypeId.HasValue).Select(x => x.VisitTypeId!.Value).Distinct().ToList();
        var locations = await db.Locations.AsNoTracking().Where(x => locationIds.Contains(x.LocationId)).ToDictionaryAsync(x => x.LocationId, ct);
        var projects = await db.Projects.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId)).ToDictionaryAsync(x => x.ProjectId, ct);
        var visitTypes = await db.VisitTypes.AsNoTracking().Where(x => visitTypeIds.Contains(x.VisitTypeId)).ToDictionaryAsync(x => x.VisitTypeId, ct);
        foreach (var stop in trip.Stops.OrderBy(x => x.StopSequence))
        {
            locations.TryGetValue(stop.LocationId ?? -1, out var location);
            projects.TryGetValue(stop.ProjectId ?? -1, out var project);
            visitTypes.TryGetValue(stop.VisitTypeId ?? -1, out var visitType);
            snapshot.Stops.Add(new VisitTripSnapshotStop
            {
                StopSequence = stop.StopSequence, LocationId = stop.LocationId, LocationCodeSnapshot = location?.LocationCode,
                LocationNameSnapshot = stop.LocationNameSnapshot ?? location?.LocationName ?? "",
                AddressSnapshot = stop.AddressSnapshot ?? location?.Address ?? location?.PlusCode,
                ProjectId = stop.ProjectId, ProjectCodeSnapshot = project?.ProjectCode, ProjectNameSnapshot = project?.ProjectName,
                VisitTypeId = stop.VisitTypeId, VisitTypeCodeSnapshot = visitType?.VisitTypeCode,
                VisitTypeNameSnapshot = visitType?.VisitTypeName, VisitPurposeSnapshot = stop.VisitPurpose,
                NotesSnapshot = stop.Notes, CreatedAt = DateTime.UtcNow
            });
        }
    }

    private async Task<int> NextVersionAsync(long tripId, CancellationToken ct) =>
        (await db.VisitTripSnapshots.AsNoTracking().Where(x => x.VisitTripId == tripId)
            .MaxAsync(x => (int?)x.SnapshotVersion, ct) ?? 0) + 1;

    private static VisitTripSnapshot CopySnapshot(VisitTripSnapshot source, int version, string type, int actorUserId)
    {
        var copy = new VisitTripSnapshot
        {
            VisitTripId = source.VisitTripId, SnapshotVersion = version, SnapshotType = type,
            TripNo = source.TripNo, UserId = source.UserId,
            EmployeeNoSnapshot = source.EmployeeNoSnapshot, DisplayNameSnapshot = source.DisplayNameSnapshot,
            OrganizationId = source.OrganizationId, OrganizationNameSnapshot = source.OrganizationNameSnapshot,
            TeamId = source.TeamId, TeamNameSnapshot = source.TeamNameSnapshot,
            StartDeploymentSiteCodeSnapshot = source.StartDeploymentSiteCodeSnapshot,
            StartDeploymentAddressSnapshot = source.StartDeploymentAddressSnapshot,
            EndDeploymentSiteCodeSnapshot = source.EndDeploymentSiteCodeSnapshot,
            EndDeploymentAddressSnapshot = source.EndDeploymentAddressSnapshot,
            VisitDate = source.VisitDate, StartTime = source.StartTime, EndTime = source.EndTime,
            StatusSnapshot = source.StatusSnapshot, VehicleTypeSnapshot = source.VehicleTypeSnapshot,
            ClaimedDistanceKmSnapshot = source.ClaimedDistanceKmSnapshot,
            RouteTravelModeSnapshot = source.RouteTravelModeSnapshot,
            SubmittedAtSnapshot = source.SubmittedAtSnapshot,
            NotesSnapshot = source.NotesSnapshot, CreatedAt = DateTime.UtcNow, CreatedByUserId = actorUserId
        };
        foreach (var stop in source.Stops.OrderBy(x => x.StopSequence))
            copy.Stops.Add(new VisitTripSnapshotStop
            {
                StopSequence = stop.StopSequence, LocationId = stop.LocationId,
                LocationCodeSnapshot = stop.LocationCodeSnapshot, LocationNameSnapshot = stop.LocationNameSnapshot,
                AddressSnapshot = stop.AddressSnapshot, ProjectId = stop.ProjectId,
                ProjectCodeSnapshot = stop.ProjectCodeSnapshot, ProjectNameSnapshot = stop.ProjectNameSnapshot,
                VisitTypeId = stop.VisitTypeId, VisitTypeCodeSnapshot = stop.VisitTypeCodeSnapshot,
                VisitTypeNameSnapshot = stop.VisitTypeNameSnapshot, VisitPurposeSnapshot = stop.VisitPurposeSnapshot,
                NotesSnapshot = stop.NotesSnapshot, CreatedAt = DateTime.UtcNow
            });
        return copy;
    }

    private async Task<SnapshotShadow> LoadShadowAsync(long snapshotId, CancellationToken ct) =>
        await db.VisitTripSnapshots.AsNoTracking()
            .Where(x => x.VisitTripSnapshotId == snapshotId)
            .Select(x => new SnapshotShadow(
                EF.Property<long?>(x, "PersonIdSnapshot"),
                EF.Property<long?>(x, "EmploymentIdSnapshot"),
                EF.Property<int?>(x, "CenterIdSnapshot"),
                EF.Property<string?>(x, "CenterCodeSnapshot"),
                EF.Property<string?>(x, "CenterNameSnapshot"),
                EF.Property<string?>(x, "TeamCodeSnapshot"),
                EF.Property<int?>(x, "StartDeploymentSiteIdSnapshot"),
                EF.Property<string?>(x, "StartDeploymentSiteNameSnapshot"),
                EF.Property<int?>(x, "StartDeploymentLocationIdSnapshot"),
                EF.Property<int?>(x, "EndDeploymentSiteIdSnapshot"),
                EF.Property<string?>(x, "EndDeploymentSiteNameSnapshot"),
                EF.Property<int?>(x, "EndDeploymentLocationIdSnapshot")))
            .SingleAsync(ct);

    private void SetShadow(VisitTripSnapshot snapshot, SnapshotShadow shadow)
    {
        var entry = db.Entry(snapshot);
        entry.Property<long?>("PersonIdSnapshot").CurrentValue = shadow.PersonId;
        entry.Property<long?>("EmploymentIdSnapshot").CurrentValue = shadow.EmploymentId;
        entry.Property<int?>("CenterIdSnapshot").CurrentValue = shadow.CenterId;
        entry.Property<string?>("CenterCodeSnapshot").CurrentValue = shadow.CenterCode;
        entry.Property<string?>("CenterNameSnapshot").CurrentValue = shadow.CenterName;
        entry.Property<string?>("TeamCodeSnapshot").CurrentValue = shadow.TeamCode;
        entry.Property<int?>("StartDeploymentSiteIdSnapshot").CurrentValue = shadow.StartDeploymentSiteId;
        entry.Property<string?>("StartDeploymentSiteNameSnapshot").CurrentValue = shadow.StartDeploymentSiteName;
        entry.Property<int?>("StartDeploymentLocationIdSnapshot").CurrentValue = shadow.StartDeploymentLocationId;
        entry.Property<int?>("EndDeploymentSiteIdSnapshot").CurrentValue = shadow.EndDeploymentSiteId;
        entry.Property<string?>("EndDeploymentSiteNameSnapshot").CurrentValue = shadow.EndDeploymentSiteName;
        entry.Property<int?>("EndDeploymentLocationIdSnapshot").CurrentValue = shadow.EndDeploymentLocationId;
    }

    private async Task ApplyApprovalAsync(
        VisitTripSnapshot snapshot, VisitTrip trip, CurrentUserDto approver, CancellationToken ct)
    {
        var calc = trip.MileageCalculation
            ?? db.MileageCalculations.Local.FirstOrDefault(x => x.VisitTripId == trip.VisitTripId)
            ?? await db.MileageCalculations.AsNoTracking()
                .FirstOrDefaultAsync(x => x.VisitTripId == trip.VisitTripId, ct);
        RouteCalculationAttempt? attempt = null;
        if (calc?.SelectedRouteCalculationAttemptId is long attemptId)
        {
            attempt = await db.RouteCalculationAttempts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.RouteCalculationAttemptId == attemptId, ct)
                ?? throw new InvalidOperationException("F_B_ROUTE_ATTEMPT_NOT_FOUND：Approved Snapshot 找不到目前選取的 route attempt。");
            if (attempt.VisitTripId != trip.VisitTripId || attempt.Status != "Succeeded")
                throw new InvalidOperationException("F_B_ROUTE_ATTEMPT_INVALID：Approved Snapshot 只能投影同一行程的成功 route attempt。");
        }
        snapshot.StatusSnapshot = trip.Status;
        snapshot.SystemDistanceKmSnapshot = calc?.SystemDistanceKm;
        snapshot.ApprovedDistanceKmSnapshot = calc?.ApprovedDistanceKm;
        snapshot.RatePerKmSnapshot = calc?.RatePerKmSnapshot;
        snapshot.SubsidyAmountSnapshot = calc?.ApprovedAmount;
        snapshot.RouteProviderSnapshot = calc?.CalculationSource;
        snapshot.ApprovedAtSnapshot = trip.ApprovedAt;
        snapshot.ApproverUserId = approver.UserId;
        snapshot.ApproverNameSnapshot = approver.DisplayName;
        snapshot.MileageRouteAttemptIdSnapshot = attempt?.RouteCalculationAttemptId;
        snapshot.RouteTravelModeSnapshot = attempt?.TravelMode;
        snapshot.RouteCalculatedAtSnapshot = attempt?.CompletedAt;
        snapshot.RouteCalculationStatusSnapshot = calc?.ManualFallbackUsed == true
            ? "ManualFallback"
            : attempt?.Status;
        snapshot.RouteErrorCodeSnapshot = attempt?.ErrorCode;
        snapshot.RouteCorrelationIdSnapshot = attempt?.CorrelationId;
        snapshot.ApprovedDistanceSourceSnapshot = calc?.ApprovedDistanceSource;
        snapshot.ApprovalBasisCodeSnapshot = calc?.ApprovalBasisCode;
        snapshot.ApprovalBasisHashSnapshot = calc?.ApprovalBasisHash?.ToArray();
        snapshot.DistanceApprovedAtSnapshot = calc?.DistanceApprovedAt;
    }
}
