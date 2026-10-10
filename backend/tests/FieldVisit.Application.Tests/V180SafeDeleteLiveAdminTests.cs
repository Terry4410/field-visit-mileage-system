using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// B4 negative repository/service tests: no UAT SQL, no migrations and no
/// permanent deletion on test data. Denied calls must fail before any write.
/// </summary>
public sealed class V180SafeDeleteLiveAdminTests
{
    private sealed class StubCurrent(CurrentUserDto user):ICurrentUserService
    {
        public CurrentUserDto GetRequired()=>user;
    }

    private static CurrentUserDto Actor(int? organization=1,string role="admin")=>
        new(501,"admin501","Admin",null,organization,7,"Team",
            [role],[new TeamScopeDto(7,"Team",true)]);

    private static async Task<AppDbContext> SeedAsync(bool datedAdmin=true,
        bool projectedAdmin=true,string hrStatus=EmploymentStatuses.Active,
        bool activeAccount=true)
    {
        var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"v180-safe-delete-gates-{Guid.NewGuid()}").Options);
        var today=DateOnly.FromDateTime(DateTime.UtcNow);
        db.Users.Add(new User{
            UserId=501,OrganizationId=1,DisplayName="Admin",
            IsActive=activeAccount,CreatedAt=DateTime.UtcNow});
        db.UserEmploymentPeriods.Add(new UserEmploymentPeriod{
            UserEmploymentPeriodId=501,UserId=501,
            EmploymentStatus=hrStatus,EffectiveFrom=today.AddDays(-30)});
        db.Roles.Add(new Role{
            RoleId=1,RoleCode="admin",RoleName="Admin",
            IsActive=true,CreatedAt=DateTime.UtcNow});
        if(projectedAdmin)db.UserRoles.Add(new UserRole{
            UserRoleId=1,UserId=501,RoleId=1,AssignedAt=DateTime.UtcNow});
        db.UserRoleAssignments.Add(new UserRoleAssignment{
            UserRoleAssignmentId=1,UserId=501,RoleId=1,
            EffectiveFrom=today.AddDays(-30),
            EffectiveTo=datedAdmin?null:today.AddDays(-1),
            CreatedAt=DateTime.UtcNow});
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static async Task AssertAllSafeDeleteOperationsDeniedAsync(
        V180SafeDeleteService service)
    {
        var ct=CancellationToken.None;
        // All seven previews and destructive calls must deny before DB writes.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>service.CenterImpactAsync(1,ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>service.DeleteCenterAsync(1,ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>service.SiteImpactAsync(1,ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>service.DeleteSiteAsync(1,ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>service.PersonImpactAsync(1,ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>service.DeletePersonAsync(1,ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>service.TeamImpactAsync(1,ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>service.DeleteTeamAsync(1,ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>service.ProjectImpactAsync(1,ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>service.DeleteProjectAsync(1,ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>service.VisitTypeImpactAsync(1,ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>service.DeleteVisitTypeAsync(1,ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>service.MileageRateImpactAsync(1,ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            ()=>service.DeleteMileageRateAsync(1,ct));
    }

    [Fact]
    public async Task Expired_admin_role_denies_all_fourteen_sensitive_endpoints()
    {
        await using var db=await SeedAsync(datedAdmin:false);
        await AssertAllSafeDeleteOperationsDeniedAsync(
            new V180SafeDeleteService(db,new StubCurrent(Actor())));
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.Equal(1,await db.Users.CountAsync());
    }

    [Theory]
    [InlineData(EmploymentStatuses.Leave)]
    [InlineData(EmploymentStatuses.Terminated)]
    [InlineData(EmploymentStatuses.PreHire)]
    public async Task Invalid_HR_state_denies_all_safe_delete_operations(string state)
    {
        await using var db=await SeedAsync(hrStatus:state);
        await AssertAllSafeDeleteOperationsDeniedAsync(
            new V180SafeDeleteService(db,new StubCurrent(Actor())));
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.Equal(1,await db.Users.CountAsync());
    }

    [Fact]
    public async Task Missing_current_role_projection_denies_all_safe_delete_operations()
    {
        await using var db=await SeedAsync(projectedAdmin:false);
        await AssertAllSafeDeleteOperationsDeniedAsync(
            new V180SafeDeleteService(db,new StubCurrent(Actor())));
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Disabled_account_denies_all_safe_delete_operations()
    {
        await using var db=await SeedAsync(activeAccount:false);
        await AssertAllSafeDeleteOperationsDeniedAsync(
            new V180SafeDeleteService(db,new StubCurrent(Actor())));
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Forged_token_admin_cannot_substitute_for_missing_role()
    {
        await using var db=await SeedAsync();
        await AssertAllSafeDeleteOperationsDeniedAsync(
            new V180SafeDeleteService(db,new StubCurrent(Actor(role:"leader"))));
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Missing_organization_fails_closed_even_with_active_admin()
    {
        await using var db=await SeedAsync();
        await AssertAllSafeDeleteOperationsDeniedAsync(
            new V180SafeDeleteService(db,new StubCurrent(Actor(organization:null))));
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }
}
