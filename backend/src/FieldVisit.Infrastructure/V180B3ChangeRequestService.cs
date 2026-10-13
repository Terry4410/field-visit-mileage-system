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
    private static V180B3RequestView ToView(V180B3ChangeRequest r)=>
        new(r.RequestPublicId,r.EntityId,r.TeamId,r.RiskCode,r.Status,
            r.RequestedByUserId,r.SubmittedAt,r.BeforeJson,r.ProposedJson,
            Convert.ToBase64String(r.RowVersion));

    // B3 live read-only policy is shared with the isolated SQL tests.
    // This preserves the existing actor and team decisions exactly.
    private Task<(CurrentUserDto User,bool Admin,bool Visitor)> LiveActorAsync(
        CancellationToken ct)=>
        V180B3LiveAuthorizationRead.EvaluateActorAsync(
            db,current.GetRequired(),BusinessTime.Today,ct);

    private Task<bool> VisitorTeamAsync(
        int userId,int organizationId,int teamId,CancellationToken ct)=>
        V180B3LiveAuthorizationRead.IsVisitorTeamValidAsync(
            db,userId,organizationId,teamId,BusinessTime.Today,ct);

    public async Task<V180B3RequestView> SubmitAsync(
        V180B3SubmitLocation input,CancellationToken ct)
    {
        await ReadyAsync(ct);
        // Fail fast on malformed client payloads before acquiring a DB transaction
        // or loading HR/team/location records. B3_DISABLED still takes precedence.
        var expectedVersion=V180B3RequestInputRules.RequireSubmission(input);
        // Keep source version, live role/membership, pending uniqueness precheck
        // and request + Submitted audit event in a single serializable transaction.
        // DB filtered unique index remains mandatory for concurrent requests.
        await using var tx=await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,ct);
        var(user,admin,visitor)=await LiveActorAsync(ct);
        var loc=await db.Locations.AsNoTracking().SingleOrDefaultAsync(x=>
            x.LocationId==input.LocationId,ct)
            ??throw new KeyNotFoundException("B3_LOCATION_NOT_FOUND");
        // Option C: Visitor and Leader submit into ONE canonical store.
        // Token claims are never sufficient: live Visitor membership or
        // live Leader role + dated team/appointment are required.
        var visitorTeamValid = visitor && loc.TeamId.HasValue
            && user.OrganizationId.HasValue
            && await VisitorTeamAsync(user.UserId,
                user.OrganizationId.Value, loc.TeamId.Value, ct);
        var leaderCandidate = user.Roles.Any(r =>
            r.Equals("leader", StringComparison.OrdinalIgnoreCase))
            && !admin && !user.Roles.Any(r =>
                r.Equals("admin", StringComparison.OrdinalIgnoreCase));
        var liveLeader = false;
        if (!visitorTeamValid && leaderCandidate)
        {
            await V180OptionCLiveLeaderScope.RequireAsync(db, user, loc.TeamId, ct);
            liveLeader = true;
        }
        V180OptionCSubmissionScope.RequireOwnPublishedCustomer(
            user, loc, visitor, visitorTeamValid, liveLeader);
        if(loc.RowVersion is null || loc.RowVersion.Length!=8
            ||!loc.RowVersion.SequenceEqual(expectedVersion))
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
        V180B3RequestInputRules.RequireReviewTarget(id,input);
        // Review authorization is deliberately re-evaluated INSIDE the
        // serializable decision transaction, not on a stale pre-transaction
        // token/role snapshot. The physical indexes remain mandatory.
        await using var tx=await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,ct);
        var(user,admin,_)=await LiveActorAsync(ct);
        if(!admin||!user.OrganizationId.HasValue)
            throw new UnauthorizedAccessException("B3_ADMIN_REQUIRED");
        // Match the approval preview's request-first lock order, so two
        // reviewers cannot both decide from an unlocked Pending snapshot.
        var row=await db.ChangeRequests.FromSqlInterpolated(
            V180B3ApprovalLockSql.RequestByPublicId(id,user.OrganizationId.Value))
            .SingleOrDefaultAsync(ct)
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
    // WORK-only read-only integration candidate. NOT exposed as an endpoint
    // and deliberately NOT called by ApproveAsync (which remains DENY ALL).
    // Serializable EF reads do not replace a reviewed UPDLOCK lock contract
    // for the future write executor. No event/status/location is changed.
    internal async Task<V180B3ApprovalApplyPlan> PreviewApprovalFromLiveDataAsync(
        Guid id, V180B3Review input, CancellationToken ct)
    {
        await ReadyAsync(ct);
        V180B3RequestInputRules.RequireReviewTarget(id, input);
        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);
        var (reviewer, admin, _) = await LiveActorAsync(ct);
        if (!admin || !reviewer.OrganizationId.HasValue)
            throw new UnauthorizedAccessException("B3_ADMIN_REQUIRED");
        var request = await db.ChangeRequests.FromSqlInterpolated(
            V180B3ApprovalLockSql.RequestByPublicId(id, reviewer.OrganizationId.Value))
            .AsNoTracking().SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("B3_REQUEST_NOT_FOUND");
        V180B3ReviewTargetRules.RequireSupportedTarget(
            request, reviewer.OrganizationId.Value, id);
        if (!int.TryParse(request.EntityId, out var locationId))
            throw new UnauthorizedAccessException("B3_REVIEW_TARGET_UNSUPPORTED");
        var location = await db.Locations.FromSqlInterpolated(
            V180B3ApprovalLockSql.LocationById(locationId, reviewer.OrganizationId.Value))
            .AsNoTracking().SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("B3_LOCATION_NOT_FOUND");
        var member = request.TeamId.HasValue
            && await VisitorTeamAsync(request.RequestedByUserId,
                request.OrganizationId, request.TeamId.Value, ct);
        var replay = await db.ChangeRequestEvents.AsNoTracking().AnyAsync(
            x => x.DecisionKey == input.DecisionKey, ct);
        var current = new V180B3LocationFields(
            location.LocationName, location.City, location.District,
            location.Address, location.PlusCode, location.TaxId,
            location.MasterNote);
        var plan = V180B3ApprovalApplyPreflight.PrepareImmutablePlan(
            request, reviewer.OrganizationId.Value, id, reviewer.UserId,
            input, location.LocationId, location.OrganizationId,
            location.TeamId, location.CreatedByUserId, location.LocationType,
            location.ApprovalStatus, location.IsActive, location.RowVersion,
            current);
        V180B3ApprovalApplyPreflight.RequireLockedApprovalBoundary(
            plan, request, current, location.RowVersion,
            location.LocationId, location.OrganizationId, location.TeamId,
            location.CreatedByUserId, location.LocationType,
            location.ApprovalStatus, location.IsActive, reviewer.UserId,
            reviewer.OrganizationId, admin, member, input.DecisionKey,
            plan.ReviewReason, replay);
        await tx.RollbackAsync(ct);
        return plan;
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
