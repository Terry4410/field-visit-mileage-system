using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>Security-changing role and membership commands fail before writing
/// when any live Admin gate is revoked. No SQL Server or UAT DB is accessed.
/// </summary>
public sealed class V180SecurityGrantWriteGatesTests
{
    private sealed class Current(CurrentUserDto user):ICurrentUserService
    {
        public CurrentUserDto GetRequired()=>user;
    }

    private static CurrentUserDto Actor(string[]? tokenRoles=null,int? orgId=1)=>
        new(701,"A701","Admin",null,orgId,7,"Team",
            tokenRoles??["admin"],[new TeamScopeDto(7,"Team",true)]);

    private static async Task<AppDbContext> SeedAsync(
        bool active=true,string hr=EmploymentStatuses.Active,
        bool dated=true,bool projected=true,bool activeRole=true)
    {
        var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"security-grant-{Guid.NewGuid()}").Options);
        var today=DateOnly.FromDateTime(DateTime.UtcNow);
        db.Users.Add(new User{
            UserId=701,OrganizationId=1,DisplayName="Admin",
            IsActive=active,CreatedAt=DateTime.UtcNow});
        db.UserEmploymentPeriods.Add(new UserEmploymentPeriod{
            UserEmploymentPeriodId=701,UserId=701,EmploymentStatus=hr,
            EffectiveFrom=today.AddDays(-30)});
        db.Roles.Add(new Role{RoleId=1,RoleCode="admin",
            RoleName="Admin",IsActive=activeRole,CreatedAt=DateTime.UtcNow});
        db.UserRoleAssignments.Add(new UserRoleAssignment{
            UserRoleAssignmentId=701,UserId=701,RoleId=1,
            EffectiveFrom=today.AddDays(-30),
            EffectiveTo=dated?null:today.AddDays(-1),
            CreatedAt=DateTime.UtcNow});
        if(projected)db.UserRoles.Add(new UserRole{
            UserRoleId=701,UserId=701,RoleId=1,AssignedAt=DateTime.UtcNow});
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static async Task AssertSecurityWritesDeniedAsync(
        AppDbContext db,CurrentUserDto user)
    {
        var actor=new Current(user);
        var ct=CancellationToken.None;
        var today=DateOnly.FromDateTime(DateTime.UtcNow);
        var roles=new V180InternalRoleCommandService(db,actor);
        var teams=new V180TeamMembershipCommandService(db,actor);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            roles.UpdateAsync(999,new V180InternalRoleAccessRequest(
                ["visitor"],today),ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            teams.UpdateAsync(999,new V180ReplaceTeamMembershipsRequest(
                [],today),ct));
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.Equal(1,await db.UserRoleAssignments.CountAsync());
        Assert.Equal(0,await db.TeamMemberships.CountAsync());
        Assert.Equal(0,await db.UserTeamAssignments.CountAsync());
    }

    [Fact]
    public async Task Revoked_effective_admin_grant_denies_role_and_team_writes()
    {
        await using var db=await SeedAsync(dated:false);
        await AssertSecurityWritesDeniedAsync(db,Actor());
    }

    [Theory]
    [InlineData(EmploymentStatuses.Leave)]
    [InlineData(EmploymentStatuses.Terminated)]
    [InlineData(EmploymentStatuses.PreHire)]
    public async Task Ineligible_HR_denies_role_and_team_writes(string status)
    {
        await using var db=await SeedAsync(hr:status);
        await AssertSecurityWritesDeniedAsync(db,Actor());
    }

    [Fact]
    public async Task Disabled_account_denies_role_and_team_writes()
    {
        await using var db=await SeedAsync(active:false);
        await AssertSecurityWritesDeniedAsync(db,Actor());
    }

    [Fact]
    public async Task Missing_role_projection_denies_role_and_team_writes()
    {
        await using var db=await SeedAsync(projected:false);
        await AssertSecurityWritesDeniedAsync(db,Actor());
    }

    [Fact]
    public async Task Inactive_role_denies_role_and_team_writes()
    {
        await using var db=await SeedAsync(activeRole:false);
        await AssertSecurityWritesDeniedAsync(db,Actor());
    }

    [Fact]
    public async Task Leader_jwt_cannot_stand_in_for_admin()
    {
        await using var db=await SeedAsync();
        await AssertSecurityWritesDeniedAsync(db,Actor(["leader"]));
    }

    [Fact]
    public async Task Missing_or_cross_org_cannot_change_security_grants()
    {
        await using var db=await SeedAsync();
        await AssertSecurityWritesDeniedAsync(db,Actor(orgId:null));
        await AssertSecurityWritesDeniedAsync(db,Actor(orgId:2));
    }
}
