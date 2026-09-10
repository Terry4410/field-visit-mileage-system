using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180TeamLocationNoteRow
{
    public long TeamLocationNoteId { get; set; }
    public int TeamId { get; set; }
    public int LocationId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<V180TeamLocationNoteHistoryRow> History { get; set; } = [];
}

public sealed class V180TeamLocationNoteHistoryRow
{
    public long TeamLocationNoteHistoryId { get; set; }
    public long TeamLocationNoteId { get; set; }
    public int TeamId { get; set; }
    public int LocationId { get; set; }
    public string Action { get; set; } = "";
    public string? OldNote { get; set; }
    public string? NewNote { get; set; }
    public string? ChangeReason { get; set; }
    public DateTime ChangedAt { get; set; }
    public int ChangedByUserId { get; set; }
    public V180TeamLocationNoteRow TeamLocationNote { get; set; } = null!;
}

public sealed class V180TeamLocationNoteDbContext(
    DbContextOptions<V180TeamLocationNoteDbContext> options) : DbContext(options)
{
    public DbSet<V180TeamLocationNoteRow> TeamLocationNotes => Set<V180TeamLocationNoteRow>();
    public DbSet<V180TeamLocationNoteHistoryRow> TeamLocationNoteHistory => Set<V180TeamLocationNoteHistoryRow>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<UserIdentityProfile> UserIdentityProfiles => Set<UserIdentityProfile>();
    public DbSet<Employment> Employments => Set<Employment>();
    public DbSet<EmploymentStatusPeriod> EmploymentStatusPeriods => Set<EmploymentStatusPeriod>();
    public DbSet<EmploymentRoleAssignment> EmploymentRoleAssignments => Set<EmploymentRoleAssignment>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<TeamLeaderAssignment> TeamLeaderAssignments => Set<TeamLeaderAssignment>();
    public DbSet<TeamLeaderDelegation> TeamLeaderDelegations => Set<TeamLeaderDelegation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<V180TeamLocationNoteRow>(e =>
        {
            e.ToTable("TeamLocationNotes");
            e.HasKey(x => x.TeamLocationNoteId);
            e.Property(x => x.TeamLocationNoteId).ValueGeneratedOnAdd();
            e.Property(x => x.Note).HasMaxLength(1000).IsRequired(false);
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.TeamId, x.LocationId })
                .IsUnique().HasDatabaseName("UQ_TeamLocationNotes_Team_Location");
            e.HasMany(x => x.History).WithOne(x => x.TeamLocationNote)
                .HasForeignKey(x => x.TeamLocationNoteId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<V180TeamLocationNoteHistoryRow>(e =>
        {
            e.ToTable("TeamLocationNoteHistory");
            e.HasKey(x => x.TeamLocationNoteHistoryId);
            e.Property(x => x.TeamLocationNoteHistoryId).ValueGeneratedOnAdd();
            e.Property(x => x.Action).HasMaxLength(20);
            e.Property(x => x.OldNote).HasMaxLength(1000);
            e.Property(x => x.NewNote).HasMaxLength(1000);
            e.Property(x => x.ChangeReason).HasMaxLength(500);
            e.HasIndex(x => new { x.TeamLocationNoteId, x.ChangedAt })
                .HasDatabaseName("IX_TeamLocationNoteHistory_Note_Changed");
        });

        b.Entity<Team>().ToTable("Teams");
        b.Entity<Location>().ToTable("Locations");
        b.Entity<UserIdentityProfile>(e =>
        {
            e.ToTable("UserIdentityProfiles");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).ValueGeneratedNever();
        });
        b.Entity<Employment>().ToTable("Employments");
        b.Entity<EmploymentStatusPeriod>().ToTable("EmploymentStatusPeriods");
        b.Entity<EmploymentRoleAssignment>().ToTable("EmploymentRoleAssignments");
        b.Entity<Role>().ToTable("Roles");
        b.Entity<TeamMembership>().ToTable("TeamMemberships");
        b.Entity<TeamLeaderAssignment>().ToTable("TeamLeaderAssignments");
        b.Entity<TeamLeaderDelegation>().ToTable("TeamLeaderDelegations");
    }
}

public sealed class V180TeamLocationNoteService(
    V180TeamLocationNoteDbContext db) : IV180TeamLocationNoteService
{
    public async Task<V180TeamLocationNoteDto> GetAsync(
        CurrentUserDto actor, int teamId, int locationId, CancellationToken ct)
    {
        var team = await RequireTeamAuthorityAsync(actor, teamId, write: false, ct);
        await RequireLocationForTeamAsync(team, locationId, ct);

        var row = await db.TeamLocationNotes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TeamId == teamId && x.LocationId == locationId, ct);

        return row is null
            ? new V180TeamLocationNoteDto(
                V180TeamLocationNoteStates.NeverExisted, null, teamId, locationId,
                null, null, null)
            : Map(row);
    }

    public async Task<IReadOnlyList<TeamDto>> GetWritableTeamsAsync(
        CurrentUserDto actor, CancellationToken ct)
    {
        var authority = await ResolveActorAsync(actor, ct);
        var today = BusinessTime.Today;
        int[] teamIds;

        if (HasPersona(actor, authority, "admin"))
        {
            teamIds = await db.Teams.AsNoTracking()
                .Where(x => x.OrganizationId == authority.OrganizationId &&
                    x.IsActive && (!x.EffectiveFrom.HasValue || x.EffectiveFrom <= today) &&
                    (!x.EffectiveTo.HasValue || today <= x.EffectiveTo.Value))
                .Select(x => x.TeamId).ToArrayAsync(ct);
        }
        else if (HasPersona(actor, authority, "leader"))
        {
            teamIds = await ResolveLeaderTeamIdsAsync(authority.EmploymentId, authority.OrganizationId, today, ct);
        }
        else
        {
            throw new UnauthorizedAccessException("只有 Leader 或 Admin 可以維護小組地點備註。");
        }

        return await db.Teams.AsNoTracking()
            .Where(x => teamIds.Contains(x.TeamId))
            .OrderBy(x => x.TeamName).ThenBy(x => x.TeamId)
            .Select(x => new TeamDto(x.TeamId, x.OrganizationId, x.TeamCode, x.TeamName))
            .ToListAsync(ct);
    }

    public async Task<V180TeamLocationNoteDto> CreateAsync(
        CurrentUserDto actor, int teamId, V180CreateTeamLocationNoteRequest request, CancellationToken ct)
    {
        var team = await RequireTeamAuthorityAsync(actor, teamId, write: true, ct);
        await RequireLocationForTeamAsync(team, request.LocationId, ct);
        var note = NormalizeNote(request.Note);
        var reason = NormalizeReason(request.ChangeReason);

        if (await db.TeamLocationNotes.AsNoTracking()
            .AnyAsync(x => x.TeamId == teamId && x.LocationId == request.LocationId, ct))
            throw DuplicateConflict();

        var now = DateTime.UtcNow;
        var row = new V180TeamLocationNoteRow
        {
            TeamId = teamId,
            LocationId = request.LocationId,
            Note = note,
            CreatedAt = now,
            CreatedByUserId = actor.UserId
        };
        row.History.Add(new V180TeamLocationNoteHistoryRow
        {
            TeamId = teamId,
            LocationId = request.LocationId,
            Action = "Created",
            OldNote = null,
            NewNote = note,
            ChangeReason = reason,
            ChangedAt = now,
            ChangedByUserId = actor.UserId
        });

        await db.TeamLocationNotes.AddAsync(row, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is SqlException sql && sql.Number is 2601 or 2627)
        {
            throw DuplicateConflict(ex);
        }

        return Map(row);
    }

    public async Task<V180TeamLocationNoteDto> UpdateAsync(
        CurrentUserDto actor, long teamLocationNoteId,
        V180UpdateTeamLocationNoteRequest request, CancellationToken ct)
    {
        var row = await RequireRowAsync(teamLocationNoteId, ct);
        await RequireTeamAuthorityAsync(actor, row.TeamId, write: true, ct);
        var note = NormalizeNote(request.Note);
        var reason = NormalizeReason(request.ChangeReason);
        EnsureVersion(row, request.RowVersion);

        var oldNote = row.Note;
        var now = DateTime.UtcNow;
        row.Note = note;
        row.UpdatedAt = now;
        row.UpdatedByUserId = actor.UserId;
        row.History.Add(new V180TeamLocationNoteHistoryRow
        {
            TeamId = row.TeamId,
            LocationId = row.LocationId,
            Action = "Updated",
            OldNote = oldNote,
            NewNote = note,
            ChangeReason = reason,
            ChangedAt = now,
            ChangedByUserId = actor.UserId
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw RowVersionConflict(ex);
        }

        return Map(row);
    }

    public async Task<V180TeamLocationNoteDto> ClearAsync(
        CurrentUserDto actor, long teamLocationNoteId,
        V180ClearTeamLocationNoteRequest request, CancellationToken ct)
    {
        var row = await RequireRowAsync(teamLocationNoteId, ct);
        await RequireTeamAuthorityAsync(actor, row.TeamId, write: true, ct);

        if (row.Note is null)
            return Map(row);

        EnsureVersion(row, request.RowVersion);
        var reason = NormalizeReason(request.ChangeReason);
        var oldNote = row.Note;
        var now = DateTime.UtcNow;
        row.Note = null;
        row.UpdatedAt = now;
        row.UpdatedByUserId = actor.UserId;
        row.History.Add(new V180TeamLocationNoteHistoryRow
        {
            TeamId = row.TeamId,
            LocationId = row.LocationId,
            Action = "Cleared",
            OldNote = oldNote,
            NewNote = null,
            ChangeReason = reason,
            ChangedAt = now,
            ChangedByUserId = actor.UserId
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return Map(row);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            db.ChangeTracker.Clear();
            var current = await db.TeamLocationNotes.AsNoTracking()
                .SingleOrDefaultAsync(x => x.TeamLocationNoteId == teamLocationNoteId, ct);
            if (current is not null && current.Note is null)
                return Map(current);
            throw RowVersionConflict(ex);
        }
    }

    private async Task<V180TeamLocationNoteRow> RequireRowAsync(long id, CancellationToken ct) =>
        await db.TeamLocationNotes.Include(x => x.History)
            .SingleOrDefaultAsync(x => x.TeamLocationNoteId == id, ct)
        ?? throw new KeyNotFoundException("找不到 Team Location Note。");

    private async Task<Team> RequireTeamAuthorityAsync(
        CurrentUserDto actor, int teamId, bool write, CancellationToken ct)
    {
        var today = BusinessTime.Today;
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(x => x.TeamId == teamId, ct)
            ?? throw new KeyNotFoundException("找不到 Team。");
        if (!team.IsActive || (team.EffectiveFrom.HasValue && today < team.EffectiveFrom.Value) ||
            (team.EffectiveTo.HasValue && team.EffectiveTo.Value < today))
            throw new UnauthorizedAccessException("目前 Team 不在有效期間，無法存取小組地點備註。");

        var authority = await ResolveActorAsync(actor, ct);
        if (team.OrganizationId != authority.OrganizationId)
            throw new UnauthorizedAccessException("無權存取其他 Organization 的 Team Location Note。");

        if (HasPersona(actor, authority, "admin"))
            return team;

        if (HasPersona(actor, authority, "leader") &&
            await HasLeaderAuthorityAsync(authority.EmploymentId, teamId, today, ct))
            return team;

        if (!write && HasPersona(actor, authority, "visitor") &&
            await HasMembershipAsync(authority.EmploymentId, teamId, today, ct))
            return team;

        throw new UnauthorizedAccessException(
            write
                ? "目前身分沒有此 Team 的 Team Location Note 寫入權限。"
                : "目前身分沒有此 Team 的 Team Location Note 讀取權限。");
    }

    private async Task RequireLocationForTeamAsync(Team team, int locationId, CancellationToken ct)
    {
        var location = await db.Locations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.LocationId == locationId, ct)
            ?? throw new KeyNotFoundException("找不到 Location。");

        if (location.OrganizationId.HasValue && location.OrganizationId.Value != team.OrganizationId)
            throw new UnauthorizedAccessException("無權以其他 Organization 的 Location 建立 Team Note。");
        if (location.TeamId.HasValue && location.TeamId.Value != team.TeamId)
            throw new UnauthorizedAccessException("此 Location 屬於其他 Team；只有 shared/global Location 可跨 Team 使用。");
    }

    private async Task<ActorAuthority> ResolveActorAsync(CurrentUserDto actor, CancellationToken ct)
    {
        var profile = await db.UserIdentityProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == actor.UserId, ct);
        if (profile is null || !profile.EmploymentId.HasValue)
            throw new UnauthorizedAccessException("目前 UserId 沒有 v1.8 authoritative Employment 綁定。");

        var employment = await db.Employments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.EmploymentId == profile.EmploymentId.Value, ct)
            ?? throw new UnauthorizedAccessException("authoritative Employment 不存在。");
        if (actor.OrganizationId.HasValue && actor.OrganizationId.Value != employment.OrganizationId)
            throw new UnauthorizedAccessException("登入 Organization 與 authoritative Employment 不一致。");

        var today = BusinessTime.Today;
        var statusRows = await db.EmploymentStatusPeriods.AsNoTracking()
            .Where(x => x.EmploymentId == employment.EmploymentId).ToListAsync(ct);
        var status = V180AsOfRules.EmploymentStatus(statusRows, today);
        if (status is null || !status.EmploymentStatus.Equals(EmploymentStatuses.Active, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("目前 Employment 不是有效在職狀態。");

        var assignmentRows = await db.EmploymentRoleAssignments.AsNoTracking()
            .Where(x => x.EmploymentId == employment.EmploymentId).ToListAsync(ct);
        var roleAssignments = V180AsOfRules.EmploymentRoles(assignmentRows, today);
        var roleIds = roleAssignments.Select(x => x.RoleId).Distinct().ToArray();
        var roleCodes = await db.Roles.AsNoTracking()
            .Where(x => roleIds.Contains(x.RoleId) && x.IsActive)
            .Select(x => x.RoleCode.ToLower()).ToListAsync(ct);

        return new ActorAuthority(
            employment.EmploymentId,
            employment.OrganizationId,
            roleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private async Task<bool> HasMembershipAsync(
        long employmentId, int teamId, DateOnly today, CancellationToken ct)
    {
        var rows = await db.TeamMemberships.AsNoTracking()
            .Where(x => x.EmploymentId == employmentId && x.TeamId == teamId)
            .ToListAsync(ct);
        return V180AsOfRules.TeamMemberships(rows, today).Count == 1;
    }

    private async Task<bool> HasLeaderAuthorityAsync(
        long employmentId, int teamId, DateOnly today, CancellationToken ct)
    {
        var rows = await db.TeamLeaderAssignments.AsNoTracking()
            .Where(x => x.TeamId == teamId).ToListAsync(ct);
        var effective = V180AsOfRules.TeamLeaders(rows, today);
        if (effective.Any(x => x.EmploymentId == employmentId))
            return true;

        var assignmentIds = effective.Select(x => x.TeamLeaderAssignmentId).ToArray();
        if (assignmentIds.Length == 0)
            return false;
        var delegations = await db.TeamLeaderDelegations.AsNoTracking()
            .Where(x => assignmentIds.Contains(x.TeamLeaderAssignmentId)).ToListAsync(ct);

        foreach (var assignment in effective)
        {
            var delegation = V180AsOfRules.DelegatedLeader(
                delegations.Where(x => x.TeamLeaderAssignmentId == assignment.TeamLeaderAssignmentId), today);
            if (delegation?.DelegateEmploymentId == employmentId)
                return true;
        }
        return false;
    }

    private async Task<int[]> ResolveLeaderTeamIdsAsync(
        long employmentId, int organizationId, DateOnly today, CancellationToken ct)
    {
        var teams = await db.Teams.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.IsActive &&
                (!x.EffectiveFrom.HasValue || x.EffectiveFrom <= today) &&
                (!x.EffectiveTo.HasValue || today <= x.EffectiveTo.Value))
            .ToListAsync(ct);
        var result = new List<int>();
        foreach (var team in teams)
            if (await HasLeaderAuthorityAsync(employmentId, team.TeamId, today, ct))
                result.Add(team.TeamId);
        return result.Distinct().OrderBy(x => x).ToArray();
    }

    private static bool HasPersona(CurrentUserDto actor, ActorAuthority authority, string role) =>
        actor.Roles.Contains(role, StringComparer.OrdinalIgnoreCase) && authority.RoleCodes.Contains(role);

    private static string NormalizeNote(string? value)
    {
        var note = value?.Trim();
        if (string.IsNullOrWhiteSpace(note))
            throw new InvalidOperationException("TEAM_LOCATION_NOTE_REQUIRED：Note 不得為 NULL、空字串或只有空白。");
        if (note.Length > 1000)
            throw new InvalidOperationException("Team Location Note 不得超過 1000 個字元。");
        return note;
    }

    private static string? NormalizeReason(string? value)
    {
        var reason = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (reason?.Length > 500)
            throw new InvalidOperationException("ChangeReason 不得超過 500 個字元。");
        return reason;
    }

    private static void EnsureVersion(V180TeamLocationNoteRow row, string version)
    {
        var expected = V180PeopleAccessRules.DecodeVersion(version);
        if (!row.RowVersion.SequenceEqual(expected))
            throw RowVersionConflict();
    }

    private static DbUpdateConcurrencyException DuplicateConflict(Exception? inner = null) =>
        inner is null
            ? new DbUpdateConcurrencyException("TEAM_LOCATION_NOTE_CONFLICT：此 Team/Location 已存在 logical Note row；請重新載入後使用 Update/Restore。")
            : new DbUpdateConcurrencyException("TEAM_LOCATION_NOTE_CONFLICT：此 Team/Location 已由其他請求建立；請重新載入。", inner);

    private static InvalidOperationException RowVersionConflict(Exception? inner = null) =>
        new("ROWVERSION_CONFLICT：Team Location Note 已由其他人更新，請重新載入。", inner);

    private static V180TeamLocationNoteDto Map(V180TeamLocationNoteRow row) =>
        new(
            row.Note is null ? V180TeamLocationNoteStates.Cleared : V180TeamLocationNoteStates.Active,
            row.TeamLocationNoteId,
            row.TeamId,
            row.LocationId,
            row.Note,
            Convert.ToBase64String(row.RowVersion),
            row.UpdatedAt ?? row.CreatedAt);

    private sealed record ActorAuthority(
        long EmploymentId,
        int OrganizationId,
        HashSet<string> RoleCodes);
}
