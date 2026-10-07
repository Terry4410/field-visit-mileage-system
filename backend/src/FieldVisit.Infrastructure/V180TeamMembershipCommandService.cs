using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed record V180ReplaceTeamMembershipsRequest(
    IReadOnlyList<InternalTeamAssignmentInput> TeamAssignments,
    DateOnly EffectiveFrom);

public sealed class V180TeamMembershipCommandService(
    AppDbContext db,
    ICurrentUserService current)
{
    public async Task UpdateAsync(int userId, V180ReplaceTeamMembershipsRequest request, CancellationToken ct)
    {
        var admin = RequireAdmin();
        var orgId = admin.OrganizationId ?? throw new UnauthorizedAccessException("管理者缺少 Organization 範圍。");
        var today = BusinessTime.Today;
        if (request.EffectiveFrom < today)
            throw new InvalidOperationException("小組歸屬回溯異動請改走正式更正流程；一般維護不可早於今天。");

        var assignments = (request.TeamAssignments ?? [])
            .GroupBy(x => x.TeamId)
            .Select(x => x.First())
            .ToList();
        if (assignments.Any(x => x.TeamId <= 0))
            throw new InvalidOperationException("TeamId 不正確。");
        if (assignments.Count > 0 && assignments.Count(x => x.IsPrimary) != 1)
            throw new InvalidOperationException("有小組歸屬時必須且只能指定一個主要小組。");

        var identity = await db.UserIdentityProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId && x.UserType == UserTypes.Internal, ct)
            ?? throw new InvalidOperationException("只能維護內部人員的小組歸屬。");
        if (!identity.EmploymentId.HasValue)
            throw new InvalidOperationException("此帳號缺少 Employment 綁定。");
        var employment = await db.Employments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.EmploymentId == identity.EmploymentId.Value && x.OrganizationId == orgId, ct)
            ?? throw new UnauthorizedAccessException("人員不屬於目前 Organization。");

        var requestedTeamIds = assignments.Select(x => x.TeamId).ToList();
        var validTeamIds = await db.Teams.AsNoTracking()
            .Where(x => x.OrganizationId == orgId && x.IsActive && requestedTeamIds.Contains(x.TeamId))
            .Select(x => x.TeamId)
            .ToListAsync(ct);
        if (validTeamIds.Count != requestedTeamIds.Count)
            throw new InvalidOperationException("包含不存在、停用或不屬於目前 Organization 的小組。");

        var activeRoleCodes = await (
            from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.RoleId
            where a.UserId == userId
                && a.EffectiveFrom <= request.EffectiveFrom
                && (!a.EffectiveTo.HasValue || a.EffectiveTo.Value >= request.EffectiveFrom)
            select r.RoleCode.ToLower()).ToListAsync(ct);
        if (assignments.Count == 0 && activeRoleCodes.Any(x => x is "visitor" or "leader"))
            throw new InvalidOperationException("Visitor 或 Leader 至少需要一個小組歸屬。");

        var hasFutureV17 = await db.UserTeamAssignments.AnyAsync(x =>
            x.UserId == userId && x.EffectiveFrom > request.EffectiveFrom, ct);
        var hasFutureV18 = await db.TeamMemberships.AnyAsync(x =>
            x.EmploymentId == employment.EmploymentId && x.EffectiveFrom > request.EffectiveFrom, ct);
        if (hasFutureV17 || hasFutureV18)
            throw new InvalidOperationException("此人員已有未來小組排程；請先處理未來排程，避免覆寫既定歷史。");

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var oldV17 = await db.UserTeamAssignments.Where(x =>
                x.UserId == userId
                && x.EffectiveFrom <= request.EffectiveFrom
                && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.EffectiveFrom)).ToListAsync(ct);
            var oldV18 = await db.TeamMemberships.Where(x =>
                x.EmploymentId == employment.EmploymentId
                && x.EffectiveFrom <= request.EffectiveFrom
                && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.EffectiveFrom)).ToListAsync(ct);

            var v17Shape = oldV17.OrderBy(x => x.TeamId).Select(x => (x.TeamId, x.IsPrimary)).ToList();
            var v18Shape = oldV18.OrderBy(x => x.TeamId).Select(x => (x.TeamId, x.IsPrimary)).ToList();
            if (!v17Shape.SequenceEqual(v18Shape))
                throw new InvalidOperationException("TEAM_MEMBERSHIP_MODEL_DRIFT：v1.7 / v1.8 小組資料不一致，請先修復資料再維護。");

            var previousDay = request.EffectiveFrom.AddDays(-1);
            foreach (var row in oldV17)
            {
                if (row.EffectiveFrom == request.EffectiveFrom) db.UserTeamAssignments.Remove(row);
                else row.EffectiveTo = previousDay;
            }
            foreach (var row in oldV18)
            {
                if (row.EffectiveFrom == request.EffectiveFrom) db.TeamMemberships.Remove(row);
                else row.EffectiveTo = previousDay;
            }
            await db.SaveChangesAsync(ct);

            foreach (var assignment in assignments)
            {
                db.UserTeamAssignments.Add(new UserTeamAssignment
                {
                    UserId = userId,
                    TeamId = assignment.TeamId,
                    IsPrimary = assignment.IsPrimary,
                    EffectiveFrom = request.EffectiveFrom,
                    EffectiveTo = null,
                    AssignedByUserId = admin.UserId,
                    CreatedAt = DateTime.UtcNow
                });
                db.TeamMemberships.Add(new TeamMembership
                {
                    EmploymentId = employment.EmploymentId,
                    TeamId = assignment.TeamId,
                    IsPrimary = assignment.IsPrimary,
                    EffectiveFrom = request.EffectiveFrom,
                    EffectiveTo = null,
                    ChangeReason = "TeamMembershipMaintenance",
                    AssignedByUserId = admin.UserId
                });
            }

            // Compatibility/current projection is a TODAY view. Future effective-dated
            // changes must not alter what users see or authorize against today.
            if (request.EffectiveFrom == today)
            {
                var scopes = await db.UserTeamScopes.Where(x => x.UserId == userId).ToListAsync(ct);

                // SQL Server has a filtered unique invariant for the current primary scope.
                // Clear the old primary first so switching Team A -> Team B cannot depend
                // on UPDATE ordering inside one SaveChanges call.
                foreach (var scope in scopes.Where(x => x.IsActive && x.IsPrimary))
                    scope.IsPrimary = false;
                await db.SaveChangesAsync(ct);

                foreach (var scope in scopes)
                {
                    var desired = assignments.FirstOrDefault(x => x.TeamId == scope.TeamId);
                    scope.IsActive = desired is not null;
                    scope.IsPrimary = desired?.IsPrimary == true;
                    scope.EndedAt = desired is null ? DateTime.UtcNow : null;
                    if (desired is not null)
                    {
                        scope.AssignedAt = DateTime.UtcNow;
                        scope.AssignedByUserId = admin.UserId;
                    }
                }
                foreach (var assignment in assignments.Where(x => scopes.All(s => s.TeamId != x.TeamId)))
                {
                    db.UserTeamScopes.Add(new UserTeamScope
                    {
                        UserId = userId,
                        TeamId = assignment.TeamId,
                        IsPrimary = assignment.IsPrimary,
                        IsActive = true,
                        AssignedAt = DateTime.UtcNow,
                        AssignedByUserId = admin.UserId
                    });
                }

                var user = await db.Users.SingleAsync(x => x.UserId == userId && x.OrganizationId == orgId, ct);
                user.TeamId = assignments.FirstOrDefault(x => x.IsPrimary)?.TeamId;
                user.UpdatedAt = DateTime.UtcNow;
            }

            db.AuditLogs.Add(new AuditLog
            {
                UserId = admin.UserId,
                EntityType = "TeamMembership",
                EntityId = userId.ToString(),
                Action = "ReplaceMemberships",
                OldValues = System.Text.Json.JsonSerializer.Serialize(v17Shape),
                NewValues = System.Text.Json.JsonSerializer.Serialize(new { request.EffectiveFrom, Assignments = assignments }),
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    private CurrentUserDto RequireAdmin()
    {
        var user = current.GetRequired();
        if (!user.Roles.Any(x => x.Equals("admin", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("只有管理者可維護小組成員。");
        return user;
    }
}
