using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public interface ICurrentUserService
{
    CurrentUserDto GetRequired();
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) Create(CurrentUserDto user);
}

public interface IUserRepository
{
    Task<User?> FindByAccountAsync(
        string account,
        CancellationToken ct);

    Task<User?> FindByEntraIdentityAsync(
        Guid tenantId,
        Guid objectId,
        CancellationToken ct);

    Task<User?> BindEntraIdentityByEmailAsync(
        Guid tenantId,
        Guid objectId,
        string email,
        CancellationToken ct);

    Task<CurrentUserDto?> GetProfileAsync(
        int userId,
        CancellationToken ct);
}

public interface ITripRepository
{
    Task<VisitTrip?> GetAsync(long tripId, bool tracking, CancellationToken ct);
    Task AddAsync(VisitTrip trip, CancellationToken ct);
    Task<List<VisitTrip>> GetVisitorHistoryAsync(int userId, DateOnly? start, DateOnly? end, string? locationKeyword, CancellationToken ct);
    Task<List<VisitTrip>> GetTeamQueueAsync(IReadOnlyCollection<int> teamIds, CancellationToken ct);
    Task<List<VisitTrip>> GetPendingMileageAsync(IReadOnlyCollection<int> teamIds, DateOnly? start, DateOnly? end, IReadOnlyList<long>? selected, CancellationToken ct);
    Task<List<VisitTrip>> FindOverlapsAsync(int userId, DateOnly date, TimeOnly start, TimeOnly end, long? excludeTripId, CancellationToken ct);
    Task<List<VisitTrip>> GetReportTripsAsync(CurrentUserDto user, DateOnly? start, DateOnly? end, CancellationToken ct);
}

public interface IMasterRepository
{
    Task<List<Team>> GetTeamsAsync(CurrentUserDto user, CancellationToken ct);
    Task<List<Location>> GetLocationsAsync(CurrentUserDto user, bool activeOnly, CancellationToken ct);
    Task<List<Location>> GetPendingLocationsAsync(CurrentUserDto user, DateTime? start, DateTime? end, CancellationToken ct);
    Task<Location?> GetLocationAsync(int id, bool tracking, CancellationToken ct);
    Task AddLocationAsync(Location location, CancellationToken ct);
    Task<Location?> FindReusableTemporaryLocationAsync(int? organizationId, int? teamId, string locationName, string? addressOrPlusCode, CancellationToken ct);
    Task AbandonUnusedTemporaryLocationsAsync(IReadOnlyCollection<int> locationIds, CancellationToken ct);
    Task<List<Project>> GetProjectsAsync(CurrentUserDto user, bool includeInactive, CancellationToken ct);
    Task<Project?> GetProjectAsync(int projectId, bool tracking, CancellationToken ct);
    Task AddProjectAsync(Project project, CancellationToken ct);
    Task<bool> ProjectCodeExistsAsync(int organizationId, string projectCode, int? excludeProjectId, CancellationToken ct);
    Task<List<Location>> GetProjectLocationsAsync(int projectId, CurrentUserDto user, CancellationToken ct);
    Task<List<VisitType>> GetVisitTypesAsync(bool includeInactive, CancellationToken ct);
    Task<VisitType?> GetVisitTypeAsync(int visitTypeId, bool tracking, CancellationToken ct);
    Task AddVisitTypeAsync(VisitType visitType, CancellationToken ct);
    Task<bool> VisitTypeCodeExistsAsync(string visitTypeCode, int? excludeVisitTypeId, CancellationToken ct);
}

public interface IMileageRepository
{
    Task<MileageCalculation?> GetByTripAsync(long tripId, bool tracking, CancellationToken ct);
    Task AddAsync(MileageCalculation row, CancellationToken ct);
    Task<MileageRateRule?> GetEffectiveRateAsync(int organizationId, string vehicleType, DateOnly date, CancellationToken ct);
    Task<List<MileageRateRule>> GetRatesAsync(CurrentUserDto user, CancellationToken ct);
    Task<MileageRateRule?> GetRateAsync(int mileageRateRuleId, bool tracking, CancellationToken ct);
    Task AddRateAsync(MileageRateRule rule, CancellationToken ct);
    Task<List<MileageRateRule>> GetRateSeriesAsync(int? organizationId, string vehicleType, bool tracking, CancellationToken ct);
    Task<(int Count, DateOnly? FirstVisitDate, DateOnly? LastVisitDate)> GetApprovedRateImpactAsync(
        int? organizationId,
        string vehicleType,
        DateOnly effectiveFrom,
        CancellationToken ct);
}

public interface ITripSnapshotRepository
{
    Task AddApprovedSnapshotAsync(VisitTrip trip, CurrentUserDto approver, CancellationToken ct);
}

public interface IWorkflowRepository
{
    Task AddApprovalAsync(ApprovalRecord row, CancellationToken ct);
    Task AddStatusHistoryAsync(VisitTripStatusHistory row, CancellationToken ct);
    Task AddLocationHistoryAsync(LocationApprovalHistory row, CancellationToken ct);
    Task AddAuditAsync(AuditLog row, CancellationToken ct);
}

public interface IRouteCalculationService
{
    Task<RouteCalculationResult> CalculateAsync(VisitTrip trip, CancellationToken ct);
}

public interface IGeocodingService
{
    Task<GeocodingResult> ResolveAsync(string? address, string? plusCode, CancellationToken ct);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
