using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain;
using FieldVisit.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

/// <summary>Admin-maintained VAL-B1-002 source. All reads are fresh per write.</summary>
public sealed class V180LeaderGrantAdminService(AppDbContext db, ICurrentUserService current)
{
    public async Task<V180LeaderGrantSnapshotDto> QueryAsync(V180LeaderGrantQueryRequest request, CancellationToken ct)
    {
        var admin = await V180CurrentAdminWriteGuard.RequireAsync(db, current.GetRequired(), ct);
        var orgId = admin.OrganizationId!.Value;
        var today = BusinessTime.Today;
        var assignments = db.TeamLeaderAssignments.AsNoTracking()
            .Join(db.Teams.AsNoTracking(), x => x.TeamId, t => t.TeamId, (x, t) => new { x, t })
            .Join(db.Employments.AsNoTracking(), x => x.x.EmploymentId, e => e.EmploymentId, (x, e) => new { x.x, x.t, e })
            .Where(x => x.t.OrganizationId == orgId);
        if (request.TeamId.HasValue) assignments = assignments.Where(x => x.x.TeamId == request.TeamId.Value);
        if (request.UserId.HasValue) assignments = assignments.Where(x => x.e.LegacyUserId == request.UserId.Value);
        if (request.CurrentOnly == true) assignments = assignments.Where(x => x.x.GrantStatus == "Active" && x.x.EffectiveFrom <= today && (!x.x.EffectiveTo.HasValue || x.x.EffectiveTo >= today) && !x.x.RevokedAt.HasValue);
        var appointmentRows = await assignments.OrderByDescending(x => x.x.EffectiveFrom).Select(x => new V180LeaderAppointmentDto(
            x.x.TeamLeaderAssignmentId, x.x.TeamId, x.t.TeamName, x.e.LegacyUserId ?? 0, x.e.EmployeeNo ?? "", x.x.EffectiveFrom, x.x.EffectiveTo,
            x.x.GrantStatus, x.x.AppointmentType, x.x.ProvenanceType, x.x.EvidenceReference, x.x.RevokedAt, x.x.RevocationReason, Convert.ToBase64String(x.x.RowVersion))).ToListAsync(ct);
        var delegations = db.TeamLeaderDelegations.AsNoTracking()
            .Join(db.TeamLeaderAssignments.AsNoTracking(), d => d.TeamLeaderAssignmentId, a => a.TeamLeaderAssignmentId, (d, a) => new { d, a })
            .Join(db.Teams.AsNoTracking(), x => x.a.TeamId, t => t.TeamId, (x, t) => new { x.d, x.a, t })
            .Join(db.Employments.AsNoTracking(), x => x.d.DelegateEmploymentId, e => e.EmploymentId, (x, e) => new { x.d, x.a, x.t, e })
            .Where(x => x.t.OrganizationId == orgId);
        if (request.TeamId.HasValue) delegations = delegations.Where(x => x.a.TeamId == request.TeamId.Value);
        if (request.UserId.HasValue) delegations = delegations.Where(x => x.e.LegacyUserId == request.UserId.Value);
        if (request.CurrentOnly == true) delegations = delegations.Where(x => x.d.GrantStatus == "Active" && x.d.EffectiveFrom <= today && x.d.EffectiveTo >= today && !x.d.RevokedAt.HasValue);
        var delegationRows = await delegations.OrderByDescending(x => x.d.EffectiveFrom).Select(x => new V180LeaderDelegationDto(
            x.d.TeamLeaderDelegationId, x.a.TeamLeaderAssignmentId, x.a.TeamId, x.t.TeamName, x.e.LegacyUserId ?? 0, x.e.EmployeeNo ?? "", x.d.EffectiveFrom, x.d.EffectiveTo,
            x.d.GrantStatus, x.d.Scope, x.d.EvidenceReference, x.d.RevokedAt, x.d.RevocationReason, Convert.ToBase64String(x.d.RowVersion))).ToListAsync(ct);
        return new(appointmentRows, delegationRows);
    }

    public async Task<V180LeaderAppointmentDto> CreateAppointmentAsync(V180CreateLeaderAppointmentRequest request, CancellationToken ct)
    {
        var admin = await V180CurrentAdminWriteGuard.RequireAsync(db, current.GetRequired(), ct);
        Validate(request.EffectiveFrom, request.EffectiveTo, request.EvidenceReference, request.ApprovedReason);
        if (request.LeaderUserId == admin.UserId) throw new UnauthorizedAccessException("LEADER_GRANT_SELF_GRANT_DENIED");
        var employment = await RequireEligibleLeaderAsync(request.LeaderUserId, admin.OrganizationId!.Value, request.EffectiveFrom, ct);
        await RequireTeamAsync(request.TeamId, admin.OrganizationId.Value, ct);
        if (await db.TeamLeaderAssignments.AnyAsync(x => x.TeamId == request.TeamId && x.EmploymentId == employment.EmploymentId && x.GrantStatus == "Active" && !x.RevokedAt.HasValue && x.EffectiveFrom <= (request.EffectiveTo ?? DateOnly.MaxValue) && request.EffectiveFrom <= (x.EffectiveTo ?? DateOnly.MaxValue), ct))
            throw new InvalidOperationException("LEADER_APPOINTMENT_OVERLAP");
        var row = new TeamLeaderAssignment { TeamId = request.TeamId, EmploymentId = employment.EmploymentId, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, AssignedByUserId = admin.UserId, CreatedAt = DateTime.UtcNow, GrantStatus = "Active", AppointmentType = "FormalAppointment", ProvenanceType = "AdminMaintained", EvidenceReference = request.EvidenceReference.Trim(), ApprovedByUserId = admin.UserId, ApprovedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, UpdatedByUserId = admin.UserId };
        db.TeamLeaderAssignments.Add(row);
        AddAudit(admin.UserId, "TeamLeaderAssignment", null, "LeaderAppointmentCreate", new { request.TeamId, request.LeaderUserId, request.EffectiveFrom, request.EffectiveTo, Evidence = row.EvidenceReference, request.ApprovedReason });
        await db.SaveChangesAsync(ct);
        return (await QueryAsync(new(UserId: request.LeaderUserId), ct)).Appointments.Single(x => x.GrantId == row.TeamLeaderAssignmentId);
    }

    public async Task<V180LeaderDelegationDto> CreateDelegationAsync(V180CreateLeaderDelegationRequest request, CancellationToken ct)
    {
        var admin = await V180CurrentAdminWriteGuard.RequireAsync(db, current.GetRequired(), ct);
        Validate(request.EffectiveFrom, request.EffectiveTo, request.EvidenceReference, request.Reason);
        var appointment = await db.TeamLeaderAssignments.SingleOrDefaultAsync(x => x.TeamLeaderAssignmentId == request.AppointmentGrantId, ct) ?? throw new KeyNotFoundException("LEADER_APPOINTMENT_NOT_FOUND");
        var team = await RequireTeamAsync(appointment.TeamId, admin.OrganizationId!.Value, ct);
        var adminEmploymentId = await db.UserIdentityProfiles.AsNoTracking().Where(x => x.UserId == admin.UserId && x.UserType == UserTypes.Internal).Select(x => x.EmploymentId).SingleOrDefaultAsync(ct);
        if (adminEmploymentId.HasValue && appointment.EmploymentId == adminEmploymentId.Value) throw new UnauthorizedAccessException("LEADER_DELEGATION_SELF_AUTHORITY_EXPANSION_DENIED");
        if (appointment.GrantStatus != "Active" || appointment.RevokedAt.HasValue || !V180MasterDataValidationService.Covers(appointment.EffectiveFrom, appointment.EffectiveTo, request.EffectiveFrom, request.EffectiveTo) || appointment.ProvenanceType != "AdminMaintained" || string.IsNullOrWhiteSpace(appointment.EvidenceReference)) throw new UnauthorizedAccessException("LEADER_DELEGATION_SOURCE_NOT_TRUSTED");
        var delegateEmployment = await RequireEligibleLeaderAsync(request.DelegateUserId, team.OrganizationId, request.EffectiveFrom, ct);
        if (delegateEmployment.EmploymentId == appointment.EmploymentId || request.DelegateUserId == admin.UserId) throw new UnauthorizedAccessException("LEADER_DELEGATION_SELF_OR_CIRCULAR_DENIED");
        if (await db.TeamLeaderDelegations.AnyAsync(x => x.TeamLeaderAssignmentId == appointment.TeamLeaderAssignmentId && x.DelegateEmploymentId == delegateEmployment.EmploymentId && x.GrantStatus == "Active" && !x.RevokedAt.HasValue && x.EffectiveFrom <= request.EffectiveTo && request.EffectiveFrom <= x.EffectiveTo, ct)) throw new InvalidOperationException("LEADER_DELEGATION_OVERLAP");
        var row = new TeamLeaderDelegation { TeamLeaderAssignmentId = appointment.TeamLeaderAssignmentId, DelegateEmploymentId = delegateEmployment.EmploymentId, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, Reason = request.Reason.Trim(), CreatedAt = DateTime.UtcNow, CreatedByUserId = admin.UserId, GrantStatus = "Active", Scope = "LocationMaintenance", ProvenanceType = "AdminMaintained", EvidenceReference = request.EvidenceReference.Trim(), ApprovedByUserId = admin.UserId, ApprovedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, UpdatedByUserId = admin.UserId };
        db.TeamLeaderDelegations.Add(row);
        AddAudit(admin.UserId, "TeamLeaderDelegation", null, "LeaderDelegationCreate", new { request.AppointmentGrantId, request.DelegateUserId, request.EffectiveFrom, request.EffectiveTo, Evidence = row.EvidenceReference });
        await db.SaveChangesAsync(ct);
        return (await QueryAsync(new(UserId: request.DelegateUserId), ct)).Delegations.Single(x => x.GrantId == row.TeamLeaderDelegationId);
    }

    public async Task EndAppointmentAsync(long id, V180EndLeaderGrantRequest request, CancellationToken ct) => await EndAsync(id, request, true, ct);
    public async Task EndDelegationAsync(long id, V180EndLeaderGrantRequest request, CancellationToken ct) => await EndAsync(id, request, false, ct);

    private async Task EndAsync(long id, V180EndLeaderGrantRequest request, bool appointment, CancellationToken ct)
    {
        var admin = await V180CurrentAdminWriteGuard.RequireAsync(db, current.GetRequired(), ct);
        if (request.EffectiveTo < BusinessTime.Today) throw new InvalidOperationException("LEADER_GRANT_RETROACTIVE_END_DENIED");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("LEADER_GRANT_REVOCATION_REASON_REQUIRED");
        if (appointment) { var row = await db.TeamLeaderAssignments.SingleOrDefaultAsync(x => x.TeamLeaderAssignmentId == id, ct) ?? throw new KeyNotFoundException(); V180MasterDataValidationService.RowVersion(request.RowVersion, row.RowVersion); await RequireTeamAsync(row.TeamId, admin.OrganizationId!.Value, ct); if (request.EffectiveTo < row.EffectiveFrom) throw new InvalidOperationException("INVALID_EFFECTIVE_PERIOD"); row.EffectiveTo = request.EffectiveTo; row.GrantStatus = "Revoked"; row.RevokedAt = DateTime.UtcNow; row.RevokedByUserId = admin.UserId; row.RevocationReason = request.Reason.Trim(); row.UpdatedAt = DateTime.UtcNow; row.UpdatedByUserId = admin.UserId; AddAudit(admin.UserId, "TeamLeaderAssignment", id.ToString(), "LeaderAppointmentRevoke", new { request.EffectiveTo, request.Reason }); }
        else { var row = await db.TeamLeaderDelegations.SingleOrDefaultAsync(x => x.TeamLeaderDelegationId == id, ct) ?? throw new KeyNotFoundException(); V180MasterDataValidationService.RowVersion(request.RowVersion, row.RowVersion); var teamId = await db.TeamLeaderAssignments.Where(x => x.TeamLeaderAssignmentId == row.TeamLeaderAssignmentId).Select(x => x.TeamId).SingleAsync(ct); await RequireTeamAsync(teamId, admin.OrganizationId!.Value, ct); if (request.EffectiveTo < row.EffectiveFrom) throw new InvalidOperationException("INVALID_EFFECTIVE_PERIOD"); row.EffectiveTo = request.EffectiveTo; row.GrantStatus = "Revoked"; row.RevokedAt = DateTime.UtcNow; row.RevokedByUserId = admin.UserId; row.RevocationReason = request.Reason.Trim(); row.UpdatedAt = DateTime.UtcNow; row.UpdatedByUserId = admin.UserId; AddAudit(admin.UserId, "TeamLeaderDelegation", id.ToString(), "LeaderDelegationRevoke", new { request.EffectiveTo, request.Reason }); }
        await db.SaveChangesAsync(ct);
    }

    private async Task<Team> RequireTeamAsync(int teamId, int orgId, CancellationToken ct) => await db.Teams.SingleOrDefaultAsync(x => x.TeamId == teamId && x.OrganizationId == orgId && x.IsActive, ct) ?? throw new UnauthorizedAccessException("LEADER_GRANT_TEAM_SCOPE_DENIED");
    private async Task<Employment> RequireEligibleLeaderAsync(int userId, int orgId, DateOnly asOf, CancellationToken ct)
    {
        var profile = await db.UserIdentityProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.UserType == UserTypes.Internal, ct) ?? throw new UnauthorizedAccessException("LEADER_GRANT_INTERNAL_IDENTITY_REQUIRED");
        if (!profile.EmploymentId.HasValue) throw new UnauthorizedAccessException("LEADER_GRANT_EMPLOYMENT_REQUIRED");
        var employment = await db.Employments.SingleOrDefaultAsync(x => x.EmploymentId == profile.EmploymentId.Value && x.OrganizationId == orgId, ct) ?? throw new UnauthorizedAccessException("LEADER_GRANT_ORGANIZATION_DENIED");
        var hasLeaderRole = await (from role in db.UserRoleAssignments join r in db.Roles on role.RoleId equals r.RoleId where role.UserId == userId && r.IsActive && r.RoleCode == "leader" && role.EffectiveFrom <= asOf && (!role.EffectiveTo.HasValue || role.EffectiveTo >= asOf) select role.UserRoleAssignmentId).AnyAsync(ct);
        if (!hasLeaderRole) throw new UnauthorizedAccessException("LEADER_GRANT_EFFECTIVE_ROLE_REQUIRED");
        var active = await db.EmploymentStatusPeriods.AnyAsync(x => x.EmploymentId == employment.EmploymentId && x.EmploymentStatus == EmploymentStatuses.Active && x.EffectiveFrom <= asOf && (!x.EffectiveTo.HasValue || x.EffectiveTo >= asOf), ct);
        if (!active) throw new UnauthorizedAccessException("LEADER_GRANT_HR_STATUS_DENIED");
        return employment;
    }
    private static void Validate(DateOnly from, DateOnly? to, string evidence, string? reason) { V180MasterDataValidationService.Period(from, to); if (string.IsNullOrWhiteSpace(evidence) || evidence.Trim().Length > 200) throw new InvalidOperationException("LEADER_GRANT_EVIDENCE_REQUIRED"); if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) throw new InvalidOperationException("LEADER_GRANT_REASON_REQUIRED"); }
    private void AddAudit(int userId, string type, string? id, string action, object values) => db.AuditLogs.Add(new AuditLog { UserId = userId, EntityType = type, EntityId = id, Action = action, NewValues = JsonSerializer.Serialize(values), CreatedAt = DateTime.UtcNow });
}

/// <summary>Fresh DB authorization for Location writes; never trust a JWT team claim.</summary>
public sealed class V180LeaderLocationAuthorizationService(AppDbContext db)
{
    public async Task<IReadOnlyList<int>> TrustedManagedTeamsAsync(CurrentUserDto user, CancellationToken ct)
    {
        if (!user.OrganizationId.HasValue) return [];
        var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.UserId && x.OrganizationId == user.OrganizationId, ct);
        if (account is null || !(await new V170AccessControl(db).EvaluateLoginAsync(user.UserId, account.IsActive, ct)).IsAllowed) return [];
        var today = BusinessTime.Today;
        var profile = await db.UserIdentityProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.UserId && x.UserType == UserTypes.Internal, ct);
        if (profile?.EmploymentId is not long employmentId) return [];
        var leaderRole = await (from a in db.UserRoleAssignments.AsNoTracking() join r in db.Roles.AsNoTracking() on a.RoleId equals r.RoleId join p in db.UserRoles.AsNoTracking() on a.RoleId equals p.RoleId where a.UserId == user.UserId && p.UserId == user.UserId && r.IsActive && r.RoleCode == "leader" && a.EffectiveFrom <= today && (!a.EffectiveTo.HasValue || a.EffectiveTo >= today) select a.UserRoleAssignmentId).AnyAsync(ct);
        if (!leaderRole) return [];
        var direct = from a in db.TeamLeaderAssignments.AsNoTracking()
                     join t in db.Teams.AsNoTracking() on a.TeamId equals t.TeamId
                     join m in db.TeamMemberships.AsNoTracking() on new { a.TeamId, EmploymentId = employmentId } equals new { m.TeamId, m.EmploymentId }
                     join u in db.UserTeamAssignments.AsNoTracking() on new { UserId = user.UserId, a.TeamId } equals new { u.UserId, u.TeamId }
                     where t.OrganizationId == user.OrganizationId && t.IsActive && a.EmploymentId == employmentId && a.GrantStatus == "Active" && a.AppointmentType == "FormalAppointment" && a.ProvenanceType == "AdminMaintained" && a.EvidenceReference != null && a.ApprovedByUserId != null && a.ApprovedAt != null && !a.RevokedAt.HasValue && a.EffectiveFrom <= today && (!a.EffectiveTo.HasValue || a.EffectiveTo >= today) && m.EffectiveFrom <= today && (!m.EffectiveTo.HasValue || m.EffectiveTo >= today) && u.EffectiveFrom <= today && (!u.EffectiveTo.HasValue || u.EffectiveTo >= today) select a.TeamId;
        var delegated = from d in db.TeamLeaderDelegations.AsNoTracking()
                        join a in db.TeamLeaderAssignments.AsNoTracking() on d.TeamLeaderAssignmentId equals a.TeamLeaderAssignmentId
                        join t in db.Teams.AsNoTracking() on a.TeamId equals t.TeamId
                        join m in db.TeamMemberships.AsNoTracking() on new { a.TeamId, EmploymentId = employmentId } equals new { m.TeamId, m.EmploymentId }
                        join u in db.UserTeamAssignments.AsNoTracking() on new { UserId = user.UserId, a.TeamId } equals new { u.UserId, u.TeamId }
                        where t.OrganizationId == user.OrganizationId && t.IsActive && d.DelegateEmploymentId == employmentId && d.GrantStatus == "Active" && d.Scope == "LocationMaintenance" && d.ProvenanceType == "AdminMaintained" && d.EvidenceReference != null && d.ApprovedByUserId != null && d.ApprovedAt != null && !d.RevokedAt.HasValue && d.EffectiveFrom <= today && d.EffectiveTo >= today && a.GrantStatus == "Active" && a.AppointmentType == "FormalAppointment" && a.ProvenanceType == "AdminMaintained" && a.EvidenceReference != null && a.ApprovedByUserId != null && a.ApprovedAt != null && !a.RevokedAt.HasValue && a.EffectiveFrom <= today && (!a.EffectiveTo.HasValue || a.EffectiveTo >= today) && m.EffectiveFrom <= today && (!m.EffectiveTo.HasValue || m.EffectiveTo >= today) && u.EffectiveFrom <= today && (!u.EffectiveTo.HasValue || u.EffectiveTo >= today) select a.TeamId;
        return await direct.Concat(delegated).Distinct().ToListAsync(ct);
    }
}
