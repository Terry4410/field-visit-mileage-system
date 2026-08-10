using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> FindByAccountAsync(string account, CancellationToken ct) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeNo == account || x.Email == account, ct);

    public async Task<CurrentUserDto?> GetProfileAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (user is null) return null;
        var roles = await (from ur in db.UserRoles.AsNoTracking()
                           join r in db.Roles.AsNoTracking() on ur.RoleId equals r.RoleId
                           where ur.UserId == userId && r.IsActive
                           select r.RoleCode).ToListAsync(ct);
        var team = user.TeamId.HasValue ? await db.Teams.AsNoTracking().FirstOrDefaultAsync(x => x.TeamId == user.TeamId.Value, ct) : null;
        return new CurrentUserDto(user.UserId, user.EmployeeNo, user.DisplayName, user.Email, user.OrganizationId, user.TeamId, team?.TeamName,
            roles.Select(NormalizeRole).Distinct().ToList());
    }

    private static string NormalizeRole(string role) => role.Trim().ToLowerInvariant() switch
    {
        "visitor" => "visitor",
        "leader" => "leader",
        "admin" => "admin",
        "supervisor" => "supervisor",
        "government" => "supervisor",
        var x => x
    };
}

public sealed class TripRepository(AppDbContext db) : ITripRepository
{
    private IQueryable<VisitTrip> Query(bool tracking) =>
        (tracking ? db.VisitTrips.AsQueryable() : db.VisitTrips.AsNoTracking())
            .Include(x => x.Stops).Include(x => x.MileageCalculation);

    public Task<VisitTrip?> GetAsync(long tripId, bool tracking, CancellationToken ct) => Query(tracking).FirstOrDefaultAsync(x => x.VisitTripId == tripId, ct);
    public Task AddAsync(VisitTrip trip, CancellationToken ct) => db.VisitTrips.AddAsync(trip, ct).AsTask();

    public Task<List<VisitTrip>> GetVisitorHistoryAsync(int userId, DateOnly? start, DateOnly? end, string? locationKeyword, CancellationToken ct)
    {
        var q = Query(false).Where(x => x.UserId == userId);
        if (start.HasValue) q = q.Where(x => x.VisitDate >= start.Value);
        if (end.HasValue) q = q.Where(x => x.VisitDate <= end.Value);
        if (!string.IsNullOrWhiteSpace(locationKeyword))
        {
            var k = locationKeyword.Trim();
            q = q.Where(x => x.Stops.Any(s => (s.LocationNameSnapshot != null && s.LocationNameSnapshot.Contains(k)) || (s.AddressSnapshot != null && s.AddressSnapshot.Contains(k))));
        }
        return q.OrderByDescending(x => x.VisitDate).ThenByDescending(x => x.VisitTripId).ToListAsync(ct);
    }

    public Task<List<VisitTrip>> GetTeamQueueAsync(int teamId, CancellationToken ct) =>
        Query(false).Where(x => x.TeamId == teamId &&
            (x.Status == TripStatuses.Submitted || x.Status == TripStatuses.RoutePending || x.Status == TripStatuses.RouteCalculated || x.Status == TripStatuses.PendingApproval))
            .OrderBy(x => x.VisitDate).ThenBy(x => x.VisitTripId).ToListAsync(ct);

    public Task<List<VisitTrip>> GetPendingMileageAsync(int teamId, DateOnly? start, DateOnly? end, IReadOnlyList<long>? selected, CancellationToken ct)
    {
        var q = Query(true).Where(x => x.TeamId == teamId &&
            (x.Status == TripStatuses.Submitted || x.Status == TripStatuses.RoutePending) &&
            (x.MileageCalculation == null || x.MileageCalculation.SystemDistanceKm == null));
        if (start.HasValue) q = q.Where(x => x.VisitDate >= start.Value);
        if (end.HasValue) q = q.Where(x => x.VisitDate <= end.Value);
        if (selected is { Count: > 0 }) q = q.Where(x => selected.Contains(x.VisitTripId));
        return q.OrderBy(x => x.VisitDate).ThenBy(x => x.VisitTripId).ToListAsync(ct);
    }

    public Task<List<VisitTrip>> FindOverlapsAsync(int userId, DateOnly date, TimeOnly start, TimeOnly end, long? excludeTripId, CancellationToken ct)
    {
        var q = db.VisitTrips.AsNoTracking().Where(x => x.UserId == userId && x.VisitDate == date && x.Status != TripStatuses.Cancelled &&
            x.StartTime != null && x.EndTime != null && x.StartTime < end && x.EndTime > start);
        if (excludeTripId.HasValue) q = q.Where(x => x.VisitTripId != excludeTripId.Value);
        return q.ToListAsync(ct);
    }

    public Task<List<VisitTrip>> GetReportTripsAsync(CurrentUserDto user, DateOnly? start, DateOnly? end, CancellationToken ct)
    {
        var q = Query(false);
        if ((user.Roles.Contains("admin") || user.Roles.Contains("supervisor")) && user.OrganizationId.HasValue) q = q.Where(x => x.OrganizationId == user.OrganizationId.Value);
        else if (user.Roles.Contains("leader") && user.TeamId.HasValue) q = q.Where(x => x.TeamId == user.TeamId.Value);
        else if (user.Roles.Contains("visitor")) q = q.Where(x => x.UserId == user.UserId);
        else q = q.Where(x => false);
        if (start.HasValue) q = q.Where(x => x.VisitDate >= start.Value);
        if (end.HasValue) q = q.Where(x => x.VisitDate <= end.Value);
        return q.OrderByDescending(x => x.VisitDate).ToListAsync(ct);
    }
}

public sealed class MasterRepository(AppDbContext db) : IMasterRepository
{
    public Task<List<Team>> GetTeamsAsync(CurrentUserDto user, CancellationToken ct)
    {
        var q = db.Teams.AsNoTracking().Where(x => x.IsActive);
        if (user.OrganizationId.HasValue) q = q.Where(x => x.OrganizationId == user.OrganizationId.Value);
        if (user.Roles.Contains("visitor") || user.Roles.Contains("leader"))
            q = user.TeamId.HasValue ? q.Where(x => x.TeamId == user.TeamId.Value) : q.Where(x => false);
        return q.OrderBy(x => x.TeamName).ToListAsync(ct);
    }

    public Task<List<Location>> GetLocationsAsync(CurrentUserDto user, bool activeOnly, CancellationToken ct)
    {
        var q = db.Locations.AsNoTracking().AsQueryable();
        if (user.OrganizationId.HasValue) q = q.Where(x => x.OrganizationId == user.OrganizationId.Value || x.OrganizationId == null);
        if (user.Roles.Contains("visitor") || user.Roles.Contains("leader"))
            q = user.TeamId.HasValue ? q.Where(x => x.TeamId == user.TeamId.Value || x.TeamId == null) : q.Where(x => false);
        if (activeOnly) q = q.Where(x => x.IsActive && x.ApprovalStatus == "Approved");
        return q.OrderBy(x => x.City).ThenBy(x => x.District).ThenBy(x => x.LocationName).ToListAsync(ct);
    }

    public Task<List<Location>> GetPendingLocationsAsync(CurrentUserDto user, DateTime? start, DateTime? end, CancellationToken ct)
    {
        var q = db.Locations.AsNoTracking().Where(x => x.ApprovalStatus == "Pending" || x.GeocodingStatus == "Pending");
        if (user.OrganizationId.HasValue) q = q.Where(x => x.OrganizationId == user.OrganizationId.Value || x.OrganizationId == null);
        if (user.Roles.Contains("leader")) q = user.TeamId.HasValue ? q.Where(x => x.TeamId == user.TeamId.Value) : q.Where(x => false);
        if (start.HasValue) q = q.Where(x => x.CreatedAt >= start.Value);
        if (end.HasValue) q = q.Where(x => x.CreatedAt <= end.Value);
        return q.OrderBy(x => x.CreatedAt).ToListAsync(ct);
    }

    public Task<Location?> GetLocationAsync(int id, bool tracking, CancellationToken ct) =>
        (tracking ? db.Locations.AsQueryable() : db.Locations.AsNoTracking()).FirstOrDefaultAsync(x => x.LocationId == id, ct);
    public Task AddLocationAsync(Location location, CancellationToken ct) => db.Locations.AddAsync(location, ct).AsTask();

    public Task<List<Project>> GetProjectsAsync(CurrentUserDto user, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var q = db.Projects.AsNoTracking().Where(x => x.IsActive && (!x.StartDate.HasValue || x.StartDate <= today) && (!x.EndDate.HasValue || x.EndDate >= today));
        if (user.OrganizationId.HasValue) q = q.Where(x => x.OrganizationId == user.OrganizationId.Value);
        if (user.Roles.Contains("visitor") || user.Roles.Contains("leader")) q = user.TeamId.HasValue ? q.Where(x => x.TeamId == user.TeamId.Value || x.TeamId == null) : q.Where(x => false);
        return q.OrderBy(x => x.ProjectName).ToListAsync(ct);
    }

    public async Task<List<Location>> GetProjectLocationsAsync(int projectId, CurrentUserDto user, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == projectId, ct) ?? throw new KeyNotFoundException("找不到專案。");
        if ((user.Roles.Contains("visitor") || user.Roles.Contains("leader")) && project.TeamId.HasValue && project.TeamId != user.TeamId)
            throw new UnauthorizedAccessException("無權使用其他小組專案。");
        return await (from pl in db.ProjectLocations.AsNoTracking()
                      join l in db.Locations.AsNoTracking() on pl.LocationId equals l.LocationId
                      where pl.ProjectId == projectId && pl.IsActive && l.IsActive
                      orderby pl.IsPrimary descending, l.LocationName
                      select l).ToListAsync(ct);
    }

    public Task<List<VisitType>> GetVisitTypesAsync(CancellationToken ct) => db.VisitTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.VisitTypeName).ToListAsync(ct);
}

public sealed class MileageRepository(AppDbContext db) : IMileageRepository
{
    public Task<MileageCalculation?> GetByTripAsync(long tripId, bool tracking, CancellationToken ct) =>
        (tracking ? db.MileageCalculations.AsQueryable() : db.MileageCalculations.AsNoTracking()).FirstOrDefaultAsync(x => x.VisitTripId == tripId, ct);
    public Task AddAsync(MileageCalculation row, CancellationToken ct) => db.MileageCalculations.AddAsync(row, ct).AsTask();

    public Task<MileageRateRule?> GetEffectiveRateAsync(int organizationId, string vehicleType, DateOnly date, CancellationToken ct) =>
        db.MileageRateRules.AsNoTracking().Where(x => x.IsActive && (x.OrganizationId == organizationId || x.OrganizationId == null) &&
            x.VehicleType == vehicleType && x.EffectiveFrom <= date && (!x.EffectiveTo.HasValue || x.EffectiveTo >= date))
            .OrderByDescending(x => x.OrganizationId.HasValue).ThenByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);

    public Task<List<MileageRateRule>> GetRatesAsync(CurrentUserDto user, CancellationToken ct)
    {
        var q = db.MileageRateRules.AsNoTracking().AsQueryable();
        if (user.OrganizationId.HasValue) q = q.Where(x => x.OrganizationId == user.OrganizationId.Value || x.OrganizationId == null);
        return q.OrderByDescending(x => x.EffectiveFrom).ToListAsync(ct);
    }
    public Task AddRateAsync(MileageRateRule rule, CancellationToken ct) => db.MileageRateRules.AddAsync(rule, ct).AsTask();
}

public sealed class WorkflowRepository(AppDbContext db) : IWorkflowRepository
{
    public Task AddApprovalAsync(ApprovalRecord row, CancellationToken ct) => db.ApprovalRecords.AddAsync(row, ct).AsTask();
    public Task AddStatusHistoryAsync(VisitTripStatusHistory row, CancellationToken ct) => db.VisitTripStatusHistories.AddAsync(row, ct).AsTask();
    public Task AddLocationHistoryAsync(LocationApprovalHistory row, CancellationToken ct) => db.LocationApprovalHistories.AddAsync(row, ct).AsTask();
    public Task AddAuditAsync(AuditLog row, CancellationToken ct) => db.AuditLogs.AddAsync(row, ct).AsTask();
}
