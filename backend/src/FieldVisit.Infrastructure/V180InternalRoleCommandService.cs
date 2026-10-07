using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed record V180InternalRoleAccessRequest(
    IReadOnlyList<string> Roles,
    DateOnly EffectiveFrom);

public sealed class V180InternalRoleCommandService(
    AppDbContext db,
    ICurrentUserService current)
{
    private static readonly HashSet<string> AllowedRoles = new(
        ["visitor", "leader", "admin"],
        StringComparer.OrdinalIgnoreCase);

    public async Task UpdateAsync(int userId, V180InternalRoleAccessRequest request, CancellationToken ct)
    {
        var admin = current.GetRequired();
        if (!admin.Roles.Any(x => x.Equals("admin", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("只有管理者可以維護角色。");
        var orgId = admin.OrganizationId
            ?? throw new UnauthorizedAccessException("管理者缺少 Organization 範圍。");
        var today = BusinessTime.Today;
        if (request.EffectiveFrom != today)
            throw new InvalidOperationException("一般角色維護只允許今天生效；回溯或未來排程請使用正式授權流程。");

        var roles = (request.Roles ?? [])
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
        if (roles.Count == 0)
            throw new InvalidOperationException("Internal User 至少需要一個角色。");
        if (roles.Any(x => !AllowedRoles.Contains(x)))
            throw new InvalidOperationException("Internal User 只允許 Visitor、Leader、Admin；Supervisor 必須使用 External Supervisor 管理。");

        var identity = await db.UserIdentityProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId && x.UserType == UserTypes.Internal, ct)
            ?? throw new InvalidOperationException("只能維護 Internal User 的角色。");
        var user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId && x.OrganizationId == orgId, ct)
            ?? throw new UnauthorizedAccessException("人員不屬於目前 Organization。");

        if (roles.Any(x => x is "visitor" or "leader"))
        {
            if (!identity.EmploymentId.HasValue)
                throw new InvalidOperationException("Visitor／Leader 缺少 Employment 綁定。");
            var v17 = await db.UserTeamAssignments.AsNoTracking()
                .Where(x => x.UserId == userId
                    && x.EffectiveFrom <= request.EffectiveFrom
                    && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.EffectiveFrom))
                .OrderBy(x => x.TeamId)
                .Select(x => new { x.TeamId, x.IsPrimary })
                .ToListAsync(ct);
            var v18 = await db.TeamMemberships.AsNoTracking()
                .Where(x => x.EmploymentId == identity.EmploymentId.Value
                    && x.EffectiveFrom <= request.EffectiveFrom
                    && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.EffectiveFrom))
                .OrderBy(x => x.TeamId)
                .Select(x => new { x.TeamId, x.IsPrimary })
                .ToListAsync(ct);
            if (!v17.Select(x => (x.TeamId, x.IsPrimary)).SequenceEqual(v18.Select(x => (x.TeamId, x.IsPrimary))))
                throw new InvalidOperationException("TEAM_MEMBERSHIP_MODEL_DRIFT：v1.7 / v1.8 小組資料不一致，請先修復資料。");
            if (v18.Count == 0)
                throw new InvalidOperationException("Visitor 或 Leader 至少需要一個有效小組歸屬。");
        }

        if (await db.UserRoleAssignments.AnyAsync(x => x.UserId == userId && x.EffectiveFrom > request.EffectiveFrom, ct))
            throw new InvalidOperationException("此人員已有未來角色排程；請先處理未來排程，避免覆寫既定授權。");

        var roleRows = await db.Roles.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var targetRoles = roleRows
            .Where(x => roles.Contains(x.RoleCode.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (targetRoles.Select(x => x.RoleCode.Trim().ToLowerInvariant()).Distinct().Count() != roles.Count)
            throw new InvalidOperationException("找不到一個或多個指定 Role。");

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;
            var current = await db.UserRoleAssignments.Where(x =>
                x.UserId == userId
                && x.EffectiveFrom <= request.EffectiveFrom
                && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.EffectiveFrom)).ToListAsync(ct);
            var oldRoleIds = current.Select(x => x.RoleId).OrderBy(x => x).ToArray();
            var previousDay = request.EffectiveFrom.AddDays(-1);
            foreach (var row in current)
            {
                if (row.EffectiveFrom == request.EffectiveFrom) db.UserRoleAssignments.Remove(row);
                else row.EffectiveTo = previousDay;
            }
            await db.SaveChangesAsync(ct);

            foreach (var role in targetRoles)
            {
                db.UserRoleAssignments.Add(new UserRoleAssignment
                {
                    UserId = userId,
                    RoleId = role.RoleId,
                    EffectiveFrom = request.EffectiveFrom,
                    EffectiveTo = null,
                    AssignedByUserId = admin.UserId,
                    CreatedAt = now
                });
            }

            var legacyRoles = await db.UserRoles.Where(x => x.UserId == userId).ToListAsync(ct);
            db.UserRoles.RemoveRange(legacyRoles);
            foreach (var role in targetRoles)
                db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.RoleId, AssignedAt = now });

            db.AuditLogs.Add(new AuditLog
            {
                UserId = admin.UserId,
                EntityType = "UserRoleAssignment",
                EntityId = userId.ToString(),
                Action = "RoleUpdate",
                OldValues = JsonSerializer.Serialize(new { RoleIds = oldRoleIds }),
                NewValues = JsonSerializer.Serialize(new { Roles = roles, request.EffectiveFrom }),
                CreatedAt = now
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }
}
