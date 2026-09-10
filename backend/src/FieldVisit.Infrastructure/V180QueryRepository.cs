using System.Data;
using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed partial class V160FinalRepository
{
    private static int RequireQueryAdmin(CurrentUserDto user)
    {
        if (!HasRole(user, "admin")) throw new UnauthorizedAccessException("只有管理者可以查詢管理主檔。");
        return RequireOrganization(user);
    }

    public async Task<PagedResult<AdminUserAccessDto>> SearchUsersAsync(CurrentUserDto user, V180SearchRequest input, CancellationToken ct)
    {
        var org = RequireQueryAdmin(user);
        var r = V180QueryRules.Normalize(input);
        var q = db.Users.AsNoTracking().Where(x => x.OrganizationId == org);
        if (r.Keyword is { } k) q = q.Where(x => (x.EmployeeNo != null && x.EmployeeNo.Contains(k)) ||
            x.DisplayName.Contains(k) || (x.Email != null && x.Email.Contains(k)));
        if (r.IsActive.HasValue) q = q.Where(x => x.IsActive == r.IsActive.Value);
        if (r.Role is { } role) q = q.Where(x => db.UserRoles.Any(ur => ur.UserId == x.UserId &&
            db.Roles.Any(rr => rr.RoleId == ur.RoleId && rr.RoleCode.ToLower() == role)));
        var count = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.EmployeeNo).ThenBy(x => x.UserId)
            .Skip((r.Page - 1) * r.PageSize).Take(r.PageSize).ToListAsync(ct);
        return new(await MapUsersAsync(rows, ct), r.Page, r.PageSize, count);
    }

    public async Task<PagedResult<V180TeamRow>> SearchTeamsAsync(CurrentUserDto user, V180SearchRequest input, CancellationToken ct)
    {
        var org = RequireQueryAdmin(user);
        var r = V180QueryRules.Normalize(input);
        // Team lifecycle dates do not exist until Epic B. Never silently
        // interpret creation timestamps as effective dates.
        if (r.StartDate.HasValue || r.EndDate.HasValue)
            throw new InvalidOperationException("小組生效期間查詢須待 lifecycle Migration 完成。");
        var q = db.Teams.AsNoTracking().Where(x => x.OrganizationId == org);
        if (r.Keyword is { } k) q = q.Where(x => x.TeamCode.Contains(k) || x.TeamName.Contains(k));
        if (r.IsActive.HasValue) q = q.Where(x => x.IsActive == r.IsActive.Value);
        var count = await q.CountAsync(ct);
        var today = BusinessTime.Today;
        var rows = await q.OrderBy(x => x.TeamCode).ThenBy(x => x.TeamId)
            .Skip((r.Page - 1) * r.PageSize).Take(r.PageSize)
            .Select(x => new V180TeamRow(x.TeamId, x.OrganizationId, x.TeamCode, x.TeamName, x.IsActive,
                db.UserTeamAssignments.Where(a => a.TeamId == x.TeamId && a.EffectiveFrom <= today &&
                    (!a.EffectiveTo.HasValue || a.EffectiveTo >= today)).Select(a => a.UserId).Distinct().Count()))
            .ToListAsync(ct);
        return new(rows, r.Page, r.PageSize, count);
    }

    public async Task<PagedResult<V180ProjectRow>> SearchProjectsAsync(CurrentUserDto user, V180SearchRequest input, CancellationToken ct)
    {
        var org = RequireQueryAdmin(user);
        var r = V180QueryRules.Normalize(input);
        var q = db.Projects.AsNoTracking().Where(x => x.OrganizationId == org);
        if (r.Keyword is { } k) q = q.Where(x => x.ProjectCode.Contains(k) || x.ProjectName.Contains(k));
        if (r.TeamId.HasValue) q = q.Where(x => x.TeamId == r.TeamId.Value);
        if (r.StartDate.HasValue) q = q.Where(x => !x.EndDate.HasValue || x.EndDate >= r.StartDate);
        if (r.EndDate.HasValue) q = q.Where(x => !x.StartDate.HasValue || x.StartDate <= r.EndDate);
        var today = BusinessTime.Today;
        q = r.Status switch {
            null => q,
            "NotStarted" => q.Where(x => x.IsActive && x.StartDate > today),
            "InProgress" => q.Where(x => x.IsActive && (!x.StartDate.HasValue || x.StartDate <= today) &&
                (!x.EndDate.HasValue || x.EndDate >= today)),
            "Ended" => q.Where(x => x.IsActive && x.EndDate < today),
            "Inactive" => q.Where(x => !x.IsActive),
            _ => throw new InvalidOperationException("專案狀態不正確。")
        };
        var count = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.ProjectCode).ThenBy(x => x.ProjectId)
            .Skip((r.Page - 1) * r.PageSize).Take(r.PageSize)
            .Select(x => new V180ProjectRow(x.ProjectId, x.TeamId, x.ProjectCode, x.ProjectName,
                x.Description, x.LocationMode, x.StartDate, x.EndDate, x.IsActive,
                db.ProjectLocations.Count(pl => pl.ProjectId == x.ProjectId && pl.IsActive)))
            .ToListAsync(ct);
        return new(rows, r.Page, r.PageSize, count);
    }

    public async Task<PagedResult<CorrectionRequestDto>> SearchCorrectionsAsync(CurrentUserDto user, V180SearchRequest input, CancellationToken ct)
    {
        var r = V180QueryRules.Normalize(input);
        var q = await ScopedCorrectionsAsync(user, ct);
        if (r.Status is null && r.Keyword is null && !r.StartDate.HasValue && !r.EndDate.HasValue && !r.TeamId.HasValue)
            throw new InvalidOperationException("請設定更正查詢條件，或選擇待審狀態。");
        if (r.Status is { } status) {
            if (status is not ("PendingLeaderReview" or "PendingAdminClose" or "Closed" or "Rejected"))
                throw new InvalidOperationException("更正狀態不正確。");
            q = q.Where(x => x.Status == status);
        }
        // Date controls explicitly represent request date in business timezone.
        if (r.StartDate.HasValue) {
            var start = DateTime.SpecifyKind(r.StartDate.Value.ToDateTime(TimeOnly.MinValue).AddHours(-8), DateTimeKind.Utc);
            q = q.Where(x => x.RequestedAt >= start);
        }
        if (r.EndDate.HasValue) {
            var end = DateTime.SpecifyKind(r.EndDate.Value.ToDateTime(TimeOnly.MaxValue).AddHours(-8), DateTimeKind.Utc);
            q = q.Where(x => x.RequestedAt <= end);
        }
        if (r.TeamId.HasValue) q = q.Where(x => db.VisitTripSnapshots.Any(s =>
            s.VisitTripSnapshotId == x.BaseSnapshotId && s.TeamId == r.TeamId));
        if (r.Keyword is { } k) q = q.Where(x => x.Reason.Contains(k) || db.VisitTripSnapshots.Any(s =>
            s.VisitTripSnapshotId == x.BaseSnapshotId && (s.TripNo.Contains(k) ||
                s.EmployeeNoSnapshot.Contains(k) || s.DisplayNameSnapshot.Contains(k) ||
                (s.TeamNameSnapshot != null && s.TeamNameSnapshot.Contains(k)) ||
                s.Stops.Any(st => st.LocationNameSnapshot.Contains(k) ||
                    (st.ProjectNameSnapshot != null && st.ProjectNameSnapshot.Contains(k))))));
        var count = await q.CountAsync(ct);
        var ids = await q.OrderByDescending(x => x.RequestedAt).ThenByDescending(x => x.CorrectionRequestId)
            .Skip((r.Page - 1) * r.PageSize).Take(r.PageSize).Select(x => x.CorrectionRequestId).ToListAsync(ct);
        var rows = new List<CorrectionRequestDto>();
        foreach (var id in ids) rows.Add(await MapCorrectionAsync(id, ct));
        return new(rows, r.Page, r.PageSize, count);
    }

    public async Task<IReadOnlyList<VisitTypeDto>> MoveVisitTypeAsync(CurrentUserDto user, int id,
        V180MoveVisitTypeRequest request, CancellationToken ct)
    {
        RequireQueryAdmin(user);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () => {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            // VisitTypes are global in the frozen baseline. Lock the ordering
            // set before comparison so concurrent moves cannot overwrite.
            var rows = await db.VisitTypes.FromSqlRaw("SELECT * FROM dbo.VisitTypes WITH (UPDLOCK, HOLDLOCK)")
                .OrderBy(x => x.SortOrder).ThenBy(x => x.VisitTypeName).ThenBy(x => x.VisitTypeId).ToListAsync(ct);
            var before = rows.Select(x => new V180OrderItem(x.VisitTypeId, x.SortOrder)).ToList();
            var after = V180QueryRules.Move(before, id, request);
            if (!before.SequenceEqual(after)) {
                foreach (var item in after) {
                    var row = rows.Single(x => x.VisitTypeId == item.VisitTypeId);
                    row.SortOrder = item.SortOrder;
                    row.UpdatedAt = DateTime.UtcNow;
                }
                db.AuditLogs.Add(new AuditLog {
                    UserId = user.UserId, EntityType = "VisitType", EntityId = id.ToString(),
                    Action = "VisitTypeReorder", OldValues = JsonSerializer.Serialize(before),
                    NewValues = JsonSerializer.Serialize(after), CreatedAt = DateTime.UtcNow, CorrelationId = Guid.NewGuid()
                });
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
            return (IReadOnlyList<VisitTypeDto>)rows.OrderBy(x => x.SortOrder).ThenBy(x => x.VisitTypeName).ThenBy(x => x.VisitTypeId)
                .Select(x => new VisitTypeDto(x.VisitTypeId, x.VisitTypeCode, x.VisitTypeName, x.Description, x.SortOrder, x.IsActive, x.InactivatedAt, x.InactivatedByUserId, Convert.ToBase64String(x.RowVersion ?? []))).ToList();
        });
    }
}
