using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class TripSnapshotRepository(AppDbContext db) : ITripSnapshotRepository
{
    public Task<VisitTripSnapshot?> GetLatestAsync(long tripId, string snapshotType, CancellationToken ct) =>
        db.VisitTripSnapshots.AsNoTracking().Include(x => x.Stops)
            .Where(x => x.VisitTripId == tripId && x.SnapshotType == snapshotType)
            .OrderByDescending(x => x.SnapshotVersion).FirstOrDefaultAsync(ct);

    public async Task AddSubmittedSnapshotAsync(
        VisitTrip trip, CurrentUserDto submitter, V180TripContextDto context, CancellationToken ct)
    {
        if (!trip.EmploymentId.HasValue || trip.EmploymentId.Value != context.EmploymentId)
            throw new InvalidOperationException("TRIP_CONTEXT_EMPLOYMENT_CHANGED：無法建立 Submitted Snapshot。");
        var snapshot = await BuildCurrentSnapshotAsync(trip, context, "Submitted", submitter.UserId, ct);
        await db.VisitTripSnapshots.AddAsync(snapshot, ct);
    }

    public async Task AddApprovedSnapshotAsync(VisitTrip trip, CurrentUserDto approver, CancellationToken ct)
    {
        VisitTripSnapshot snapshot;
        if (trip.EmploymentId.HasValue)
        {
            var submitted = await GetLatestAsync(trip.VisitTripId, "Submitted", ct)
                ?? throw new InvalidOperationException("SUBMITTED_SNAPSHOT_REQUIRED：v1.8 行程缺少 Submitted Snapshot，禁止以目前主檔重建歷史。");
            snapshot = CopySnapshot(submitted, await NextVersionAsync(trip.VisitTripId, ct), "Approved", approver.UserId);
            ApplyApproval(snapshot, trip, approver);
        }
        else snapshot = await BuildLegacyApprovedSnapshotAsync(trip, approver, ct);
        await db.VisitTripSnapshots.AddAsync(snapshot, ct);
    }

    private async Task<VisitTripSnapshot> BuildCurrentSnapshotAsync(
        VisitTrip trip, V180TripContextDto context, string type, int actorUserId, CancellationToken ct)
    {
        var employment = await db.Employments.AsNoTracking().SingleAsync(x => x.EmploymentId == context.EmploymentId, ct);
        var person = await db.Persons.AsNoTracking().SingleAsync(x => x.PersonId == employment.PersonId, ct);
        var organizationName = await db.Organizations.AsNoTracking().Where(x => x.OrganizationId == trip.OrganizationId)
            .Select(x => x.OrganizationName).SingleAsync(ct);
        var team = context.Teams.Single(x => x.TeamId == context.SelectedTeamId);
        var start = context.EligibleDeploymentSites.Single(x => x.DeploymentSiteId == trip.StartDeploymentSiteId);
        var end = context.EligibleDeploymentSites.Single(x => x.DeploymentSiteId == trip.EndDeploymentSiteId);
        var calc = trip.MileageCalculation ?? await db.MileageCalculations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.VisitTripId == trip.VisitTripId, ct);
        var snapshot = new VisitTripSnapshot
        {
            VisitTripId = trip.VisitTripId, SnapshotVersion = await NextVersionAsync(trip.VisitTripId, ct), SnapshotType = type,
            TripNo = trip.TripNo, UserId = trip.UserId, PersonIdSnapshot = person.PersonId,
            EmploymentIdSnapshot = employment.EmploymentId, EmployeeNoSnapshot = employment.EmployeeNo ?? "",
            DisplayNameSnapshot = person.DisplayName, OrganizationId = trip.OrganizationId,
            OrganizationNameSnapshot = organizationName, TeamId = trip.TeamId, TeamCodeSnapshot = team.Code,
            TeamNameSnapshot = team.Name, CenterIdSnapshot = start.CenterId, CenterCodeSnapshot = start.CenterCode,
            CenterNameSnapshot = start.CenterName, StartDeploymentSiteIdSnapshot = start.DeploymentSiteId,
            StartDeploymentSiteCodeSnapshot = start.SiteCode, StartDeploymentSiteNameSnapshot = start.SiteName,
            StartDeploymentLocationIdSnapshot = start.LocationId, StartDeploymentAddressSnapshot = start.Address,
            EndDeploymentSiteIdSnapshot = end.DeploymentSiteId, EndDeploymentSiteCodeSnapshot = end.SiteCode,
            EndDeploymentSiteNameSnapshot = end.SiteName, EndDeploymentLocationIdSnapshot = end.LocationId,
            EndDeploymentAddressSnapshot = end.Address, VisitDate = trip.VisitDate, StartTime = trip.StartTime,
            EndTime = trip.EndTime, StatusSnapshot = trip.Status, VehicleTypeSnapshot = trip.VehicleType,
            ClaimedDistanceKmSnapshot = calc?.ClaimedDistanceKm, SubmittedAtSnapshot = trip.SubmittedAt,
            NotesSnapshot = trip.Notes, CreatedAt = DateTime.UtcNow, CreatedByUserId = actorUserId
        };
        await AddCurrentStopsAsync(snapshot, trip, ct);
        return snapshot;
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
            VisitTripId = source.VisitTripId, SnapshotVersion = version, SnapshotType = type, TripNo = source.TripNo,
            UserId = source.UserId, PersonIdSnapshot = source.PersonIdSnapshot, EmploymentIdSnapshot = source.EmploymentIdSnapshot,
            EmployeeNoSnapshot = source.EmployeeNoSnapshot, DisplayNameSnapshot = source.DisplayNameSnapshot,
            OrganizationId = source.OrganizationId, OrganizationNameSnapshot = source.OrganizationNameSnapshot,
            TeamId = source.TeamId, TeamCodeSnapshot = source.TeamCodeSnapshot, TeamNameSnapshot = source.TeamNameSnapshot,
            CenterIdSnapshot = source.CenterIdSnapshot, CenterCodeSnapshot = source.CenterCodeSnapshot, CenterNameSnapshot = source.CenterNameSnapshot,
            StartDeploymentSiteIdSnapshot = source.StartDeploymentSiteIdSnapshot, StartDeploymentSiteCodeSnapshot = source.StartDeploymentSiteCodeSnapshot,
            StartDeploymentSiteNameSnapshot = source.StartDeploymentSiteNameSnapshot, StartDeploymentLocationIdSnapshot = source.StartDeploymentLocationIdSnapshot,
            StartDeploymentAddressSnapshot = source.StartDeploymentAddressSnapshot, EndDeploymentSiteIdSnapshot = source.EndDeploymentSiteIdSnapshot,
            EndDeploymentSiteCodeSnapshot = source.EndDeploymentSiteCodeSnapshot, EndDeploymentSiteNameSnapshot = source.EndDeploymentSiteNameSnapshot,
            EndDeploymentLocationIdSnapshot = source.EndDeploymentLocationIdSnapshot, EndDeploymentAddressSnapshot = source.EndDeploymentAddressSnapshot,
            VisitDate = source.VisitDate, StartTime = source.StartTime, EndTime = source.EndTime,
            StatusSnapshot = source.StatusSnapshot, VehicleTypeSnapshot = source.VehicleTypeSnapshot,
            ClaimedDistanceKmSnapshot = source.ClaimedDistanceKmSnapshot, SubmittedAtSnapshot = source.SubmittedAtSnapshot,
            NotesSnapshot = source.NotesSnapshot, CreatedAt = DateTime.UtcNow, CreatedByUserId = actorUserId
        };
        foreach (var stop in source.Stops.OrderBy(x => x.StopSequence))
            copy.Stops.Add(new VisitTripSnapshotStop
            {
                StopSequence = stop.StopSequence, LocationId = stop.LocationId, LocationCodeSnapshot = stop.LocationCodeSnapshot,
                LocationNameSnapshot = stop.LocationNameSnapshot, AddressSnapshot = stop.AddressSnapshot, ProjectId = stop.ProjectId,
                ProjectCodeSnapshot = stop.ProjectCodeSnapshot, ProjectNameSnapshot = stop.ProjectNameSnapshot,
                VisitTypeId = stop.VisitTypeId, VisitTypeCodeSnapshot = stop.VisitTypeCodeSnapshot,
                VisitTypeNameSnapshot = stop.VisitTypeNameSnapshot, VisitPurposeSnapshot = stop.VisitPurposeSnapshot,
                NotesSnapshot = stop.NotesSnapshot, CreatedAt = DateTime.UtcNow
            });
        return copy;
    }

    private static void ApplyApproval(VisitTripSnapshot snapshot, VisitTrip trip, CurrentUserDto approver)
    {
        var calc = trip.MileageCalculation;
        snapshot.StatusSnapshot = trip.Status;
        snapshot.SystemDistanceKmSnapshot = calc?.SystemDistanceKm;
        snapshot.ApprovedDistanceKmSnapshot = calc?.ApprovedDistanceKm;
        snapshot.RatePerKmSnapshot = calc?.RatePerKmSnapshot;
        snapshot.SubsidyAmountSnapshot = calc?.ApprovedAmount;
        snapshot.RouteProviderSnapshot = calc?.CalculationSource;
        snapshot.ApprovedAtSnapshot = trip.ApprovedAt;
        snapshot.ApproverUserId = approver.UserId;
        snapshot.ApproverNameSnapshot = approver.DisplayName;
    }
}
