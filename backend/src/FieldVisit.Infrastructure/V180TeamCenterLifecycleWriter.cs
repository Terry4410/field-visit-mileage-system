using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180TeamCenterLifecycleWriter(AppDbContext db) : IV180TeamCenterLifecycleWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> GetTeamVersionAsync(int teamId, int organizationId, CancellationToken ct)
    {
        var version = await db.Teams.AsNoTracking()
            .Where(x => x.TeamId == teamId && x.OrganizationId == organizationId)
            .Select(x => x.RowVersion).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("找不到小組。");
        return Convert.ToBase64String(version);
    }

    public async Task<V180TeamWriteResult> CreateTeamAsync(CurrentUserDto admin,
        V180CreateTeamRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var code = V180TeamCenterLifecycleRules.NormalizeCode(input.Code, "小組");
        var name = V180TeamCenterLifecycleRules.NormalizeName(input.Name, "小組");
        var notes = V180TeamCenterLifecycleRules.NormalizeNotes(input.Notes, 1000, "備註");
        V180TeamCenterLifecycleRules.ValidatePeriod(input.EffectiveFrom, input.EffectiveTo, "小組有效期間");
        EnsureInactiveHasEnd(input.IsActive, input.EffectiveTo, "小組");
        await EnsureOrganizationAsync(organizationId, ct);
        if (await db.Teams.AnyAsync(x => x.OrganizationId == organizationId && x.TeamCode == code, ct))
            throw new InvalidOperationException("小組代碼已存在。");

        var now = DateTime.UtcNow;
        var row = new Team
        {
            OrganizationId = organizationId, TeamCode = code, TeamName = name,
            EffectiveFrom = input.EffectiveFrom, EffectiveTo = input.EffectiveTo,
            IsActive = input.IsActive, Notes = notes, CreatedAt = now,
            InactivatedByUserId = input.IsActive ? null : admin.UserId
        };
        db.Teams.Add(row);
        AddAudit(admin.UserId, "Team", null, "V180TeamCreate", input);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<V180TeamWriteResult> UpdateTeamAsync(CurrentUserDto admin, int teamId,
        V180UpdateTeamRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var row = await FindTeamAsync(teamId, organizationId, ct);
        EnsureVersion(row.RowVersion, input.Version);
        var code = V180TeamCenterLifecycleRules.NormalizeCode(input.Code, "小組");
        var name = V180TeamCenterLifecycleRules.NormalizeName(input.Name, "小組");
        var notes = V180TeamCenterLifecycleRules.NormalizeNotes(input.Notes, 1000, "備註");
        V180TeamCenterLifecycleRules.ValidatePeriod(input.EffectiveFrom, input.EffectiveTo, "小組有效期間");
        EnsureInactiveHasEnd(input.IsActive, input.EffectiveTo, "小組");
        if (await db.Teams.AnyAsync(x => x.OrganizationId == organizationId && x.TeamId != teamId && x.TeamCode == code, ct))
            throw new InvalidOperationException("小組代碼已存在。");
        await EnsureTeamRelationshipsWithinLifecycleAsync(teamId, input.EffectiveFrom, input.EffectiveTo, ct);
        if (row.IsActive && !input.IsActive)
            await EnsureTeamCanDeactivateAsync(teamId, input.EffectiveTo!.Value, ct);

        var before = new { row.TeamCode, row.TeamName, row.EffectiveFrom, row.EffectiveTo, row.IsActive, row.Notes };
        row.TeamCode = code; row.TeamName = name; row.EffectiveFrom = input.EffectiveFrom;
        row.EffectiveTo = input.EffectiveTo; row.IsActive = input.IsActive; row.Notes = notes;
        row.UpdatedAt = DateTime.UtcNow;
        row.InactivatedByUserId = input.IsActive ? null : admin.UserId;
        AddAudit(admin.UserId, "Team", teamId.ToString(), "V180TeamUpdate", new { before, input });
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<V180TeamWriteResult> DeactivateTeamAsync(CurrentUserDto admin, int teamId,
        V180DeactivateRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var row = await FindTeamAsync(teamId, organizationId, ct);
        EnsureVersion(row.RowVersion, input.Version);
        var from = row.EffectiveFrom ?? input.EffectiveTo;
        V180TeamCenterLifecycleRules.ValidatePeriod(from, input.EffectiveTo, "小組有效期間");
        await EnsureTeamCanDeactivateAsync(teamId, input.EffectiveTo, ct);
        row.EffectiveTo = input.EffectiveTo; row.IsActive = false;
        row.InactivatedByUserId = admin.UserId; row.UpdatedAt = DateTime.UtcNow;
        AddAudit(admin.UserId, "Team", teamId.ToString(), "V180TeamDeactivate", input);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<V180CenterWriteResult> CreateCenterAsync(CurrentUserDto admin,
        V180CreateCenterRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var code = V180TeamCenterLifecycleRules.NormalizeCode(input.Code, "中心");
        var name = V180TeamCenterLifecycleRules.NormalizeName(input.Name, "中心");
        var notes = V180TeamCenterLifecycleRules.NormalizeNotes(input.Notes, 1000, "備註");
        V180TeamCenterLifecycleRules.ValidatePeriod(input.EffectiveFrom, input.EffectiveTo, "中心有效期間");
        EnsureInactiveHasEnd(input.IsActive, input.EffectiveTo, "中心");
        await EnsureOrganizationAsync(organizationId, ct);
        if (await db.Centers.AnyAsync(x => x.OrganizationId == organizationId && x.CenterCode == code, ct))
            throw new InvalidOperationException("中心代碼已存在。");
        var now = DateTime.UtcNow;
        var row = new Center
        {
            OrganizationId = organizationId, CenterCode = code, CenterName = name,
            EffectiveFrom = input.EffectiveFrom, EffectiveTo = input.EffectiveTo,
            IsActive = input.IsActive, Notes = notes, CreatedAt = now, CreatedByUserId = admin.UserId,
            InactivatedAt = input.IsActive ? null : now,
            InactivatedByUserId = input.IsActive ? null : admin.UserId
        };
        db.Centers.Add(row);
        AddAudit(admin.UserId, "Center", null, "V180CenterCreate", input);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<V180CenterWriteResult> UpdateCenterAsync(CurrentUserDto admin, int centerId,
        V180UpdateCenterRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var row = await FindCenterAsync(centerId, organizationId, ct);
        EnsureVersion(row.RowVersion, input.Version);
        var code = V180TeamCenterLifecycleRules.NormalizeCode(input.Code, "中心");
        var name = V180TeamCenterLifecycleRules.NormalizeName(input.Name, "中心");
        var notes = V180TeamCenterLifecycleRules.NormalizeNotes(input.Notes, 1000, "備註");
        V180TeamCenterLifecycleRules.ValidatePeriod(input.EffectiveFrom, input.EffectiveTo, "中心有效期間");
        EnsureInactiveHasEnd(input.IsActive, input.EffectiveTo, "中心");
        if (await db.Centers.AnyAsync(x => x.OrganizationId == organizationId && x.CenterId != centerId && x.CenterCode == code, ct))
            throw new InvalidOperationException("中心代碼已存在。");
        await EnsureCenterAssignmentsWithinLifecycleAsync(centerId, input.EffectiveFrom, input.EffectiveTo, ct);
        if (row.IsActive && !input.IsActive)
            await EnsureCenterCanDeactivateAsync(centerId, input.EffectiveTo!.Value, ct);
        var before = new { row.CenterCode, row.CenterName, row.EffectiveFrom, row.EffectiveTo, row.IsActive, row.Notes };
        row.CenterCode = code; row.CenterName = name; row.EffectiveFrom = input.EffectiveFrom;
        row.EffectiveTo = input.EffectiveTo; row.IsActive = input.IsActive; row.Notes = notes;
        row.UpdatedAt = DateTime.UtcNow; row.UpdatedByUserId = admin.UserId;
        row.InactivatedAt = input.IsActive ? null : DateTime.UtcNow;
        row.InactivatedByUserId = input.IsActive ? null : admin.UserId;
        AddAudit(admin.UserId, "Center", centerId.ToString(), "V180CenterUpdate", new { before, input });
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<V180CenterWriteResult> DeactivateCenterAsync(CurrentUserDto admin, int centerId,
        V180DeactivateRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var row = await FindCenterAsync(centerId, organizationId, ct);
        EnsureVersion(row.RowVersion, input.Version);
        V180TeamCenterLifecycleRules.ValidatePeriod(row.EffectiveFrom, input.EffectiveTo, "中心有效期間");
        await EnsureCenterCanDeactivateAsync(centerId, input.EffectiveTo, ct);
        row.EffectiveTo = input.EffectiveTo; row.IsActive = false; row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedByUserId = admin.UserId; row.InactivatedAt = DateTime.UtcNow; row.InactivatedByUserId = admin.UserId;
        AddAudit(admin.UserId, "Center", centerId.ToString(), "V180CenterDeactivate", input);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<V180TeamCenterAssignmentWriteResult> CreateTeamCenterAssignmentAsync(CurrentUserDto admin,
        V180CreateTeamCenterAssignmentRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var team = await FindTeamAsync(input.TeamId, organizationId, ct);
        var center = await FindCenterForAssignmentAsync(input.CenterId, ct);
        ValidateAssignment(team, center, input.EffectiveFrom, input.EffectiveTo);
        await EnsureNoAssignmentOverlapAsync(input.TeamId, input.EffectiveFrom, input.EffectiveTo, null, ct);
        var row = new TeamCenterAssignment
        {
            TeamId = input.TeamId, CenterId = input.CenterId, EffectiveFrom = input.EffectiveFrom,
            EffectiveTo = input.EffectiveTo,
            ChangeReason = V180TeamCenterLifecycleRules.NormalizeNotes(input.ChangeReason, 500, "異動原因"),
            CreatedAt = DateTime.UtcNow, CreatedByUserId = admin.UserId
        };
        db.TeamCenterAssignments.Add(row);
        AddAudit(admin.UserId, "TeamCenterAssignment", null, "V180TeamCenterCreate", input);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<V180TeamCenterAssignmentWriteResult> UpdateTeamCenterAssignmentAsync(CurrentUserDto admin,
        long assignmentId, V180UpdateTeamCenterAssignmentRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var row = await FindAssignmentAsync(assignmentId, organizationId, ct);
        EnsureVersion(row.RowVersion, input.Version);
        var team = await FindTeamAsync(row.TeamId, organizationId, ct);
        var center = await FindCenterForAssignmentAsync(input.CenterId, ct);
        ValidateAssignment(team, center, input.EffectiveFrom, input.EffectiveTo);
        await EnsureNoAssignmentOverlapAsync(row.TeamId, input.EffectiveFrom, input.EffectiveTo, assignmentId, ct);
        await EnsureTeamSiteCoverageAfterAssignmentChangeAsync(assignmentId, row.TeamId,
            input.CenterId, input.EffectiveFrom, input.EffectiveTo, ct);
        row.CenterId = input.CenterId; row.EffectiveFrom = input.EffectiveFrom; row.EffectiveTo = input.EffectiveTo;
        row.ChangeReason = V180TeamCenterLifecycleRules.NormalizeNotes(input.ChangeReason, 500, "異動原因");
        AddAudit(admin.UserId, "TeamCenterAssignment", assignmentId.ToString(), "V180TeamCenterUpdate", input);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<V180TeamCenterAssignmentWriteResult> EndTeamCenterAssignmentAsync(CurrentUserDto admin,
        long assignmentId, V180EndTeamCenterAssignmentRequest input, CancellationToken ct)
    {
        var organizationId = RequireAdminOrganization(admin);
        var row = await FindAssignmentAsync(assignmentId, organizationId, ct);
        EnsureVersion(row.RowVersion, input.Version);
        V180TeamCenterLifecycleRules.ValidatePeriod(row.EffectiveFrom, input.EffectiveTo, "Team-Center 有效期間");
        await EnsureTeamSiteCoverageAfterAssignmentChangeAsync(assignmentId, row.TeamId,
            row.CenterId, row.EffectiveFrom, input.EffectiveTo, ct);
        row.EffectiveTo = input.EffectiveTo;
        AddAudit(admin.UserId, "TeamCenterAssignment", assignmentId.ToString(), "V180TeamCenterEnd", input);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    private async Task EnsureTeamSiteCoverageAfterAssignmentChangeAsync(long assignmentId, int teamId,
        int proposedCenterId, DateOnly proposedFrom, DateOnly? proposedTo, CancellationToken ct)
    {
        var teamSites = await db.Set<TeamDeploymentSiteAssignment>().AsNoTracking()
            .Include(x => x.DeploymentSite).Where(x => x.TeamId == teamId).ToListAsync(ct);
        if (teamSites.Count == 0) return;

        var otherAssignments = await db.TeamCenterAssignments.AsNoTracking()
            .Where(x => x.TeamId == teamId && x.TeamCenterAssignmentId != assignmentId).ToListAsync(ct);
        foreach (var teamSite in teamSites)
        {
            var coveredByProposed = teamSite.DeploymentSite.CenterId == proposedCenterId &&
                V180TeamCenterLifecycleRules.IsWithin(teamSite.EffectiveFrom, teamSite.EffectiveTo,
                    proposedFrom, proposedTo);
            var coveredByOther = otherAssignments.Any(x =>
                x.CenterId == teamSite.DeploymentSite.CenterId &&
                V180TeamCenterLifecycleRules.IsWithin(teamSite.EffectiveFrom, teamSite.EffectiveTo,
                    x.EffectiveFrom, x.EffectiveTo));
            if (!coveredByProposed && !coveredByOther)
                throw new InvalidOperationException("TEAM_CENTER_DEPENDENCY_CONFLICT：調整 Team-Center 會使既有或未來 Team-Site 缺少完整 coverage。");
        }
    }

    private async Task EnsureTeamCanDeactivateAsync(int teamId, DateOnly end, CancellationToken ct)
    {
        if (await db.TeamMemberships.AnyAsync(x => x.TeamId == teamId && (!x.EffectiveTo.HasValue || x.EffectiveTo >= end), ct))
            throw new InvalidOperationException("TEAM_DEACTIVATION_BLOCKED：仍有生效或未來 TeamMembership。");
        if (await db.TeamLeaderAssignments.AnyAsync(x => x.TeamId == teamId && (!x.EffectiveTo.HasValue || x.EffectiveTo >= end), ct))
            throw new InvalidOperationException("TEAM_DEACTIVATION_BLOCKED：仍有生效或未來 TeamLeaderAssignment。");
        if (await db.TeamLeaderDelegations.AnyAsync(d => db.TeamLeaderAssignments.Any(a =>
            a.TeamLeaderAssignmentId == d.TeamLeaderAssignmentId && a.TeamId == teamId) && d.EffectiveTo >= end, ct))
            throw new InvalidOperationException("TEAM_DEACTIVATION_BLOCKED：仍有生效或未來 TeamLeaderDelegation。");
        if (await db.TeamCenterAssignments.AnyAsync(x => x.TeamId == teamId && (!x.EffectiveTo.HasValue || x.EffectiveTo >= end), ct))
            throw new InvalidOperationException("TEAM_DEACTIVATION_BLOCKED：仍有生效或未來 TeamCenterAssignment。");
    }

    private async Task EnsureCenterCanDeactivateAsync(int centerId, DateOnly end, CancellationToken ct)
    {
        if (await db.TeamCenterAssignments.AnyAsync(x => x.CenterId == centerId && (!x.EffectiveTo.HasValue || x.EffectiveTo >= end), ct))
            throw new InvalidOperationException("CENTER_DEACTIVATION_BLOCKED：仍有生效或未來 TeamCenterAssignment。");
    }

    private async Task EnsureTeamRelationshipsWithinLifecycleAsync(int teamId, DateOnly from, DateOnly? to, CancellationToken ct)
    {
        var assignments = await db.TeamCenterAssignments.AsNoTracking().Where(x => x.TeamId == teamId).ToListAsync(ct);
        if (assignments.Any(x => !V180TeamCenterLifecycleRules.IsWithin(x.EffectiveFrom, x.EffectiveTo, from, to)))
            throw new InvalidOperationException("TEAM_LIFECYCLE_CONFLICT：既有或未來 TeamCenterAssignment 超出小組有效期間。");
        var memberships = await db.TeamMemberships.AsNoTracking().Where(x => x.TeamId == teamId).ToListAsync(ct);
        if (memberships.Any(x => !V180TeamCenterLifecycleRules.IsWithin(x.EffectiveFrom, x.EffectiveTo, from, to)))
            throw new InvalidOperationException("TEAM_LIFECYCLE_CONFLICT：既有或未來 TeamMembership 超出小組有效期間。");
        var leaders = await db.TeamLeaderAssignments.AsNoTracking().Where(x => x.TeamId == teamId).ToListAsync(ct);
        if (leaders.Any(x => !V180TeamCenterLifecycleRules.IsWithin(x.EffectiveFrom, x.EffectiveTo, from, to)))
            throw new InvalidOperationException("TEAM_LIFECYCLE_CONFLICT：既有或未來 TeamLeaderAssignment 超出小組有效期間。");
    }

    private async Task EnsureCenterAssignmentsWithinLifecycleAsync(int centerId, DateOnly from, DateOnly? to, CancellationToken ct)
    {
        var assignments = await db.TeamCenterAssignments.AsNoTracking().Where(x => x.CenterId == centerId).ToListAsync(ct);
        if (assignments.Any(x => !V180TeamCenterLifecycleRules.IsWithin(x.EffectiveFrom, x.EffectiveTo, from, to)))
            throw new InvalidOperationException("CENTER_LIFECYCLE_CONFLICT：既有或未來 TeamCenterAssignment 超出中心有效期間。");
    }

    private async Task EnsureNoAssignmentOverlapAsync(int teamId, DateOnly from, DateOnly? to,
        long? excludedId, CancellationToken ct)
    {
        var rows = await db.TeamCenterAssignments.AsNoTracking()
            .Where(x => x.TeamId == teamId && (!excludedId.HasValue || x.TeamCenterAssignmentId != excludedId.Value))
            .ToListAsync(ct);
        if (rows.Any(x => V180TeamCenterLifecycleRules.Overlaps(from, to, x.EffectiveFrom, x.EffectiveTo)))
            throw new InvalidOperationException("TEAM_CENTER_OVERLAP：Team-Center effective periods 不得重疊。");
    }

    private static void ValidateAssignment(Team team, Center center, DateOnly from, DateOnly? to)
    {
        V180TeamCenterLifecycleRules.ValidatePeriod(from, to, "Team-Center 有效期間");
        if (team.OrganizationId != center.OrganizationId)
            throw new InvalidOperationException("TEAM_CENTER_ORGANIZATION_MISMATCH：小組與中心必須屬於相同 Organization。");
        if (!V180TeamCenterLifecycleRules.IsWithin(from, to, team.EffectiveFrom, team.EffectiveTo))
            throw new InvalidOperationException("TEAM_CENTER_TEAM_LIFECYCLE：assignment 必須位於小組有效期間內。");
        if (!V180TeamCenterLifecycleRules.IsWithin(from, to, center.EffectiveFrom, center.EffectiveTo))
            throw new InvalidOperationException("TEAM_CENTER_CENTER_LIFECYCLE：assignment 必須位於中心有效期間內。");
    }

    private async Task<Team> FindTeamAsync(int teamId, int organizationId, CancellationToken ct) =>
        await db.Teams.SingleOrDefaultAsync(x => x.TeamId == teamId && x.OrganizationId == organizationId, ct)
        ?? throw new KeyNotFoundException("找不到小組。");

    private async Task<Center> FindCenterAsync(int centerId, int organizationId, CancellationToken ct) =>
        await db.Centers.SingleOrDefaultAsync(x => x.CenterId == centerId && x.OrganizationId == organizationId, ct)
        ?? throw new KeyNotFoundException("找不到中心。");

    private async Task<Center> FindCenterForAssignmentAsync(int centerId, CancellationToken ct) =>
        await db.Centers.SingleOrDefaultAsync(x => x.CenterId == centerId, ct)
        ?? throw new KeyNotFoundException("找不到中心。");

    private async Task<TeamCenterAssignment> FindAssignmentAsync(long assignmentId, int organizationId, CancellationToken ct) =>
        await db.TeamCenterAssignments.SingleOrDefaultAsync(x => x.TeamCenterAssignmentId == assignmentId &&
            db.Teams.Any(t => t.TeamId == x.TeamId && t.OrganizationId == organizationId), ct)
        ?? throw new KeyNotFoundException("找不到 Team-Center assignment。");

    private async Task EnsureOrganizationAsync(int organizationId, CancellationToken ct)
    {
        if (!await db.Organizations.AnyAsync(x => x.OrganizationId == organizationId && x.IsActive, ct))
            throw new InvalidOperationException("Organization 不存在或已停用。");
    }

    private static int RequireAdminOrganization(CurrentUserDto admin)
    {
        if (!admin.Roles.Contains("admin", StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("此操作僅限系統管理員。");
        return admin.OrganizationId ?? throw new InvalidOperationException("目前帳號缺少 OrganizationId。");
    }

    private static void EnsureInactiveHasEnd(bool active, DateOnly? end, string label)
    {
        if (!active && !end.HasValue) throw new InvalidOperationException($"停用的{label}必須有 EffectiveTo。");
    }

    private static void EnsureVersion(byte[] current, string supplied)
    {
        var expected = V180TeamCenterLifecycleRules.DecodeVersion(supplied);
        if (!current.SequenceEqual(expected))
            throw new InvalidOperationException("ROWVERSION_CONFLICT：資料已由其他人更新，請重新載入。");
    }

    private void AddAudit(int userId, string entityType, string? entityId, string action, object value) =>
        db.AuditLogs.Add(new AuditLog { UserId = userId, EntityType = entityType, EntityId = entityId,
            Action = action, NewValues = JsonSerializer.Serialize(value, JsonOptions),
            CorrelationId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow });

    private static V180TeamWriteResult Map(Team x) => new(x.TeamId, x.OrganizationId, x.TeamCode,
        x.TeamName, x.EffectiveFrom, x.EffectiveTo, x.IsActive, x.Notes, Convert.ToBase64String(x.RowVersion));
    private static V180CenterWriteResult Map(Center x) => new(x.CenterId, x.OrganizationId, x.CenterCode,
        x.CenterName, x.EffectiveFrom, x.EffectiveTo, x.IsActive, x.Notes, Convert.ToBase64String(x.RowVersion));
    private static V180TeamCenterAssignmentWriteResult Map(TeamCenterAssignment x) => new(
        x.TeamCenterAssignmentId, x.TeamId, x.CenterId, x.EffectiveFrom, x.EffectiveTo,
        x.ChangeReason, Convert.ToBase64String(x.RowVersion));
}
