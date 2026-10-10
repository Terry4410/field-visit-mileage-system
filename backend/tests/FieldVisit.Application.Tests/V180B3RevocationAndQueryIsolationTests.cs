using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// In-memory B3 live actor and queue regression only; no formal 011 schema,
/// deployment, feature activation or live HR token/session environment.
/// </summary>
public sealed class V180B3RevocationAndQueryIsolationTests
{
    private const int UserId=871;
    private static CurrentUserDto Actor(int id=UserId,int? org=1,string[]? roles=null) =>
        new(id,"B3TEST","Visitor",null,org,7,"Team 7",
            roles??new[]{"visitor"},new[]{new TeamScopeDto(7,"Team 7",true)});

    private static async Task<AppDbContext> ActiveUserAsync()
    {
        var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"b3-revocation-{Guid.NewGuid()}").Options);
        db.Users.Add(new User{
            UserId=UserId,OrganizationId=1,IsActive=true,DisplayName="Fixture",
            CreatedAt=DateTime.UtcNow
        });
        db.UserEmploymentPeriods.Add(new UserEmploymentPeriod{
            UserEmploymentPeriodId=1001,UserId=UserId,
            EmploymentStatus=EmploymentStatuses.Active,
            EffectiveFrom=BusinessTime.Today.AddDays(-45)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    [Theory]
    [InlineData(EmploymentStatuses.Leave)]
    [InlineData(EmploymentStatuses.Terminated)]
    [InlineData(EmploymentStatuses.PreHire)]
    public async Task Live_HR_revocation_denies_existing_actor_without_new_token(string revokedStatus)
    {
        await using var db=await ActiveUserAsync();
        await V180B3ActorEligibility.RequireAsync(db,Actor(),CancellationToken.None);
        db.UserEmploymentPeriods.Add(new UserEmploymentPeriod{
            UserEmploymentPeriodId=1002,UserId=UserId,
            EmploymentStatus=revokedStatus,EffectiveFrom=BusinessTime.Today
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            V180B3ActorEligibility.RequireAsync(db,Actor(),CancellationToken.None));
    }

    [Fact]
    public async Task Live_account_disable_immediately_denies_same_actor_identity()
    {
        await using var db=await ActiveUserAsync();
        await V180B3ActorEligibility.RequireAsync(db,Actor(),CancellationToken.None);
        var account=await db.Users.SingleAsync(x=>x.UserId==UserId);
        account.IsActive=false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            V180B3ActorEligibility.RequireAsync(db,Actor(),CancellationToken.None));
    }

    [Fact]
    public async Task Expired_HR_period_does_not_resurrect_old_active_status()
    {
        await using var db=await ActiveUserAsync();
        var active=await db.UserEmploymentPeriods.SingleAsync(x=>x.UserId==UserId);
        active.EffectiveTo=BusinessTime.Today.AddDays(-1);
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            V180B3ActorEligibility.RequireAsync(db,Actor(),CancellationToken.None));
    }

    [Fact]
    public async Task Same_user_token_replayed_against_different_organization_is_denied()
    {
        await using var db=await ActiveUserAsync();
        await V180B3ActorEligibility.RequireAsync(db,Actor(),CancellationToken.None);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            V180B3ActorEligibility.RequireAsync(db,Actor(org:2),CancellationToken.None));
    }

    [Theory]
    [InlineData("assignment-revoked")]
    [InlineData("projection-revoked")]
    [InlineData("token-forged")]
    [InlineData("all-revoked")]
    public void Stale_admin_claim_never_overrides_live_role_intersection(string scenario)
    {
        var token=scenario=="token-forged"?new[]{"admin"}:new[]{"admin","visitor"};
        var assigned=scenario is "assignment-revoked" or "all-revoked"
            ?new[]{"visitor"}:new[]{"admin","visitor"};
        var projected=scenario is "projection-revoked" or "all-revoked"
            ?new[]{"visitor"}:new[]{"admin","visitor"};
        if(scenario=="token-forged")
        {
            assigned=new[]{"visitor"};
            projected=new[]{"visitor"};
        }
        var result=V180LocationLiveRoleRules.Evaluate(token,assigned,projected);
        Assert.False(result.Admin);
        Assert.False(result.Leader);
    }

    [Fact]
    public void Leader_token_without_independently_valid_grants_never_creates_leader_permission()
    {
        var result=V180LocationLiveRoleRules.Evaluate(
            new[]{"leader","visitor"},new[]{"visitor"},new[]{"leader","visitor"});
        Assert.False(result.Leader);
        Assert.True(result.Visitor);
        Assert.False(result.Admin);
    }

    private static V180B3ChangeRequest Request(long id,int requester,int org,int? team=7,
        string kind="Location",string op="UpdatePublishedLocation") =>
        new(){ChangeRequestId=id,RequestPublicId=Guid.NewGuid(),OrganizationId=org,
            RequestedByUserId=requester,Status="Pending",TeamId=team,
            EntityKind=kind,OperationCode=op,RiskCode="High",EntityId="20",
            ProposedJson="{\"Name\":\"SENSITIVE_MARKER_"+id+"\"}"};

    [Fact]
    public async Task Same_team_peer_jwt_does_not_disclose_requester_proposals()
    {
        await using var db=await ActiveUserAsync();
        db.ChangeRequests.AddRange(Request(100,UserId,1),Request(101,872,1));
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var payloads=await V180B3QueueScopeRules.ForRequester(
            db.ChangeRequests.AsNoTracking(),Actor()).Select(x=>x.ProposedJson)
            .ToArrayAsync();
        Assert.Single(payloads);
        Assert.Contains("SENSITIVE_MARKER_100",payloads[0]);
        Assert.DoesNotContain("SENSITIVE_MARKER_101",string.Join("|",payloads));
    }

    [Fact]
    public async Task Admin_pending_queue_never_exposes_other_organization_proposals()
    {
        await using var db=await ActiveUserAsync();
        db.ChangeRequests.AddRange(Request(201,UserId,1),Request(202,UserId,2));
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var payloads=await V180B3QueueScopeRules.ForAdminPending(
            db.ChangeRequests.AsNoTracking(),Actor(roles:new[]{"admin"}))
            .Select(x=>x.ProposedJson).ToArrayAsync();
        Assert.Single(payloads);
        Assert.Contains("SENSITIVE_MARKER_201",payloads[0]);
        Assert.DoesNotContain("SENSITIVE_MARKER_202",string.Join("|",payloads));
    }

    [Fact]
    public async Task Forged_operation_and_missing_team_never_appear_in_queue()
    {
        await using var db=await ActiveUserAsync();
        db.ChangeRequests.AddRange(Request(301,UserId,1),
            Request(302,UserId,1,team:null),
            Request(303,UserId,1,op:"UpdateRole"),
            Request(304,UserId,1,kind:"User"));
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var ids=await V180B3QueueScopeRules.ForRequester(
            db.ChangeRequests.AsNoTracking(),Actor())
            .Select(x=>x.ChangeRequestId).ToArrayAsync();
        Assert.Equal(new long[]{301},ids);
    }
}
