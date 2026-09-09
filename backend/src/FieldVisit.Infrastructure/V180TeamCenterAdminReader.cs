using FieldVisit.Application;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180TeamCenterAdminReader(AppDbContext db) : IV180TeamCenterAdminReader
{
    public async Task<V180TeamLifecycleDetailDto?> GetTeamAsync(
        CurrentUserDto user, int teamId, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(user);
        var row = await db.Teams.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TeamId == teamId && x.OrganizationId == organizationId, ct);
        return row is null ? null : new V180TeamLifecycleDetailDto(
            row.TeamId, row.OrganizationId, row.TeamCode, row.TeamName,
            row.EffectiveFrom, row.EffectiveTo, row.IsActive, row.Notes,
            Convert.ToBase64String(row.RowVersion));
    }

    public async Task<V180CenterLifecycleDetailDto?> GetCenterAsync(
        CurrentUserDto user, int centerId, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(user);
        var row = await db.Centers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CenterId == centerId && x.OrganizationId == organizationId, ct);
        return row is null ? null : new V180CenterLifecycleDetailDto(
            row.CenterId, row.OrganizationId, row.CenterCode, row.CenterName,
            row.EffectiveFrom, row.EffectiveTo, row.IsActive, row.Notes,
            Convert.ToBase64String(row.RowVersion));
    }

    public async Task<IReadOnlyList<V180TeamCenterAssignmentAdminDto>> ListAssignmentsAsync(
        CurrentUserDto user, int teamId, bool includeHistory, DateOnly? asOf, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(user);
        var teamExists = await db.Teams.AsNoTracking()
            .AnyAsync(x => x.TeamId == teamId && x.OrganizationId == organizationId, ct);
        if (!teamExists) throw new KeyNotFoundException("找不到小組。");

        var effectiveAsOf = asOf ?? BusinessTime.Today;
        var source = db.TeamCenterAssignments.AsNoTracking().Where(x => x.TeamId == teamId);
        if (!includeHistory)
            source = source.Where(x => !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= effectiveAsOf);

        var rows = await source.OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.TeamCenterAssignmentId).ToListAsync(ct);
        var centerIds = rows.Select(x => x.CenterId).Distinct().ToArray();
        var centers = await db.Centers.AsNoTracking()
            .Where(x => centerIds.Contains(x.CenterId)).ToDictionaryAsync(x => x.CenterId, ct);

        var result = new List<V180TeamCenterAssignmentAdminDto>(rows.Count);
        foreach (var row in rows)
        {
            if (!centers.TryGetValue(row.CenterId, out var center) || center.OrganizationId != organizationId)
                throw new InvalidOperationException("TEAM_CENTER_ORGANIZATION_MISMATCH：Assignment 指向不同 Organization 的 Center。");
            result.Add(new V180TeamCenterAssignmentAdminDto(
                row.TeamCenterAssignmentId, row.TeamId, row.CenterId,
                center.CenterCode, center.CenterName, row.EffectiveFrom, row.EffectiveTo,
                row.ChangeReason, Convert.ToBase64String(row.RowVersion)));
        }
        return result;
    }

    private static int RequireAdminOrganization(CurrentUserDto user)
    {
        if (!user.Roles.Any(x => x.Equals("admin", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("只有管理者可以查詢 v1.8 Team/Center lifecycle 主檔。");
        return user.OrganizationId ?? throw new InvalidOperationException("目前帳號缺少 OrganizationId。");
    }
}
