using System.Text.Json;
using FieldVisit.Application;
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
        // Runtime schema check: appsettings DbSchemaVersion is not proof.
        var valid=await db.Database.SqlQueryRaw<int>(
            @"SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.ChangeRequests',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.ChangeRequestEvents',N'U') IS NOT NULL
                AND EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE VersionNumber=N'1.8.0-011')
                THEN 1 ELSE 0 END AS int) AS Value").SingleAsync(ct);
        if(valid!=1)throw new InvalidOperationException("B3_SCHEMA_NOT_VERIFIED");
    }
    private static byte[] Version(string input)
    {
        if(string.IsNullOrWhiteSpace(input))
            throw new InvalidOperationException("ROWVERSION_CONFLICT");
        try{var b=Convert.FromBase64String(input);
            if(b.Length==8)return b;}
        catch(FormatException){}
        throw new InvalidOperationException("ROWVERSION_CONFLICT");
    }
    private static V180B3RequestView ToView(V180B3ChangeRequest r)=>
        new(r.RequestPublicId,r.EntityId,r.TeamId,r.RiskCode,r.Status,
            r.RequestedByUserId,r.SubmittedAt,r.BeforeJson,r.ProposedJson,
            Convert.ToBase64String(r.RowVersion));

    private async Task<(CurrentUserDto User,bool Admin,bool Visitor)> LiveActorAsync(CancellationToken ct)
    {
        var user=current.GetRequired();
        var account=await db.Users.AsNoTracking().FirstOrDefaultAsync(x=>
            x.UserId==user.UserId&&x.OrganizationId==user.OrganizationId,ct)
            ??throw new UnauthorizedAccessException("B3_ACCOUNT_INVALID");
        if(!(await new V170AccessControl(db)
            .EvaluateLoginAsync(user.UserId,account.IsActive,ct)).IsAllowed)
            throw new UnauthorizedAccessException("B3_HR_STATUS_DENIED");
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
    private async Task<bool> VisitorTeamAsync(int userId,int teamId,CancellationToken ct)
    {
        var today=BusinessTime.Today;
        var employment=await db.UserIdentityProfiles.AsNoTracking()
            .Where(x=>x.UserId==userId).Select(x=>x.EmploymentId)
            .FirstOrDefaultAsync(ct);
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
           ||!await VisitorTeamAsync(user.UserId,loc.TeamId.Value,ct))
            throw new UnauthorizedAccessException("B3_NO_VERIFIED_LOCATION_WRITE_SCOPE");
        if(!loc.RowVersion.SequenceEqual(Version(input.ExpectedRowVersion)))
            throw new InvalidOperationException("ROWVERSION_CONFLICT");
        if(input.Proposed is null||string.IsNullOrWhiteSpace(input.Proposed.LocationName)
           ||string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Length>1000
           ||input.Proposed.LocationName.Length>200
           ||input.Proposed.TaxId?.Length>20||input.Proposed.MasterNote?.Length>1000)
            throw new InvalidOperationException("B3_PROPOSAL_INVALID");
        var row=new V180B3ChangeRequest{
            RequestPublicId=Guid.NewGuid(),OrganizationId=loc.OrganizationId.Value,
            TeamId=loc.TeamId,EntityKind="Location",EntityId=loc.LocationId.ToString(),
            OperationCode="UpdatePublishedLocation",RiskCode="High",
            ExpectedEntityRowVersion=loc.RowVersion.ToArray(),
            BeforeJson=JsonSerializer.Serialize(new{loc.LocationName,loc.City,
                loc.District,loc.Address,loc.PlusCode,loc.TaxId,loc.MasterNote}),
            ProposedJson=JsonSerializer.Serialize(input.Proposed),
            RequestedByUserId=user.UserId,SubmittedAt=DateTime.UtcNow,Status="Pending"};
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        db.ChangeRequests.Add(row);
        await db.SaveChangesAsync(ct);
        db.ChangeRequestEvents.Add(new V180B3ChangeEvent{
            ChangeRequestId=row.ChangeRequestId,EventType="Submitted",ActorUserId=user.UserId,
            OccurredAt=DateTime.UtcNow,CorrelationId=Guid.NewGuid(),
            DetailsJson=JsonSerializer.Serialize(new{Reason=input.Reason.Trim()})});
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToView(row);
    }
    public async Task<IReadOnlyList<V180B3RequestView>> MineAsync(CancellationToken ct)
    {
        await ReadyAsync(ct);var(user,_,_)=await LiveActorAsync(ct);
        var rows=await db.ChangeRequests.AsNoTracking()
            .Where(x=>x.RequestedByUserId==user.UserId&&x.OrganizationId==user.OrganizationId)
            .OrderByDescending(x=>x.SubmittedAt).Take(100).ToListAsync(ct);
        return rows.Select(ToView).ToList();
    }
    public async Task<IReadOnlyList<V180B3RequestView>> PendingAsync(CancellationToken ct)
    {
        await ReadyAsync(ct);var(user,admin,_)=await LiveActorAsync(ct);
        if(!admin||!user.OrganizationId.HasValue)
            throw new UnauthorizedAccessException("B3_ADMIN_REQUIRED");
        var rows=await db.ChangeRequests.AsNoTracking()
            .Where(x=>x.OrganizationId==user.OrganizationId&&x.Status=="Pending")
            .OrderBy(x=>x.SubmittedAt).Take(100).ToListAsync(ct);
        return rows.Select(ToView).ToList();
    }
    public async Task<V180B3RequestView> RejectAsync(
        Guid id,V180B3Review input,CancellationToken ct)
    {
        await ReadyAsync(ct);var(user,admin,_)=await LiveActorAsync(ct);
        if(!admin||!user.OrganizationId.HasValue)
            throw new UnauthorizedAccessException("B3_ADMIN_REQUIRED");
        if(input.DecisionKey==Guid.Empty||string.IsNullOrWhiteSpace(input.Reason)
           ||input.Reason.Length>1000)throw new InvalidOperationException("B3_REASON_REQUIRED");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var row=await db.ChangeRequests.SingleOrDefaultAsync(x=>
            x.RequestPublicId==id&&x.OrganizationId==user.OrganizationId,ct)
            ??throw new KeyNotFoundException("B3_REQUEST_NOT_FOUND");
        if(row.RequestedByUserId==user.UserId)
            throw new UnauthorizedAccessException("B3_SELF_REVIEW_DENIED");
        if(row.Status!="Pending"||!row.RowVersion.SequenceEqual(Version(input.RequestRowVersion)))
            throw new InvalidOperationException("ROWVERSION_CONFLICT");
        row.Status="Rejected";row.ReviewedByUserId=user.UserId;
        row.ReviewedAt=DateTime.UtcNow;row.ReviewReason=input.Reason.Trim();
        db.ChangeRequestEvents.Add(new V180B3ChangeEvent{
            ChangeRequestId=row.ChangeRequestId,EventType="Rejected",
            ActorUserId=user.UserId,OccurredAt=DateTime.UtcNow,
            CorrelationId=Guid.NewGuid(),DecisionKey=input.DecisionKey,
            DetailsJson=JsonSerializer.Serialize(new{Reason=input.Reason.Trim()})});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
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
