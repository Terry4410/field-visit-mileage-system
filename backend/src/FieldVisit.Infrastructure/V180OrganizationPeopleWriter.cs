using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180OrganizationPeopleWriter(AppDbContext db) : IV180OrganizationPeopleWriter
{
    public async Task<long> ResolveEmploymentIdAsync(int legacyUserId, CancellationToken ct)
    {
        var profileMatches = await db.UserIdentityProfiles.AsNoTracking()
            .Where(x => x.UserId == legacyUserId && x.EmploymentId.HasValue)
            .Select(x => x.EmploymentId!.Value).ToListAsync(ct);
        var legacyMatches = await db.Employments.AsNoTracking()
            .Where(x => x.LegacyUserId == legacyUserId).Select(x => x.EmploymentId).ToListAsync(ct);
        return V180IdentityBridgeRules.ResolveEmploymentId(profileMatches, legacyMatches);
    }

    public async Task<string> GetVersionAsync(long employmentId, CancellationToken ct)
    {
        var version = await db.Employments.AsNoTracking().Where(x => x.EmploymentId == employmentId)
            .Select(x => x.RowVersion).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("找不到 Employment。");
        return Convert.ToBase64String(version);
    }

    public Task<V180PeopleAccessWriteResult> UpdateAccessAsync(CurrentUserDto admin, long employmentId,
        V180UpdatePeopleAccessRequest request, CancellationToken ct) =>
        UpdateCoreAsync(admin, employmentId, request, new(false, null, null, null), ct);

    public Task<V180PeopleAccessWriteResult> UpdateAccessFromLegacyAsync(CurrentUserDto admin, long employmentId,
        V180UpdatePeopleAccessRequest request, V170IdentityBindingInput identity, CancellationToken ct) =>
        UpdateCoreAsync(admin, employmentId, request, identity, ct);

    public async Task<V180ExternalIdentityWriteResult> CreateExternalIdentityAsync(CurrentUserDto admin,
        User user, SaveExternalSupervisorRequest request, int supervisorRoleId, DateTime now, CancellationToken ct)
    {
        RequireAmbientTransaction();
        if (user.UserId <= 0 || user.OrganizationId != RequireAdminOrganization(admin) || user.EmployeeNo is not null)
            throw new InvalidOperationException("External Supervisor identity prerequisite 不正確。");
        var person = new Person { DisplayName = request.DisplayName, LegacyUserId = user.UserId,
            CreatedAt = now, CreatedByUserId = admin.UserId };
        await db.Persons.AddAsync(person, ct);
        await db.SaveChangesAsync(ct);
        var employment = new Employment
        {
            PersonId = person.PersonId, OrganizationId = user.OrganizationId.Value,
            EmployeeNo = null, Email = request.Email, LegacyUserId = user.UserId,
            SourceType = "ExternalSupervisor", SourceReference = $"Users:{user.UserId}",
            CreatedAt = now, CreatedByUserId = admin.UserId
        };
        await db.Employments.AddAsync(employment, ct);
        await db.SaveChangesAsync(ct);
        await db.EmploymentStatusPeriods.AddAsync(new EmploymentStatusPeriod
        {
            EmploymentId = employment.EmploymentId, EmploymentStatus = EmploymentStatuses.Active,
            EffectiveFrom = request.AuthorizationFrom, EffectiveTo = request.AuthorizationTo,
            SourceType = "ExternalSupervisor", SourceReference = $"Users:{user.UserId}",
            CreatedAt = now, CreatedByUserId = admin.UserId
        }, ct);
        await db.EmploymentRoleAssignments.AddAsync(new EmploymentRoleAssignment
        {
            EmploymentId = employment.EmploymentId, RoleId = supervisorRoleId,
            EffectiveFrom = request.AuthorizationFrom, EffectiveTo = request.AuthorizationTo,
            AssignedByUserId = admin.UserId, CreatedAt = now
        }, ct);
        await db.SaveChangesAsync(ct);
        return new(person.PersonId, employment.EmploymentId);
    }

    public async Task<V180ExternalIdentityWriteResult> UpdateExternalIdentityAsync(CurrentUserDto admin, User user,
        UserIdentityProfile profile, UpdateExternalSupervisorRequest request, int supervisorRoleId,
        DateTime now, CancellationToken ct)
    {
        RequireAmbientTransaction();
        if (!profile.EmploymentId.HasValue)
            profile.EmploymentId = await ResolveEmploymentIdAsync(user.UserId, ct);
        var employment = await db.Employments.SingleAsync(x => x.EmploymentId == profile.EmploymentId, ct);
        if (employment.LegacyUserId != user.UserId || employment.OrganizationId != RequireAdminOrganization(admin))
            throw new InvalidOperationException("IDENTITY_BRIDGE_MISMATCH：External Supervisor bridge 不一致。");
        if (await db.TeamMemberships.AnyAsync(x => x.EmploymentId == employment.EmploymentId, ct))
            throw new InvalidOperationException("External Supervisor 不得具有 TeamMembership。");
        var person = await db.Persons.SingleAsync(x => x.PersonId == employment.PersonId, ct);
        person.DisplayName = request.DisplayName; person.UpdatedAt = now; person.UpdatedByUserId = admin.UserId;
        employment.Email = request.Email; employment.UpdatedAt = now; employment.UpdatedByUserId = admin.UserId;
        await PrepareEffectiveRowsAsync(db.EmploymentStatusPeriods.Where(x => x.EmploymentId == employment.EmploymentId),
            request.ChangeEffectiveFrom, "Employment Status", ct);
        await PrepareEffectiveRowsAsync(db.EmploymentRoleAssignments.Where(x => x.EmploymentId == employment.EmploymentId),
            request.ChangeEffectiveFrom, "External Role", ct);
        await db.EmploymentStatusPeriods.AddAsync(new EmploymentStatusPeriod
        {
            EmploymentId = employment.EmploymentId, EmploymentStatus = EmploymentStatuses.Active,
            EffectiveFrom = request.ChangeEffectiveFrom, EffectiveTo = request.AuthorizationTo,
            SourceType = "ExternalSupervisor", SourceReference = $"Users:{user.UserId}",
            CreatedAt = now, CreatedByUserId = admin.UserId
        }, ct);
        await db.EmploymentRoleAssignments.AddAsync(new EmploymentRoleAssignment
        {
            EmploymentId = employment.EmploymentId, RoleId = supervisorRoleId,
            EffectiveFrom = request.ChangeEffectiveFrom, EffectiveTo = request.AuthorizationTo,
            AssignedByUserId = admin.UserId, CreatedAt = now
        }, ct);
        await db.SaveChangesAsync(ct);
        return new(person.PersonId, employment.EmploymentId);
    }

    public async Task ProjectExternalCompatibilityAsync(CurrentUserDto admin, User user,
        long employmentId, DateTime now, CancellationToken ct)
    {
        RequireAmbientTransaction();
        var bridge = await ResolveBridgeAsync(
            await db.Employments.SingleAsync(x => x.EmploymentId == employmentId, ct), ct);
        if (bridge.UserId != user.UserId)
            throw new InvalidOperationException("IDENTITY_BRIDGE_MISMATCH：External projection target 不一致。");
        await ProjectCompatibilityAsync(employmentId, user.UserId, user, admin.UserId, now, ct);
    }

    private async Task<V180PeopleAccessWriteResult> UpdateCoreAsync(CurrentUserDto admin, long employmentId,
        V180UpdatePeopleAccessRequest input, V170IdentityBindingInput identityBinding, CancellationToken ct)
    {
        var request = V180PeopleAccessRules.Normalize(input, BusinessTime.Today);
        var expectedVersion = V180PeopleAccessRules.DecodeVersion(request.Version);
        var organizationId = RequireAdminOrganization(admin);
        var result = default(V180PeopleAccessWriteResult);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;
            var employment = await db.Employments.SingleOrDefaultAsync(x =>
                x.EmploymentId == employmentId && x.OrganizationId == organizationId, ct)
                ?? throw new KeyNotFoundException("找不到 Employment。");
            if (!employment.RowVersion.SequenceEqual(expectedVersion))
                throw new InvalidOperationException("ROWVERSION_CONFLICT：Employment 已由其他人更新，請重新載入。");

            var bridge = await ResolveBridgeAsync(employment, ct);
            var user = await db.Users.SingleAsync(x => x.UserId == bridge.UserId && x.OrganizationId == organizationId, ct);
            var profile = await db.UserIdentityProfiles.SingleAsync(x => x.UserId == bridge.UserId, ct);
            if (!profile.UserType.Equals(UserTypes.Internal, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("External Supervisor 不可使用 Internal TeamMembership 寫入流程。");
            profile.EmploymentId ??= employment.EmploymentId;

            if (identityBinding.IsSpecified)
            {
                await EnsureIdentityBindingAvailableAsync(identityBinding, bridge.UserId, ct);
                profile.IdentityProvider = identityBinding.IdentityProvider!;
                profile.EntraTenantId = identityBinding.EntraTenantId;
                profile.EntraObjectId = identityBinding.EntraObjectId;
                profile.UpdatedAt = now;
            }

            var teamIds = request.TeamMemberships.Select(x => x.TeamId).ToArray();
            var validTeams = await db.Teams.AsNoTracking().Where(x => teamIds.Contains(x.TeamId) &&
                x.OrganizationId == organizationId && x.IsActive).Select(x => x.TeamId).ToListAsync(ct);
            if (validTeams.Count != teamIds.Length)
                throw new InvalidOperationException("包含不存在、已停用或不屬於目前 Organization 的 Team。");

            var roleRows = await db.Roles.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
            var targetRoles = roleRows.Where(x => request.Roles.Contains(NormalizeRole(x.RoleCode),
                StringComparer.OrdinalIgnoreCase)).ToList();
            if (targetRoles.Select(x => NormalizeRole(x.RoleCode)).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != request.Roles.Count)
                throw new InvalidOperationException("找不到一個或多個指定 Role。");

            var rolesBefore = await db.EmploymentRoleAssignments.AsNoTracking()
                .Where(x => x.EmploymentId == employmentId).ToListAsync(ct);
            var teamsBefore = await db.TeamMemberships.AsNoTracking()
                .Where(x => x.EmploymentId == employmentId).ToListAsync(ct);
            await PrepareEffectiveRowsAsync(db.EmploymentRoleAssignments.Where(x => x.EmploymentId == employmentId),
                request.ChangeEffectiveFrom, "Role", ct);
            await PrepareEffectiveRowsAsync(db.TeamMemberships.Where(x => x.EmploymentId == employmentId),
                request.ChangeEffectiveFrom, "Team", ct);
            await PrepareEffectiveRowsAsync(db.TeamLeaderAssignments.Where(x => x.EmploymentId == employmentId),
                request.ChangeEffectiveFrom, "Team Leader", ct);

            foreach (var role in targetRoles)
                await db.EmploymentRoleAssignments.AddAsync(new EmploymentRoleAssignment
                {
                    EmploymentId = employmentId, RoleId = role.RoleId,
                    EffectiveFrom = request.ChangeEffectiveFrom, AssignedByUserId = admin.UserId, CreatedAt = now
                }, ct);
            foreach (var team in request.TeamMemberships)
                await db.TeamMemberships.AddAsync(new TeamMembership
                {
                    EmploymentId = employmentId, TeamId = team.TeamId, IsPrimary = team.IsPrimary,
                    EffectiveFrom = request.ChangeEffectiveFrom, AssignedByUserId = admin.UserId, CreatedAt = now
                }, ct);
            foreach (var leaderTeamId in V180LeaderAssignmentRules.DeriveTeamIds(
                         request.Roles, request.TeamMemberships))
                    await db.TeamLeaderAssignments.AddAsync(new TeamLeaderAssignment
                    {
                        TeamId = leaderTeamId, EmploymentId = employmentId,
                        EffectiveFrom = request.ChangeEffectiveFrom, AssignedByUserId = admin.UserId, CreatedAt = now
                    }, ct);

            user.IsActive = request.AdminEnabled;
            user.UpdatedAt = now;
            employment.UpdatedAt = now;
            employment.UpdatedByUserId = admin.UserId;
            await db.SaveChangesAsync(ct);

            await ProjectCompatibilityAsync(employmentId, bridge.UserId, user, admin.UserId, now, ct);
            await db.SaveChangesAsync(ct);
            var afterVersion = Convert.ToBase64String(employment.RowVersion);
            await db.AuditLogs.AddAsync(new AuditLog
            {
                UserId = admin.UserId, EntityType = "Employment", EntityId = employmentId.ToString(),
                Action = request.AdminEnabled ? "V180PeopleAccessUpdate" : "V180PeopleAccessDisable",
                NewValues = JsonSerializer.Serialize(new
                {
                    employment.PersonId, employment.EmploymentId, LegacyUserId = bridge.UserId,
                    request.ChangeEffectiveFrom, RolesBefore = rolesBefore, TeamsBefore = teamsBefore,
                    Roles = request.Roles, request.TeamMemberships, request.AdminEnabled,
                    VersionBefore = request.Version, VersionAfter = afterVersion, request.ConfirmRetroactive
                }), CreatedAt = now
            }, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            result = new(employment.PersonId, employment.EmploymentId, bridge.UserId,
                Convert.ToBase64String(employment.RowVersion));
        });
        return result!;
    }

    private async Task ProjectCompatibilityAsync(long employmentId, int userId, User user,
        int adminUserId, DateTime now, CancellationToken ct)
    {
        var statusRows = await db.EmploymentStatusPeriods.AsNoTracking().Where(x => x.EmploymentId == employmentId).ToListAsync(ct);
        db.UserEmploymentPeriods.RemoveRange(await db.UserEmploymentPeriods.Where(x => x.UserId == userId).ToListAsync(ct));
        foreach (var row in statusRows)
            await db.UserEmploymentPeriods.AddAsync(new UserEmploymentPeriod
            {
                UserId = userId, EmploymentStatus = row.EmploymentStatus, EffectiveFrom = row.EffectiveFrom,
                EffectiveTo = row.EffectiveTo, SourceType = row.SourceType, SourceReference = row.SourceReference,
                CreatedAt = row.CreatedAt
            }, ct);

        var roleRows = await db.EmploymentRoleAssignments.AsNoTracking().Where(x => x.EmploymentId == employmentId).ToListAsync(ct);
        db.UserRoleAssignments.RemoveRange(await db.UserRoleAssignments.Where(x => x.UserId == userId).ToListAsync(ct));
        foreach (var row in roleRows)
            await db.UserRoleAssignments.AddAsync(new UserRoleAssignment
            {
                UserId = userId, RoleId = row.RoleId, EffectiveFrom = row.EffectiveFrom,
                EffectiveTo = row.EffectiveTo, AssignedByUserId = row.AssignedByUserId, CreatedAt = row.CreatedAt
            }, ct);

        var memberships = await db.TeamMemberships.AsNoTracking().Where(x => x.EmploymentId == employmentId).ToListAsync(ct);
        db.UserTeamAssignments.RemoveRange(await db.UserTeamAssignments.Where(x => x.UserId == userId).ToListAsync(ct));
        foreach (var row in memberships)
            await db.UserTeamAssignments.AddAsync(new UserTeamAssignment
            {
                UserId = userId, TeamId = row.TeamId, IsPrimary = row.IsPrimary,
                EffectiveFrom = row.EffectiveFrom, EffectiveTo = row.EffectiveTo,
                AssignedByUserId = row.AssignedByUserId, CreatedAt = row.CreatedAt
            }, ct);
        await db.SaveChangesAsync(ct);

        var today = BusinessTime.Today;
        var currentRoleIds = roleRows.Where(x => V180AsOfRules.IsEffective(x.EffectiveFrom, x.EffectiveTo, today))
            .Select(x => x.RoleId).Distinct().ToHashSet();
        db.UserRoles.RemoveRange(await db.UserRoles.Where(x => x.UserId == userId).ToListAsync(ct));
        foreach (var roleId in currentRoleIds)
            await db.UserRoles.AddAsync(new UserRole { UserId = userId, RoleId = roleId, AssignedAt = now }, ct);

        var currentTeams = memberships.Where(x => V180AsOfRules.IsEffective(x.EffectiveFrom, x.EffectiveTo, today)).ToList();
        if (currentTeams.Count > 0 && currentTeams.Count(x => x.IsPrimary) != 1)
            throw new InvalidOperationException("目前有效 TeamMembership 的 Primary Team 資料不正確。");
        var scopes = await db.UserTeamScopes.Where(x => x.UserId == userId).ToListAsync(ct);
        foreach (var scope in scopes.Where(x => x.IsActive && x.IsPrimary)) scope.IsPrimary = false;
        await db.SaveChangesAsync(ct);
        foreach (var scope in scopes)
        {
            var desired = currentTeams.SingleOrDefault(x => x.TeamId == scope.TeamId);
            scope.IsActive = desired is not null; scope.IsPrimary = desired?.IsPrimary == true;
            scope.EndedAt = desired is null ? now : null;
            if (desired is not null) { scope.AssignedAt = now; scope.AssignedByUserId = adminUserId; }
        }
        foreach (var desired in currentTeams.Where(x => scopes.All(s => s.TeamId != x.TeamId)))
            await db.UserTeamScopes.AddAsync(new UserTeamScope
            {
                UserId = userId, TeamId = desired.TeamId, IsPrimary = desired.IsPrimary,
                IsActive = true, AssignedAt = now, AssignedByUserId = adminUserId
            }, ct);
        user.TeamId = currentTeams.SingleOrDefault(x => x.IsPrimary)?.TeamId;
    }

    private async Task PrepareEffectiveRowsAsync<TEntity>(IQueryable<TEntity> query,
        DateOnly effectiveFrom, string label, CancellationToken ct) where TEntity : class
    {
        var rows = await query.ToListAsync(ct);
        var type = typeof(TEntity);
        var from = type.GetProperty("EffectiveFrom")!;
        var to = type.GetProperty("EffectiveTo")!;
        if (rows.Any(x => (DateOnly)from.GetValue(x)! > effectiveFrom))
            throw new InvalidOperationException($"此 Employment 已有較晚生效的 {label} 排程，請先處理該排程後再異動。");
        var sameStart = rows.Where(x => (DateOnly)from.GetValue(x)! == effectiveFrom).ToList();
        foreach (var row in rows.Except(sameStart))
        {
            var rowFrom = (DateOnly)from.GetValue(row)!;
            var rowTo = (DateOnly?)to.GetValue(row);
            if (rowFrom < effectiveFrom && (!rowTo.HasValue || rowTo >= effectiveFrom))
                to.SetValue(row, PreviousDay(effectiveFrom));
        }
        db.RemoveRange(sameStart);
    }

    private async Task<Bridge> ResolveBridgeAsync(Employment employment, CancellationToken ct)
    {
        var profiles = await db.UserIdentityProfiles.Where(x => x.EmploymentId == employment.EmploymentId).ToListAsync(ct);
        if (profiles.Count > 1) throw new InvalidOperationException("AMBIGUOUS_IDENTITY_BRIDGE：Employment 對應多個 UserIdentityProfile。");
        var profileUserId = profiles.SingleOrDefault()?.UserId;
        if (profileUserId.HasValue && employment.LegacyUserId.HasValue && profileUserId != employment.LegacyUserId)
            throw new InvalidOperationException("IDENTITY_BRIDGE_MISMATCH：UserIdentityProfile 與 Employment.LegacyUserId 不一致。");
        var userId = profileUserId ?? employment.LegacyUserId
            ?? throw new InvalidOperationException("Employment 缺少 deterministic Legacy UserId bridge。");
        return new(userId);
    }

    private async Task EnsureIdentityBindingAvailableAsync(V170IdentityBindingInput identity, int userId, CancellationToken ct)
    {
        if (!identity.IsSpecified || identity.IdentityProvider != "EntraId") return;
        if (await db.UserIdentityProfiles.AsNoTracking().AnyAsync(x => x.UserId != userId &&
            x.EntraTenantId == identity.EntraTenantId && x.EntraObjectId == identity.EntraObjectId, ct))
            throw new InvalidOperationException("此 Microsoft Entra 身分已綁定其他帳號。");
    }

    private static int RequireAdminOrganization(CurrentUserDto admin)
    {
        if (!admin.Roles.Any(x => x.Equals("admin", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("只有管理者可以維護 v1.8 人員權限。");
        return admin.OrganizationId ?? throw new InvalidOperationException("目前管理者缺少 OrganizationId。");
    }

    private void RequireAmbientTransaction()
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("External identity projection requires the caller's atomic transaction.");
    }

    private static string NormalizeRole(string role) => V170InternalUserAccessRules.NormalizeRole(role);
    private static DateOnly PreviousDay(DateOnly date) => date == DateOnly.MinValue
        ? throw new InvalidOperationException("異動生效日不可早於支援範圍。") : date.AddDays(-1);
    private sealed record Bridge(int UserId);
}
