using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// In-memory persistence regression: an old token plus legacy UserRoles
/// projection does not authorize privileged Location write operations.
/// Tests intentionally exercise the repository, not only the pure role policy.
/// </summary>
public sealed class V180LocationAdminRepositoryFailClosedTests
{
    private static CurrentUserDto AdminToken() =>
        new(101,"admin101","Admin",null,1,7,"Team 7",
            new[]{"admin"},new[]{new TeamScopeDto(7,"Team 7",true)});

    private static async Task<AppDbContext> SeedAsync(
        bool liveRole,string employmentStatus)
    {
        var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"v180-admin-gates-{Guid.NewGuid()}").Options);
        var today=DateOnly.FromDateTime(DateTime.UtcNow);
        db.Users.Add(new User{
            UserId=101,OrganizationId=1,IsActive=true,
            DisplayName="Test Admin",CreatedAt=DateTime.UtcNow});
        db.UserEmploymentPeriods.Add(new UserEmploymentPeriod{
            UserEmploymentPeriodId=1,UserId=101,
            EmploymentStatus=employmentStatus,
            EffectiveFrom=today.AddDays(-30)});
        db.Roles.Add(new Role{
            RoleId=1,RoleCode="admin",RoleName="Admin",
            IsActive=true,CreatedAt=DateTime.UtcNow});
        db.UserRoles.Add(new UserRole{
            UserRoleId=1,UserId=101,RoleId=1,AssignedAt=DateTime.UtcNow});
        db.UserRoleAssignments.Add(new UserRoleAssignment{
            UserRoleAssignmentId=1,UserId=101,RoleId=1,
            EffectiveFrom=today.AddDays(-30),
            EffectiveTo=liveRole?null:today.AddDays(-1),
            CreatedAt=DateTime.UtcNow});
        db.Locations.Add(new Location{
            LocationId=9,OrganizationId=1,TeamId=7,
            LocationName="Protected location",LocationType="Customer",
            Address="Road 1",ApprovalStatus="Approved",
            IsActive=true,CreatedByUserId=101,CreatedAt=DateTime.UtcNow});
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    [Fact]
    public async Task Expired_admin_role_denies_all_three_location_write_paths_without_mutations()
    {
        await using var db=await SeedAsync(false,EmploymentStatuses.Active);
        var repo=new V170LocationRepository(db);
        var actor=AdminToken();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            repo.UpdateMaintenanceAsync(actor,9,
                new V170LocationMaintenanceUpdateRequest(
                    "Taipei",null,"Changed road",null,null,null,""),
                CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            repo.ConfirmDistinctAsync(actor,9,
                new V170LocationDuplicateDistinctRequest(10,"reason","",true),
                CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            repo.MergeAsync(actor,9,
                new V170LocationMergeRequest(10,"reason","",true),
                CancellationToken.None));
        var row=await db.Locations.AsNoTracking().SingleAsync();
        Assert.Equal("Road 1",row.Address);
        Assert.True(row.IsActive);
        Assert.Equal("Approved",row.ApprovalStatus);
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.Empty(await db.LocationApprovalHistories.ToListAsync());
    }

    [Theory]
    [InlineData(EmploymentStatuses.Leave)]
    [InlineData(EmploymentStatuses.Terminated)]
    public async Task Ineligible_HR_status_denies_master_write_even_with_live_admin_role(
        string status)
    {
        await using var db=await SeedAsync(true,status);
        var repo=new V170LocationRepository(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            repo.UpdateMaintenanceAsync(AdminToken(),9,
                new V170LocationMaintenanceUpdateRequest(
                    null,null,"Changed road",null,null,null,""),
                CancellationToken.None));
        Assert.Equal("Road 1",(await db.Locations.AsNoTracking().SingleAsync()).Address);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }
}
