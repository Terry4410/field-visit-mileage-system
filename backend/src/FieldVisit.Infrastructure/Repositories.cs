using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> FindByAccountAsync(string account, CancellationToken ct) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeNo == account || x.Email == account, ct);

    public Task<User?> FindByEntraIdentityAsync(
        Guid tenantId,
        Guid objectId,
        CancellationToken ct) =>
        (
            from identity in db.UserIdentityProfiles.AsNoTracking()
            join user in db.Users.AsNoTracking()
                on identity.UserId equals user.UserId
            where
                identity.EntraTenantId == tenantId
                && identity.EntraObjectId == objectId
            select user
        ).SingleOrDefaultAsync(ct);

    public async Task<User?> BindEntraIdentityByEmailAsync(
        Guid tenantId,
        Guid objectId,
        string email,
        CancellationToken ct)
    {
        var normalizedEmail =
            email.Trim();

        var candidates =
            await db.Users
                .Where(
                    x =>
                        x.Email != null
                        && x.Email == normalizedEmail)
                .ToListAsync(ct);

        if (candidates.Count == 0)
            return null;

        if (candidates.Count > 1)
            throw new UnauthorizedAccessException(
                "此 Microsoft 帳號對應到多個系統 Email，禁止自動綁定。");

        var user =
            candidates[0];

        var identity =
            await db.UserIdentityProfiles
                .SingleOrDefaultAsync(
                    x => x.UserId == user.UserId,
                    ct)
            ?? throw new UnauthorizedAccessException(
                "此系統帳號缺少 Identity Profile。");

        if (identity.EntraTenantId.HasValue
            || identity.EntraObjectId.HasValue)
        {
            if (identity.EntraTenantId == tenantId
                && identity.EntraObjectId == objectId)
                return user;

            throw new UnauthorizedAccessException(
                "此系統帳號已綁定其他 Microsoft Entra 身分。");
        }

        identity.EntraTenantId =
            tenantId;

        identity.EntraObjectId =
            objectId;

        identity.IdentityProvider =
            "EntraId";

        identity.UpdatedAt =
            DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException(
                "Microsoft Entra 身分綁定發生衝突，請由系統管理者確認。");
        }

        return user;
    }

    public async Task<CurrentUserDto?> GetProfileAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (user is null) return null;
        var today =
            BusinessTime.Today;

        var hasV170Identity =
            await db.UserIdentityProfiles
                .AsNoTracking()
                .AnyAsync(
                    x => x.UserId == userId,
                    ct);

        List<string> roles;
        List<TeamScopeDto> scopeRows;

        if (hasV170Identity)
        {
            var roleCodes =
                await (
                    from assignment
                        in db.UserRoleAssignments
                            .AsNoTracking()
                    join role
                        in db.Roles.AsNoTracking()
                        on assignment.RoleId
                        equals role.RoleId
                    where
                        assignment.UserId == userId
                        && assignment.EffectiveFrom <= today
                        && (!assignment.EffectiveTo.HasValue
                            || assignment.EffectiveTo >= today)
                        && role.IsActive
                    select role.RoleCode)
                    .ToListAsync(ct);

            roles =
                roleCodes
                    .Select(NormalizeRole)
                    .Distinct()
                    .ToList();

            scopeRows =
                await (
                    from assignment
                        in db.UserTeamAssignments
                            .AsNoTracking()
                    join team
                        in db.Teams.AsNoTracking()
                        on assignment.TeamId
                        equals team.TeamId
                    where
                        assignment.UserId == userId
                        && assignment.EffectiveFrom <= today
                        && (!assignment.EffectiveTo.HasValue
                            || assignment.EffectiveTo >= today)
                        && team.IsActive
                    orderby
                        assignment.IsPrimary descending,
                        team.TeamName
                    select new TeamScopeDto(
                        team.TeamId,
                        team.TeamName,
                        assignment.IsPrimary))
                    .ToListAsync(ct);
        }
        else
        {
            var roleCodes =
                await (
                    from ur
                        in db.UserRoles.AsNoTracking()
                    join role
                        in db.Roles.AsNoTracking()
                        on ur.RoleId equals role.RoleId
                    where
                        ur.UserId == userId
                        && role.IsActive
                    select role.RoleCode)
                    .ToListAsync(ct);

            roles =
                roleCodes
                    .Select(NormalizeRole)
                    .Distinct()
                    .ToList();

            scopeRows =
                await (
                    from scope
                        in db.UserTeamScopes
                            .AsNoTracking()
                    join team
                        in db.Teams.AsNoTracking()
                        on scope.TeamId
                        equals team.TeamId
                    where
                        scope.UserId == userId
                        && scope.IsActive
                        && team.IsActive
                    orderby
                        scope.IsPrimary descending,
                        team.TeamName
                    select new TeamScopeDto(
                        team.TeamId,
                        team.TeamName,
                        scope.IsPrimary))
                    .ToListAsync(ct);
        }

        var primary =
            scopeRows.FirstOrDefault(
                x => x.IsPrimary)
            ?? scopeRows.FirstOrDefault();

        Team? fallbackTeam =
            null;

        if (!hasV170Identity
            && primary is null
            && user.TeamId.HasValue)
        {
            fallbackTeam =
                await db.Teams
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.TeamId
                            == user.TeamId.Value,
                        ct);
        }

        var resolvedTeamId =
            primary?.TeamId
            ?? (!hasV170Identity
                ? user.TeamId
                : null);

        var resolvedTeamName =
            primary?.TeamName
            ?? fallbackTeam?.TeamName;

        return new CurrentUserDto(
            user.UserId,
            user.EmployeeNo ?? "",
            user.DisplayName,
            user.Email,
            user.OrganizationId,
            resolvedTeamId,
            resolvedTeamName,
            roles,
            scopeRows);
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
            .Include(x => x.Stops).ThenInclude(x => x.Location).Include(x => x.MileageCalculation);

    public Task<VisitTrip?> GetAsync(long tripId, bool tracking, CancellationToken ct) => Query(tracking).FirstOrDefaultAsync(x => x.VisitTripId == tripId, ct);
    public Task AddAsync(VisitTrip trip, CancellationToken ct) => db.VisitTrips.AddAsync(trip, ct).AsTask();

    public Task<List<VisitTrip>> GetVisitorHistoryAsync(int userId, DateOnly? start, DateOnly? end, string? locationKeyword, CancellationToken ct)
    {
        var q = Query(false).Where(x => x.UserId == userId && x.Status != TripStatuses.Cancelled);
        if (start.HasValue) q = q.Where(x => x.VisitDate >= start.Value);
        if (end.HasValue) q = q.Where(x => x.VisitDate <= end.Value);
        if (!string.IsNullOrWhiteSpace(locationKeyword))
        {
            var k = locationKeyword.Trim();
            q = q.Where(x => x.Stops.Any(s => (s.LocationNameSnapshot != null && s.LocationNameSnapshot.Contains(k)) || (s.AddressSnapshot != null && s.AddressSnapshot.Contains(k))));
        }
        return q.OrderByDescending(x => x.VisitDate).ThenByDescending(x => x.VisitTripId).ToListAsync(ct);
    }

    public Task<List<VisitTrip>> GetTeamQueueAsync(IReadOnlyCollection<int> teamIds, CancellationToken ct) =>
        teamIds.Count == 0
            ? Task.FromResult(new List<VisitTrip>())
            : Query(false).Where(x => x.TeamId.HasValue && teamIds.Contains(x.TeamId.Value) &&
                (x.Status == TripStatuses.Submitted || x.Status == TripStatuses.RoutePending || x.Status == TripStatuses.RouteCalculated || x.Status == TripStatuses.PendingApproval))
                .OrderBy(x => x.VisitDate).ThenBy(x => x.VisitTripId).ToListAsync(ct);

    public Task<List<VisitTrip>> GetPendingMileageAsync(IReadOnlyCollection<int> teamIds, DateOnly? start, DateOnly? end, IReadOnlyList<long>? selected, CancellationToken ct)
    {
        if (teamIds.Count == 0) return Task.FromResult(new List<VisitTrip>());
        var q = Query(true).Where(x => x.TeamId.HasValue && teamIds.Contains(x.TeamId.Value) &&
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
        var q = Query(false).Where(x => x.Status != TripStatuses.Cancelled);
        if ((user.Roles.Contains("admin") || user.Roles.Contains("supervisor")) && user.OrganizationId.HasValue) q = q.Where(x => x.OrganizationId == user.OrganizationId.Value);
        else if (user.Roles.Contains("leader"))
        {
            var teamIds = user.TeamIds;
            q = teamIds.Count > 0 ? q.Where(x => x.TeamId.HasValue && teamIds.Contains(x.TeamId.Value)) : q.Where(x => false);
        }
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
        if (user.Roles.Contains("leader"))
        {
            var teamIds = user.TeamIds;
            q = teamIds.Count > 0 ? q.Where(x => teamIds.Contains(x.TeamId)) : q.Where(x => false);
        }
        else if (user.Roles.Contains("visitor"))
            q = user.TeamId.HasValue ? q.Where(x => x.TeamId == user.TeamId.Value) : q.Where(x => false);
        return q.OrderBy(x => x.TeamName).ToListAsync(ct);
    }

    public Task<List<Location>> GetLocationsAsync(CurrentUserDto user, bool activeOnly, CancellationToken ct)
    {
        var q = db.Locations.AsNoTracking().AsQueryable();
        if (user.OrganizationId.HasValue) q = q.Where(x => x.OrganizationId == user.OrganizationId.Value || x.OrganizationId == null);
        if (user.Roles.Contains("leader"))
        {
            var teamIds = user.TeamIds;
            q = teamIds.Count > 0 ? q.Where(x => x.TeamId == null || (x.TeamId.HasValue && teamIds.Contains(x.TeamId.Value))) : q.Where(x => false);
        }
        else if (user.Roles.Contains("visitor"))
            q = user.TeamId.HasValue ? q.Where(x => x.TeamId == user.TeamId.Value || x.TeamId == null) : q.Where(x => false);
        if (activeOnly) q = q.Where(x => x.IsActive && x.ApprovalStatus == "Approved");
        return q.OrderBy(x => x.City).ThenBy(x => x.District).ThenBy(x => x.LocationName).ToListAsync(ct);
    }

    public Task<List<Location>> GetPendingLocationsAsync(CurrentUserDto user, DateTime? start, DateTime? end, CancellationToken ct)
    {
        var q = db.Locations.AsNoTracking().Where(x =>
            (x.ApprovalStatus == "Pending" || x.GeocodingStatus == "Pending") &&
            !db.VisitTripStops.Any(s => s.LocationId == x.LocationId && s.VisitTrip.Status == TripStatuses.Cancelled));
        if (user.OrganizationId.HasValue) q = q.Where(x => x.OrganizationId == user.OrganizationId.Value || x.OrganizationId == null);
        if (user.Roles.Contains("leader"))
        {
            var teamIds = user.TeamIds;
            q = teamIds.Count > 0 ? q.Where(x => x.TeamId.HasValue && teamIds.Contains(x.TeamId.Value)) : q.Where(x => false);
        }
        if (start.HasValue) q = q.Where(x => x.CreatedAt >= start.Value);
        if (end.HasValue) q = q.Where(x => x.CreatedAt <= end.Value);
        return q.OrderBy(x => x.CreatedAt).ToListAsync(ct);
    }

    public Task<Location?> GetLocationAsync(int id, bool tracking, CancellationToken ct) =>
        (tracking ? db.Locations.AsQueryable() : db.Locations.AsNoTracking()).FirstOrDefaultAsync(x => x.LocationId == id, ct);
    public Task AddLocationAsync(Location location, CancellationToken ct) => db.Locations.AddAsync(location, ct).AsTask();
    public Task<Location?> FindReusableTemporaryLocationAsync(int? organizationId, int? teamId, string locationName, string? addressOrPlusCode, CancellationToken ct) =>
        db.Locations.FirstOrDefaultAsync(x => x.IsTemporary && x.ApprovalStatus == "Pending" && x.OrganizationId == organizationId && x.TeamId == teamId && x.LocationName == locationName && (x.Address == addressOrPlusCode || x.PlusCode == addressOrPlusCode), ct);
    public async Task AbandonUnusedTemporaryLocationsAsync(IReadOnlyCollection<int> locationIds, CancellationToken ct)
    {
        if (locationIds.Count == 0) return;
        var rows = await db.Locations.Where(x => locationIds.Contains(x.LocationId) && x.IsTemporary && x.ApprovalStatus == "Pending").ToListAsync(ct);
        foreach (var row in rows)
        {
            var stillUsed = await db.VisitTripStops.AnyAsync(s => s.LocationId == row.LocationId && s.VisitTrip.Status != TripStatuses.Cancelled, ct);
            if (!stillUsed) { row.ApprovalStatus = "Abandoned"; row.GeocodingStatus = "NotRequired"; row.IsActive = false; row.UpdatedAt = DateTime.UtcNow; }
        }
    }

    public Task<List<Project>> GetProjectsAsync(CurrentUserDto user, bool includeInactive, CancellationToken ct)
    {
        var today = BusinessTime.Today;
        var q = db.Projects.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(x => x.IsActive && (!x.StartDate.HasValue || x.StartDate <= today) && (!x.EndDate.HasValue || x.EndDate >= today));
        if (user.OrganizationId.HasValue) q = q.Where(x => x.OrganizationId == user.OrganizationId.Value);
        if (user.Roles.Contains("admin") || user.Roles.Contains("supervisor")) { }
        else if (user.Roles.Contains("leader"))
        {
            var teamIds = user.TeamIds;
            q = teamIds.Count > 0 ? q.Where(x => x.TeamId == null || (x.TeamId.HasValue && teamIds.Contains(x.TeamId.Value))) : q.Where(x => false);
        }
        else if (user.Roles.Contains("visitor")) q = user.TeamId.HasValue ? q.Where(x => x.TeamId == user.TeamId.Value || x.TeamId == null) : q.Where(x => false);
        return q.OrderBy(x => x.ProjectName).ToListAsync(ct);
    }

    public Task<Project?> GetProjectAsync(int projectId, bool tracking, CancellationToken ct) =>
        (tracking ? db.Projects.AsQueryable() : db.Projects.AsNoTracking()).FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);
    public Task AddProjectAsync(Project project, CancellationToken ct) => db.Projects.AddAsync(project, ct).AsTask();
    public Task<bool> ProjectCodeExistsAsync(int organizationId, string projectCode, int? excludeProjectId, CancellationToken ct)
    {
        var code = projectCode.Trim();
        var q = db.Projects.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.ProjectCode == code);
        if (excludeProjectId.HasValue) q = q.Where(x => x.ProjectId != excludeProjectId.Value);
        return q.AnyAsync(ct);
    }

    public async Task<List<Location>> GetProjectLocationsAsync(int projectId, CurrentUserDto user, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == projectId, ct) ?? throw new KeyNotFoundException("找不到專案。");
        if ((user.Roles.Contains("admin") || user.Roles.Contains("supervisor")) && user.OrganizationId.HasValue && project.OrganizationId != user.OrganizationId.Value)
            throw new UnauthorizedAccessException("無權使用其他 Organization 專案。");
        if (user.Roles.Contains("leader") && project.TeamId.HasValue && !user.TeamIds.Contains(project.TeamId.Value))
            throw new UnauthorizedAccessException("無權使用未授權小組專案。");
        if (user.Roles.Contains("visitor") && project.TeamId.HasValue && project.TeamId != user.TeamId)
            throw new UnauthorizedAccessException("無權使用其他小組專案。");
        return await (from pl in db.ProjectLocations.AsNoTracking()
                      join l in db.Locations.AsNoTracking() on pl.LocationId equals l.LocationId
                      where pl.ProjectId == projectId && pl.IsActive && l.IsActive
                      orderby pl.IsPrimary descending, l.LocationName
                      select l).ToListAsync(ct);
    }

    public Task<List<VisitType>> GetVisitTypesAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.VisitTypes.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(x => x.IsActive);
        return q.OrderBy(x => x.SortOrder).ThenBy(x => x.VisitTypeName).ToListAsync(ct);
    }
    public Task<VisitType?> GetVisitTypeAsync(int visitTypeId, bool tracking, CancellationToken ct) =>
        (tracking ? db.VisitTypes.AsQueryable() : db.VisitTypes.AsNoTracking()).FirstOrDefaultAsync(x => x.VisitTypeId == visitTypeId, ct);
    public Task AddVisitTypeAsync(VisitType visitType, CancellationToken ct) => db.VisitTypes.AddAsync(visitType, ct).AsTask();
    public Task<bool> VisitTypeCodeExistsAsync(string visitTypeCode, int? excludeVisitTypeId, CancellationToken ct)
    {
        var code = visitTypeCode.Trim();
        var q = db.VisitTypes.AsNoTracking().Where(x => x.VisitTypeCode == code);
        if (excludeVisitTypeId.HasValue) q = q.Where(x => x.VisitTypeId != excludeVisitTypeId.Value);
        return q.AnyAsync(ct);
    }
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
    public Task<MileageRateRule?> GetRateAsync(int mileageRateRuleId, bool tracking, CancellationToken ct) =>
        (tracking ? db.MileageRateRules.AsQueryable() : db.MileageRateRules.AsNoTracking()).FirstOrDefaultAsync(x => x.MileageRateRuleId == mileageRateRuleId, ct);
    public Task AddRateAsync(MileageRateRule rule, CancellationToken ct) => db.MileageRateRules.AddAsync(rule, ct).AsTask();
    public Task<List<MileageRateRule>> GetRateSeriesAsync(int? organizationId, string vehicleType, bool tracking, CancellationToken ct)
    {
        var q = tracking ? db.MileageRateRules.AsQueryable() : db.MileageRateRules.AsNoTracking();
        q = organizationId.HasValue ? q.Where(x => x.OrganizationId == organizationId.Value) : q.Where(x => x.OrganizationId == null);
        return q.Where(x => x.VehicleType == vehicleType).OrderBy(x => x.EffectiveFrom).ThenBy(x => x.MileageRateRuleId).ToListAsync(ct);
    }
}

public sealed class WorkflowRepository(AppDbContext db) : IWorkflowRepository
{
    public Task AddApprovalAsync(ApprovalRecord row, CancellationToken ct) => db.ApprovalRecords.AddAsync(row, ct).AsTask();
    public Task AddStatusHistoryAsync(VisitTripStatusHistory row, CancellationToken ct) => db.VisitTripStatusHistories.AddAsync(row, ct).AsTask();
    public Task AddLocationHistoryAsync(LocationApprovalHistory row, CancellationToken ct) => db.LocationApprovalHistories.AddAsync(row, ct).AsTask();
    public Task AddAuditAsync(AuditLog row, CancellationToken ct) => db.AuditLogs.AddAsync(row, ct).AsTask();
}
