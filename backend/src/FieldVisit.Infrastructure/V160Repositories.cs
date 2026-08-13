using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class TripSnapshotRepository(AppDbContext db) : ITripSnapshotRepository
{
    public async Task AddApprovedSnapshotAsync(VisitTrip trip, CurrentUserDto approver, CancellationToken ct)
    {
        var nextVersion = (await db.VisitTripSnapshots.AsNoTracking()
            .Where(x => x.VisitTripId == trip.VisitTripId)
            .MaxAsync(x => (int?)x.SnapshotVersion, ct) ?? 0) + 1;

        var visitor = await db.Users.AsNoTracking().FirstAsync(x => x.UserId == trip.UserId, ct);
        var organizationName = await db.Organizations.AsNoTracking()
            .Where(x => x.OrganizationId == trip.OrganizationId)
            .Select(x => x.OrganizationName)
            .FirstOrDefaultAsync(ct) ?? $"Organization {trip.OrganizationId}";
        var teamName = trip.TeamId.HasValue
            ? await db.Teams.AsNoTracking().Where(x => x.TeamId == trip.TeamId.Value).Select(x => x.TeamName).FirstOrDefaultAsync(ct)
            : null;

        var calc = trip.MileageCalculation ??
            await db.MileageCalculations.AsNoTracking().FirstOrDefaultAsync(x => x.VisitTripId == trip.VisitTripId, ct);

        var locationIds = trip.Stops.Where(x => x.LocationId.HasValue).Select(x => x.LocationId!.Value).Distinct().ToList();
        var projectIds = trip.Stops.Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId!.Value).Distinct().ToList();
        var visitTypeIds = trip.Stops.Where(x => x.VisitTypeId.HasValue).Select(x => x.VisitTypeId!.Value).Distinct().ToList();

        var locations = await db.Locations.AsNoTracking().Where(x => locationIds.Contains(x.LocationId))
            .ToDictionaryAsync(x => x.LocationId, ct);
        var projects = await db.Projects.AsNoTracking().Where(x => projectIds.Contains(x.ProjectId))
            .ToDictionaryAsync(x => x.ProjectId, ct);
        var visitTypes = await db.VisitTypes.AsNoTracking().Where(x => visitTypeIds.Contains(x.VisitTypeId))
            .ToDictionaryAsync(x => x.VisitTypeId, ct);

        var snapshot = new VisitTripSnapshot
        {
            VisitTripId = trip.VisitTripId,
            SnapshotVersion = nextVersion,
            SnapshotType = "Approved",
            TripNo = trip.TripNo,
            UserId = trip.UserId,
            EmployeeNoSnapshot = visitor.EmployeeNo ?? "",
            DisplayNameSnapshot = visitor.DisplayName,
            OrganizationId = trip.OrganizationId,
            OrganizationNameSnapshot = organizationName,
            TeamId = trip.TeamId,
            TeamNameSnapshot = teamName,
            VisitDate = trip.VisitDate,
            StartTime = trip.StartTime,
            EndTime = trip.EndTime,
            StatusSnapshot = trip.Status,
            VehicleTypeSnapshot = trip.VehicleType,
            ClaimedDistanceKmSnapshot = calc?.ClaimedDistanceKm,
            SystemDistanceKmSnapshot = calc?.SystemDistanceKm,
            ApprovedDistanceKmSnapshot = calc?.ApprovedDistanceKm,
            RatePerKmSnapshot = calc?.RatePerKmSnapshot,
            SubsidyAmountSnapshot = calc?.ApprovedAmount,
            RouteProviderSnapshot = calc?.CalculationSource,
            SubmittedAtSnapshot = trip.SubmittedAt,
            ApprovedAtSnapshot = trip.ApprovedAt,
            ApproverUserId = approver.UserId,
            ApproverNameSnapshot = approver.DisplayName,
            NotesSnapshot = trip.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = approver.UserId
        };

        foreach (var stop in trip.Stops.OrderBy(x => x.StopSequence))
        {
            locations.TryGetValue(stop.LocationId ?? -1, out var location);
            projects.TryGetValue(stop.ProjectId ?? -1, out var project);
            visitTypes.TryGetValue(stop.VisitTypeId ?? -1, out var visitType);

            snapshot.Stops.Add(new VisitTripSnapshotStop
            {
                StopSequence = stop.StopSequence,
                LocationId = stop.LocationId,
                LocationCodeSnapshot = location?.LocationCode,
                LocationNameSnapshot = stop.LocationNameSnapshot ?? location?.LocationName ?? "",
                AddressSnapshot = stop.AddressSnapshot ?? location?.Address ?? location?.PlusCode,
                ProjectId = stop.ProjectId,
                ProjectCodeSnapshot = project?.ProjectCode,
                ProjectNameSnapshot = project?.ProjectName,
                VisitTypeId = stop.VisitTypeId,
                VisitTypeCodeSnapshot = visitType?.VisitTypeCode,
                VisitTypeNameSnapshot = visitType?.VisitTypeName,
                VisitPurposeSnapshot = stop.VisitPurpose,
                NotesSnapshot = stop.Notes,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.VisitTripSnapshots.AddAsync(snapshot, ct);
    }
}
