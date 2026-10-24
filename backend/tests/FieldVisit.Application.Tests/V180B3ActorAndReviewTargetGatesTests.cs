using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// DB-free B4 regression: B3 identities and rejection target invariants.
/// B3 SQL schema has NOT been created and feature flags remain OFF.
/// </summary>
public sealed class V180B3ActorAndReviewTargetGatesTests
{
    private static CurrentUserDto Actor(int? org=1)=>new(
        910,"A910","Admin",null,org,7,"Team",
        new[]{"admin"},new[]{new TeamScopeDto(7,"Team",true)});

    private static async Task<AppDbContext> SeedAsync(
        bool active=true,string hr=EmploymentStatuses.Active,
        string? userType=null,int org=1)
    {
        var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"b3-actor-gates-{Guid.NewGuid()}").Options);
        var today=DateOnly.FromDateTime(DateTime.UtcNow);
        db.Users.Add(new User{UserId=910,OrganizationId=org,
            DisplayName="Admin",IsActive=active,CreatedAt=DateTime.UtcNow});
        db.UserEmploymentPeriods.Add(new UserEmploymentPeriod{
            UserEmploymentPeriodId=910,UserId=910,EmploymentStatus=hr,
            EffectiveFrom=today.AddDays(-30)});
        if(userType is not null)db.UserIdentityProfiles.Add(
            new UserIdentityProfile{UserId=910,UserType=userType,UserCode="A910",
                CreatedAt=DateTime.UtcNow});
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    [Fact]
    public async Task Disabled_B3_actor_is_denied_even_when_HR_active()
    {
        await using var db=await SeedAsync(active:false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            V180B3ActorEligibility.RequireAsync(db,Actor(),CancellationToken.None));
    }

    [Theory]
    [InlineData(EmploymentStatuses.Leave)]
    [InlineData(EmploymentStatuses.Terminated)]
    [InlineData(EmploymentStatuses.PreHire)]
    public async Task HR_ineligible_B3_actors_are_denied(string status)
    {
        await using var db=await SeedAsync(hr:status);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            V180B3ActorEligibility.RequireAsync(db,Actor(),CancellationToken.None));
    }

    [Theory]
    [InlineData(UserTypes.External)]
    [InlineData("Unknown")]
    [InlineData("")]
    public async Task External_and_unknown_profile_types_cannot_review_requests(string type)
    {
        await using var db=await SeedAsync(userType:type);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            V180B3ActorEligibility.RequireAsync(db,Actor(),CancellationToken.None));
    }

    [Fact]
    public async Task Mismatched_organization_and_missing_organization_are_denied()
    {
        await using var db=await SeedAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            V180B3ActorEligibility.RequireAsync(db,Actor(2),CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            V180B3ActorEligibility.RequireAsync(db,Actor(null),CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(UserTypes.Internal)]
    public async Task Valid_internal_or_legacy_internal_account_retains_access(string? type)
    {
        await using var db=await SeedAsync(userType:type);
        await V180B3ActorEligibility.RequireAsync(db,Actor(),CancellationToken.None);
    }

    private static V180B3ChangeRequest Target()=>new(){
        RequestPublicId=Guid.NewGuid(),OrganizationId=1,TeamId=7,
        EntityKind="Location",EntityId="9",
        OperationCode="UpdatePublishedLocation",RiskCode="High",
        RequestedByUserId=910,Status="Pending",
        ExpectedEntityRowVersion=new byte[8]
    };

    [Fact]
    public void Only_expected_scoped_B3_location_change_can_be_rejected()
    {
        var row=Target();
        V180B3ReviewTargetRules.RequireSupportedTarget(row,1,row.RequestPublicId);
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180B3ReviewTargetRules.RequireSupportedTarget(row,2,row.RequestPublicId));
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180B3ReviewTargetRules.RequireSupportedTarget(row,1,Guid.NewGuid()));
    }

    [Fact]
    public void Forged_and_unapproved_operation_kinds_fail_closed()
    {
        var row=Target();
        var oldKind=row.EntityKind;
        row.EntityKind="Employment";
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180B3ReviewTargetRules.RequireSupportedTarget(row,1,row.RequestPublicId));
        row.EntityKind=oldKind;
        row.OperationCode="UpdateRole";
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180B3ReviewTargetRules.RequireSupportedTarget(row,1,row.RequestPublicId));
        row.OperationCode="UpdatePublishedLocation";
        row.RiskCode="Low";
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180B3ReviewTargetRules.RequireSupportedTarget(row,1,row.RequestPublicId));
        row.RiskCode="High";
        row.ExpectedEntityRowVersion=new byte[7];
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180B3ReviewTargetRules.RequireSupportedTarget(row,1,row.RequestPublicId));
        row.ExpectedEntityRowVersion=new byte[8];
        row.TeamId=null;
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180B3ReviewTargetRules.RequireSupportedTarget(row,1,row.RequestPublicId));
        row.TeamId=7;
        row.EntityId="not-a-number";
        Assert.Throws<UnauthorizedAccessException>(()=>
            V180B3ReviewTargetRules.RequireSupportedTarget(row,1,row.RequestPublicId));
    }
}
