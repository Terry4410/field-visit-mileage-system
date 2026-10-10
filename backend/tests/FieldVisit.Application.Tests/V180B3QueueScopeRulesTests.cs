using FieldVisit.Application;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// B4 query isolation using InMemory only; never accesses 011 DB schema.
/// LiveActorAsync is independently enforced by the service.
/// </summary>
public sealed class V180B3QueueScopeRulesTests
{
    private static CurrentUserDto Actor(int user=101,int? org=1) =>
        new(user,"tester","Tester",null,org,7,"Team 7",
            new[]{"admin"},new[]{new TeamScopeDto(7,"Team 7",true)});

    private static V180B3ChangeRequest Request(long id,int user,int org,string status,
        string kind="Location",string operation="UpdatePublishedLocation",
        string risk="High",int? team=7)=>
        new(){ChangeRequestId=id,RequestPublicId=Guid.NewGuid(),
            RequestedByUserId=user,OrganizationId=org,Status=status,
            EntityKind=kind,EntityId="10",OperationCode=operation,
            RiskCode=risk,TeamId=team,SubmittedAt=DateTime.UtcNow};

    private static async Task<AppDbContext> DbAsync()
    {
        var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"b4-b3-queue-{Guid.NewGuid()}").Options);
        db.ChangeRequests.AddRange(
            Request(1,101,1,"Pending"),
            Request(2,101,1,"Rejected"),
            Request(3,102,1,"Pending"),
            Request(4,101,2,"Pending"),
            Request(5,101,1,"Pending",kind:"User"),
            Request(6,101,1,"Pending",operation:"UpdateRole"),
            Request(7,101,1,"Pending",risk:"Low"),
            Request(8,101,1,"Pending",team:null),
            Request(9,101,1,"Applied"),
            Request(10,101,1,"Unknown"),
            Request(11,102,2,"Pending"));
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        return db;
    }

    [Fact]
    public async Task Mine_contains_only_own_current_org_supported_b3_requests()
    {
        await using var db=await DbAsync();
        var ids=await V180B3QueueScopeRules.ForRequester(
            db.ChangeRequests.AsNoTracking(),Actor())
            .Select(x=>x.ChangeRequestId).OrderBy(x=>x).ToArrayAsync();
        Assert.Equal(new long[]{1,2},ids);
    }

    [Fact]
    public async Task Pending_admin_can_see_own_org_pending_but_never_foreign_or_unknown_ops()
    {
        await using var db=await DbAsync();
        var ids=await V180B3QueueScopeRules.ForAdminPending(
            db.ChangeRequests.AsNoTracking(),Actor())
            .Select(x=>x.ChangeRequestId).OrderBy(x=>x).ToArrayAsync();
        Assert.Equal(new long[]{1,3},ids);
        var otherOrg=await V180B3QueueScopeRules.ForAdminPending(
            db.ChangeRequests.AsNoTracking(),Actor(org:2))
            .Select(x=>x.ChangeRequestId).OrderBy(x=>x).ToArrayAsync();
        Assert.Equal(new long[]{4,11},otherOrg);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Missing_or_invalid_org_never_reads_any_B3_payload(int? org)
    {
        await using var db=await DbAsync();
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180B3QueueScopeRules.ForRequester(
                db.ChangeRequests.AsNoTracking(),Actor(org:org)));
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180B3QueueScopeRules.ForAdminPending(
                db.ChangeRequests.AsNoTracking(),Actor(org:org)));
    }

    [Fact]
    public async Task Invalid_requester_id_cannot_read_audit_payload()
    {
        await using var db=await DbAsync();
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180B3QueueScopeRules.ForRequester(
                db.ChangeRequests.AsNoTracking(),Actor(user:0)));
    }
}
