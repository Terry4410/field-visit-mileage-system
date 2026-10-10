using System.Text.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FieldVisit.Infrastructure;

// Schema draft only: these tables MUST NOT be assumed to exist in UAT.
public sealed class V180B3ChangeRequest
{
    public long ChangeRequestId { get; set; }
    public Guid RequestPublicId { get; set; }
    public int OrganizationId { get; set; }
    public int? TeamId { get; set; }
    public string EntityKind { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string OperationCode { get; set; } = "";
    public string RiskCode { get; set; } = "";
    public byte[]? ExpectedEntityRowVersion { get; set; }
    public string? BeforeJson { get; set; }
    public string ProposedJson { get; set; } = "{}";
    public string? EvidenceJson { get; set; }
    public int RequestedByUserId { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string Status { get; set; } = "Pending";
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewReason { get; set; }
    public DateTime? AppliedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
public sealed class V180B3ChangeEvent
{
    public long ChangeRequestEventId { get; set; }
    public long ChangeRequestId { get; set; }
    public string EventType { get; set; } = "";
    public int? ActorUserId { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? DecisionKey { get; set; }
    public string? DetailsJson { get; set; }
}
public sealed record V180B3LocationFields(string LocationName,string? City,
    string? District,string? Address,string? PlusCode,string? TaxId,string? MasterNote);
public sealed record V180B3SubmitLocation(int LocationId,string ExpectedRowVersion,
    string Reason,V180B3LocationFields Proposed);
public sealed record V180B3Review(string RequestRowVersion,Guid DecisionKey,string Reason);
public sealed record V180B3RequestView(Guid RequestPublicId,string EntityId,int? TeamId,
    string RiskCode,string Status,int RequestedByUserId,DateTime SubmittedAt,
    string? BeforeJson,string ProposedJson,string RowVersion);

/// <summary>Deliberately nondeployable-by-default B3 candidate.
/// No permission for Leader is inferred from membership or seeded grants.
/// Approval/execution is fail-closed until independently reviewed.
/// </summary>
public sealed class V180B3ChangeRequestService(
    AppDbContext db,ICurrentUserService current,IConfiguration config)
{
    public bool Enabled=>config.GetValue<bool>("PackageB:B3:Enabled");

    private async Task ReadyAsync(CancellationToken ct)
    {
        if(!Enabled) throw new InvalidOperationException("B3_DISABLED");
        // Read-only catalog proof first; a recorded version alone is not
        // enough to prove unique Pending and DecisionKey indexes exist.
        // Do not select SchemaVersions until its table is confirmed present.
        try
        {
            var valid=await db.Database.SqlQueryRaw<int>(
                V180B3SqlSafetyRules.CatalogCheckSql).SingleAsync(ct);
            if(valid!=1)throw new InvalidOperationException("B3_SCHEMA_NOT_VERIFIED");
            var latest=await db.Database.SqlQueryRaw<string>(
                V180B3SqlSafetyRules.LatestSchemaVersionSql).FirstOrDefaultAsync(ct);
            V180B3SqlSafetyRules.RequireLatestSchemaVersion(latest);
        }
        catch(SqlException ex)
        {
            // Unverifiable schema (including insufficient catalog permission,
            // missing columns or broken SQL connection) must NEVER proceed.
            // No retries, schema auto-repair, or feature activation.
            throw new InvalidOperationException("B3_SCHEMA_NOT_VERIFIED",ex);
        }
    }
    private static byte[] Version(string input) =>
        V180B3RowVersionRules.Parse(input);
    private static V180B3RequestView ToView(V180B3ChangeRequest r)=>
        new(r.RequestPublicId,r.EntityId,r.TeamId,r.RiskCode,r.Status,
            r.RequestedByUserId,r.SubmittedAt,r.BeforeJson,r.ProposedJson,
            Convert.ToBase64String(r.RowVersion));

    private async Task<(CurrentUserDto User,bool Admin,bool Visitor)> LiveActorAsync(CancellationToken ct)
    {
        var user=current.GetRequired();
        await V180B3ActorEligibility.RequireAsync(db,user,ct);
        var today=BusinessTime.Today;
        var assigned=await (from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.RoleId
            where a.UserId==user.UserId&&r.IsActive&&a.EffectiveFrom<=today
                &&(!a.EffectiveTo.HasValue||a.EffectiveTo.Value>=today)
            select r.RoleCode).ToListAsync(ct);
        var projected=await (from a in db.UserRoles.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.RoleId
            where a.UserId==user.UserId&&r.IsActive
            select r.RoleCode).ToListAsync(ct);
        var roles=V180LocationLiveRoleRules.Evaluate(user.Roles,assigned,projected);
        return(user,roles.Admin,roles.Visitor);
    }
    private async Task<bool> VisitorTeamAsync(int userId,int organizationId,int teamId,CancellationToken ct)
    {
        var today=BusinessTime.Today;
        // Scope and membership alone do not prove that the TEAM remains usable.
        var team=await db.Teams.AsNoTracking().Where(x=>x.TeamId==teamId)
            .Select(x=>new{x.OrganizationId,x.IsActive,x.EffectiveFrom,x.EffectiveTo})
            .SingleOrDefaultAsync(ct);
        if(team is null || !V180B3LocationTeamRules.IsEffectiveForOrganization(
            organizationId,team.OrganizationId,team.IsActive,
            team.EffectiveFrom,team.EffectiveTo,today))return false;
        var employment=await db.UserIdentityProfiles.AsNoTracking()
            .Where(x=>x.UserId==userId&&x.UserType==UserTypes.Internal)
            .Select(x=>x.EmploymentId).FirstOrDefaultAsync(ct);
        if(!employment.HasValue)return false;
        return await db.UserTeamScopes.AsNoTracking().AnyAsync(x=>
               x.UserId==userId&&x.TeamId==teamId&&x.IsActive,ct)
            &&await db.UserTeamAssignments.AsNoTracking().AnyAsync(x=>
               x.UserId==userId&&x.TeamId==teamId&&x.EffectiveFrom<=today
               &&(!x.EffectiveTo.HasValue||x.EffectiveTo.Value>=today),ct)
            &&await db.TeamMemberships.AsNoTracking().AnyAsync(x=>
               x.EmploymentId==employment.Value&&x.TeamId==teamId
               &&x.EffectiveFrom<=today
               &&(!x.EffectiveTo.HasValue||x.EffectiveTo.Value>=today),ct);
    }
    public async Task<V180B3RequestView> SubmitAsync(
        V180B3SubmitLocation input,CancellationToken ct)
    {
        await ReadyAsync(ct);
        // Keep source version, live role/membership, pending uniqueness precheck
        // and request + Submitted audit event in a single serializable transaction.
        // DB filtered unique index remains mandatory for concurrent requests.
        await using var tx=await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,ct);
        var(user,_,visitor)=await LiveActorAsync(ct);
        var loc=await db.Locations.AsNoTracking().SingleOrDefaultAsync(x=>
            x.LocationId==input.LocationId,ct)
            ??throw new KeyNotFoundException("B3_LOCATION_NOT_FOUND");
        // B1C provenance blocked: Leader cannot submit on behalf of team,
        // but a dual-role Visitor may propose a change to their OWN record.
        if(!visitor||!user.OrganizationId.HasValue||!loc.OrganizationId.HasValue
           ||loc.OrganizationId!=user.OrganizationId||!loc.TeamId.HasValue
           ||loc.CreatedByUserId!=user.UserId||loc.LocationType!="Customer"
           ||loc.ApprovalStatus!="Approved"||!loc.IsActive
           ||!user.TeamIds.Contains(loc.TeamId.Value)
           ||!await VisitorTeamAsync(user.UserId,user.OrganizationId.Value,loc.TeamId.Value,ct))
            throw new UnauthorizedAccessException("B3_NO_VERIFIED_LOCATION_WRITE_SCOPE");
        if(!loc.RowVersion.SequenceEqual(Version(input.ExpectedRowVersion)))
            throw new InvalidOperationException("ROWVERSION_CONFLICT");
        var proposal=V180B3ProposalSafetyRules.Validate(
            input.Proposed,
            new V180B3LocationFields(loc.LocationName,loc.City,loc.District,
                loc.Address,loc.PlusCode,loc.TaxId,loc.MasterNote),input.Reason);
        // Precheck is informative; DB filtered unique index resolves submit races.
        if(await db.ChangeRequests.AsNoTracking().AnyAsync(x=>
            x.OrganizationId==loc.OrganizationId&&x.EntityKind=="Location"
            &&x.EntityId==loc.LocationId.ToString()&&x.Status=="Pending",ct))
            throw new InvalidOperationException("B3_PENDING_REQUEST_EXISTS");
        var row=new V180B3ChangeRequest{
            RequestPublicId=Guid.NewGuid(),OrganizationId=loc.OrganizationId.Value,
            TeamId=loc.TeamId,EntityKind="Location",EntityId=loc.LocationId.ToString(),
            OperationCode="UpdatePublishedLocation",RiskCode="High",
            ExpectedEntityRowVersion=loc.RowVersion.ToArray(),
            BeforeJson=JsonSerializer.Serialize(new{loc.LocationName,loc.City,
                loc.District,loc.Address,loc.PlusCode,loc.TaxId,loc.MasterNote}),
            ProposedJson=JsonSerializer.Serialize(proposal.Proposed),
            RequestedByUserId=user.UserId,SubmittedAt=DateTime.UtcNow,Status="Pending"};
        db.ChangeRequests.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch(DbUpdateException ex) { V180B3SqlSafetyRules.RethrowRecognizedUniqueConflict(ex); throw; }
        db.ChangeRequestEvents.Add(new V180B3ChangeEvent{
            ChangeRequestId=row.ChangeRequestId,EventType="Submitted",ActorUserId=user.UserId,
            OccurredAt=DateTime.UtcNow,CorrelationId=Guid.NewGuid(),
            DetailsJson=JsonSerializer.Serialize(new{Reason=proposal.Reason})});
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToView(row);
    }
    public async Task<IReadOnlyList<V180B3RequestView>> MineAsync(CancellationToken ct)
    {
        await ReadyAsync(ct);var(user,_,_)=await LiveActorAsync(ct);
        var rows=await V180B3QueueScopeRules.ForRequester(
                db.ChangeRequests.AsNoTracking(),user)
            .OrderByDescending(x=>x.SubmittedAt).Take(100).ToListAsync(ct);
        return rows.Select(ToView).ToList();
    }
    public async Task<IReadOnlyList<V180B3RequestView>> PendingAsync(CancellationToken ct)
    {
        await ReadyAsync(ct);var(user,admin,_)=await LiveActorAsync(ct);
        if(!admin||!user.OrganizationId.HasValue)
            throw new UnauthorizedAccessException("B3_ADMIN_REQUIRED");
        var rows=await V180B3QueueScopeRules.ForAdminPending(
                db.ChangeRequests.AsNoTracking(),user)
            .OrderBy(x=>x.SubmittedAt).Take(100).ToListAsync(ct);
        return rows.Select(ToView).ToList();
    }
    public async Task<V180B3RequestView> RejectAsync(
        Guid id,V180B3Review input,CancellationToken ct)
    {
        await ReadyAsync(ct);
        // Review authorization is deliberately re-evaluated INSIDE the
        // serializable decision transaction, not on a stale pre-transaction
        // token/role snapshot. The physical indexes remain mandatory.
        await using var tx=await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,ct);
        var(user,admin,_)=await LiveActorAsync(ct);
        if(!admin||!user.OrganizationId.HasValue)
            throw new UnauthorizedAccessException("B3_ADMIN_REQUIRED");
        var row=await db.ChangeRequests.SingleOrDefaultAsync(x=>
            x.RequestPublicId==id&&x.OrganizationId==user.OrganizationId,ct)
            ??throw new KeyNotFoundException("B3_REQUEST_NOT_FOUND");
        V180B3ReviewTargetRules.RequireSupportedTarget(row,user.OrganizationId.Value,id);
        var reviewReason=V180B3ProposalSafetyRules.RequireIndependentReview(
            row.RequestedByUserId,user.UserId,row.Status,row.RowVersion,
            input.RequestRowVersion,input.DecisionKey,input.Reason);
        // Friendly replay rejection; the separate database unique index
        // remains the authoritative concurrent replay barrier.
        if(await db.ChangeRequestEvents.AsNoTracking().AnyAsync(x=>
            x.DecisionKey==input.DecisionKey,ct))
            throw new InvalidOperationException("B3_DECISION_KEY_REPLAY");
        row.Status="Rejected";row.ReviewedByUserId=user.UserId;
        row.ReviewedAt=DateTime.UtcNow;row.ReviewReason=reviewReason;
        db.ChangeRequestEvents.Add(new V180B3ChangeEvent{
            ChangeRequestId=row.ChangeRequestId,EventType="Rejected",
            ActorUserId=user.UserId,OccurredAt=DateTime.UtcNow,
            CorrelationId=Guid.NewGuid(),DecisionKey=input.DecisionKey,
            DetailsJson=JsonSerializer.Serialize(new{Reason=reviewReason})});
        try { await db.SaveChangesAsync(ct); }
        catch(DbUpdateException ex) { V180B3SqlSafetyRules.RethrowRecognizedUniqueConflict(ex); throw; }
        await tx.CommitAsync(ct);
        return ToView(row);
    }
    public async Task<V180B3RequestView> ApproveAsync(
        Guid id,V180B3Review input,CancellationToken ct)
    {
        await ReadyAsync(ct);var(_,admin,_)=await LiveActorAsync(ct);
        if(!admin)throw new UnauthorizedAccessException("B3_ADMIN_REQUIRED");
        // Explicitly no write or Approved event while manager provenance,
        // address/geocoding, duplicate, history invariants remain unverified.
        throw new InvalidOperationException("B3_APPROVAL_EXECUTOR_NOT_AUTHORIZED");
    }
}
