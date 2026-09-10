using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180ManagedLocationGovernanceRepository(AppDbContext db) : IV180ManagedLocationGovernanceRepository
{
    public async Task<PagedResult<ManagedLocationDto>> SearchManagedLocationsAsync(
        CurrentUserDto user,
        ManagedLocationQueryRequest request,
        CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = request.PageSize is 20 or 50 or 100 ? request.PageSize : 50;
        var q = ApplyLegacyReadScope(db.Locations.AsNoTracking(), user);

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var keyword = request.Q.Trim();
            var pattern = $"%{keyword}%";
            var taxIdIds = await db.Database
                .SqlQuery<int>($"SELECT LocationId AS Value FROM dbo.Locations WHERE TaxId LIKE {pattern}")
                .ToListAsync(ct);
            q = q.Where(x =>
                (x.LocationCode != null && x.LocationCode.Contains(keyword))
                || x.LocationName.Contains(keyword)
                || (x.Address != null && x.Address.Contains(keyword))
                || (x.PlusCode != null && x.PlusCode.Contains(keyword))
                || taxIdIds.Contains(x.LocationId));
        }

        if (request.TeamId.HasValue) q = q.Where(x => x.TeamId == request.TeamId.Value);
        if (!string.IsNullOrWhiteSpace(request.City))
        {
            var city = request.City.Trim();
            q = q.Where(x => x.City == city);
        }
        if (!string.IsNullOrWhiteSpace(request.District))
        {
            var district = request.District.Trim();
            q = q.Where(x => x.District == district);
        }
        if (!string.IsNullOrWhiteSpace(request.GeocodingStatus))
        {
            var status = request.GeocodingStatus.Trim();
            if (status.Equals("NeedsProcessing", StringComparison.OrdinalIgnoreCase))
                q = q.Where(x => x.ApprovalStatus == "Pending" || x.GeocodingStatus == "Pending" || x.GeocodingStatus == "Failed");
            else
                q = q.Where(x => x.GeocodingStatus == status);
        }
        if (request.IsActive.HasValue) q = q.Where(x => x.IsActive == request.IsActive.Value);

        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.City).ThenBy(x => x.District).ThenBy(x => x.LocationName).ThenBy(x => x.LocationId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var teamIds = rows.Where(x => x.TeamId.HasValue).Select(x => x.TeamId!.Value).Distinct().ToList();
        var teams = await db.Teams.AsNoTracking().Where(x => teamIds.Contains(x.TeamId)).ToDictionaryAsync(x => x.TeamId, ct);
        var items = rows.Select(x => Map(x, x.TeamId.HasValue && teams.TryGetValue(x.TeamId.Value, out var team) ? team.TeamName : null)).ToList();
        return new PagedResult<ManagedLocationDto>(items, page, pageSize, total);
    }

    public async Task<ManagedLocationDto> UpdateManagedLocationAsync(
        CurrentUserDto user,
        int locationId,
        SaveManagedLocationRequest request,
        CancellationToken ct)
    {
        ValidateLegacyRequest(user, request);
        await EnsureLegacyTeamAsync(user, request.TeamId, ct);
        var row = await db.Locations.FirstOrDefaultAsync(x => x.LocationId == locationId, ct)
            ?? throw new KeyNotFoundException("找不到地點。");
        EnsureLegacyWriteScope(row, user);
        EnsureOptionalVersion(row.RowVersion, request.RowVersion);
        if (request.IsActive != row.IsActive)
            throw new InvalidOperationException("LOCATION_ACTIVE_STATE_REQUIRES_DEACTIVATION_ROUTE：一般地點修改不可變更啟用狀態。");

        row.TeamId = request.TeamId;
        row.LocationName = request.LocationName.Trim();
        row.LocationType = string.IsNullOrWhiteSpace(request.LocationType) ? row.LocationType : request.LocationType.Trim();
        row.City = NormalizeOptional(request.City);
        row.District = NormalizeOptional(request.District);
        row.Address = NormalizeOptional(request.Address);
        row.PlusCode = NormalizeOptional(request.PlusCode);
        row.GeocodingStatus = "Pending";
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        var teamName = row.TeamId.HasValue
            ? await db.Teams.AsNoTracking().Where(x => x.TeamId == row.TeamId.Value).Select(x => x.TeamName).FirstOrDefaultAsync(ct)
            : null;
        return Map(row, teamName);
    }

    public async Task<ManagedLocationGovernanceDto> UpdateGovernanceAsync(
        CurrentUserDto user,
        int locationId,
        ManagedLocationGovernanceRequest request,
        CancellationToken ct)
    {
        var expected = V180LocationGovernanceRules.RequireRowVersion(request.RowVersion);
        var normalized = V180LocationGovernanceRules.Normalize(locationId, request);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var orgId = await ResolveAuthoritativeAdminOrganizationAsync(user, ct);
            var row = await RequireGovernableLocationAsync(locationId, orgId, ct);
            EnsureVersion(row.RowVersion, expected);

            if (normalized.DuplicateOfLocationId.HasValue)
            {
                var duplicate = await db.Locations.AsNoTracking().FirstOrDefaultAsync(
                    x => x.LocationId == normalized.DuplicateOfLocationId.Value, ct)
                    ?? throw new InvalidOperationException("LOCATION_DUPLICATE_TARGET_NOT_FOUND：找不到重複參照目標。");
                if (!duplicate.OrganizationId.HasValue || duplicate.OrganizationId.Value != orgId)
                    throw new UnauthorizedAccessException("LOCATION_DUPLICATE_TARGET_SCOPE：重複參照目標必須屬於相同 Organization。");
            }

            var affected = await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE dbo.Locations
SET TaxId={normalized.TaxId}, MasterNote={normalized.MasterNote},
    DuplicateOfLocationId={normalized.DuplicateOfLocationId}, DuplicateReason={normalized.DuplicateReason}
WHERE LocationId={locationId} AND RowVersion={expected};", ct);
            if (affected != 1) throw new DbUpdateConcurrencyException("ROWVERSION_CONFLICT：資料已被其他人更新，請重新整理後再試。");

            await db.Entry(row).ReloadAsync(ct);
            await tx.CommitAsync(ct);
            return new ManagedLocationGovernanceDto(
                locationId,
                normalized.TaxId,
                normalized.MasterNote,
                normalized.DuplicateOfLocationId,
                normalized.DuplicateReason,
                Convert.ToBase64String(row.RowVersion));
        });
    }

    public async Task DeactivateManagedLocationAsync(CurrentUserDto user, int locationId, string rowVersion, CancellationToken ct)
    {
        var expected = V180LocationGovernanceRules.RequireRowVersion(rowVersion);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var orgId = await ResolveAuthoritativeAdminOrganizationAsync(user, ct);
            var row = await RequireGovernableLocationAsync(locationId, orgId, ct);
            EnsureVersion(row.RowVersion, expected);

            if (!row.IsActive)
            {
                await tx.CommitAsync(ct);
                return;
            }

            var today = BusinessTime.Today;
            var blocked = await db.DeploymentSiteLocationAssignments.AsNoTracking().AnyAsync(x =>
                x.LocationId == locationId && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today), ct);
            if (blocked)
                throw new InvalidOperationException("LOCATION_INACTIVATION_BLOCKED：地點仍有今日或未來的部署站點指派。");

            try
            {
                var affected = await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE dbo.Locations
SET IsActive=0, InactivatedAt=SYSUTCDATETIME(), InactivatedByUserId={user.UserId}
WHERE LocationId={locationId} AND RowVersion={expected} AND IsActive=1;", ct);
                if (affected != 1) throw new DbUpdateConcurrencyException("ROWVERSION_CONFLICT：資料已被其他人更新，請重新整理後再試。");
                await tx.CommitAsync(ct);
            }
            catch (SqlException ex) when (ex.Number is 53605 or 53606)
            {
                await tx.RollbackAsync(ct);
                throw new InvalidOperationException("LOCATION_INACTIVATION_BLOCKED：資料庫拒絕停用地點，因為目前仍有部署站點指派或 invariant lock 無法取得。", ex);
            }
        });
    }

    private async Task<int> ResolveAuthoritativeAdminOrganizationAsync(CurrentUserDto user, CancellationToken ct)
    {
        if (!user.Roles.Any(x => x.Equals("admin", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("LOCATION_GOVERNANCE_ADMIN_ONLY：只有 Admin 可執行地點治理。");
        var today = BusinessTime.Today;
        var profile = await db.UserIdentityProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.UserId, ct);
        if (profile?.EmploymentId is not long employmentId || !profile.UserType.Equals(UserTypes.Internal, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("LOCATION_GOVERNANCE_IDENTITY_REQUIRED：找不到有效的內部 Employment identity。");
        var employment = await db.Employments.AsNoTracking().SingleOrDefaultAsync(x => x.EmploymentId == employmentId, ct)
            ?? throw new UnauthorizedAccessException("LOCATION_GOVERNANCE_EMPLOYMENT_REQUIRED：找不到有效 Employment。");
        var active = await db.EmploymentStatusPeriods.AsNoTracking().AnyAsync(x =>
            x.EmploymentId == employmentId && x.EmploymentStatus == EmploymentStatuses.Active
            && x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today), ct);
        if (!active) throw new UnauthorizedAccessException("LOCATION_GOVERNANCE_EMPLOYMENT_INACTIVE：目前 Employment 非有效在職狀態。");
        var authoritativeAdmin = await (
            from a in db.EmploymentRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.RoleId
            where a.EmploymentId == employmentId && r.IsActive && r.RoleCode == "admin"
                && a.EffectiveFrom <= today && (!a.EffectiveTo.HasValue || a.EffectiveTo.Value >= today)
            select a.EmploymentRoleAssignmentId).AnyAsync(ct);
        if (!authoritativeAdmin)
            throw new UnauthorizedAccessException("LOCATION_GOVERNANCE_ADMIN_AUTHORITY_REQUIRED：目前沒有有效 Admin authority。");
        return employment.OrganizationId;
    }

    private async Task<Location> RequireGovernableLocationAsync(int locationId, int orgId, CancellationToken ct)
    {
        var row = await db.Locations.FirstOrDefaultAsync(x => x.LocationId == locationId, ct)
            ?? throw new KeyNotFoundException("找不到地點。");
        if (!row.OrganizationId.HasValue)
            throw new UnauthorizedAccessException("LOCATION_GOVERNANCE_SHARED_DENIED：Organization Admin 不可治理 shared/global 地點。");
        if (row.OrganizationId.Value != orgId)
            throw new UnauthorizedAccessException("LOCATION_GOVERNANCE_CROSS_ORG_DENIED：不可治理其他 Organization 地點。");
        return row;
    }

    private IQueryable<Location> ApplyLegacyReadScope(IQueryable<Location> q, CurrentUserDto user)
    {
        if (user.OrganizationId.HasValue) q = q.Where(x => x.OrganizationId == user.OrganizationId.Value || x.OrganizationId == null);
        if (HasRole(user, "admin") || HasRole(user, "supervisor")) return q;
        if (HasRole(user, "leader"))
        {
            var teamIds = user.TeamIds;
            return teamIds.Count == 0 ? q.Where(x => false) : q.Where(x => x.TeamId.HasValue && teamIds.Contains(x.TeamId.Value));
        }
        if (HasRole(user, "visitor")) return q.Where(x => x.TeamId == user.TeamId || x.TeamId == null);
        return q;
    }

    private async Task EnsureLegacyTeamAsync(CurrentUserDto user, int? teamId, CancellationToken ct)
    {
        if (!teamId.HasValue) return;
        if (!user.OrganizationId.HasValue) throw new UnauthorizedAccessException("目前帳號未設定 Organization。");
        var valid = await db.Teams.AsNoTracking().AnyAsync(x => x.TeamId == teamId.Value && x.OrganizationId == user.OrganizationId.Value && x.IsActive, ct);
        if (!valid) throw new InvalidOperationException("所選小組不存在、已停用或不屬於目前 Organization。");
    }

    private static void ValidateLegacyRequest(CurrentUserDto user, SaveManagedLocationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LocationName)) throw new InvalidOperationException("地點名稱必填。");
        if (string.IsNullOrWhiteSpace(request.Address) && string.IsNullOrWhiteSpace(request.PlusCode)) throw new InvalidOperationException("地址與 Plus Code 至少需要一項。");
        if (HasRole(user, "leader") && (!request.TeamId.HasValue || !user.TeamIds.Contains(request.TeamId.Value)))
            throw new UnauthorizedAccessException("小組長只能維護授權小組地點。");
    }

    private static void EnsureLegacyWriteScope(Location row, CurrentUserDto user)
    {
        if (user.OrganizationId.HasValue && row.OrganizationId.HasValue && row.OrganizationId != user.OrganizationId)
            throw new UnauthorizedAccessException("無權維護其他 Organization 地點。");
        if (HasRole(user, "leader") && (!row.TeamId.HasValue || !user.TeamIds.Contains(row.TeamId.Value)))
            throw new UnauthorizedAccessException("無權維護未授權小組地點。");
    }

    private static void EnsureOptionalVersion(byte[] current, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) return;
        var parsed = V180LocationGovernanceRules.RequireRowVersion(expected);
        EnsureVersion(current, parsed);
    }

    private static void EnsureVersion(byte[] current, byte[] expected)
    {
        if (!current.SequenceEqual(expected))
            throw new DbUpdateConcurrencyException("ROWVERSION_CONFLICT：資料已被其他人更新，請重新整理後再試。");
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool HasRole(CurrentUserDto user, string role) => user.Roles.Any(x => x.Equals(role, StringComparison.OrdinalIgnoreCase));

    private static ManagedLocationDto Map(Location x, string? teamName) => new(
        x.LocationId, x.LocationCode ?? "", x.TeamId, teamName, x.LocationName, x.LocationType,
        x.City, x.District, x.Address, x.PlusCode, x.Latitude, x.Longitude, x.IsTemporary,
        x.ApprovalStatus, x.GeocodingStatus, x.IsActive, x.CreatedAt, Convert.ToBase64String(x.RowVersion));
}
