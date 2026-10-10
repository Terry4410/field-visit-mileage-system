using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// In-memory repository regression only. These tests must never connect to
/// UAT SQL or execute DDL/migrations.
/// </summary>
public sealed class V180ManagedLocationDeleteRepositoryGatesTests
{
    private static CurrentUserDto Token(int organizationId=1) =>
        new(101,"admin101","Admin",null,organizationId,7,"Team 7",
            new[]{"admin"},new[]{new TeamScopeDto(7,"Team 7",true)});

    private static async Task<AppDbContext> SeedAsync(bool liveAdmin=true,
        string status=EmploymentStatuses.Active)
    {
        var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"v180-managed-delete-{Guid.NewGuid()}").Options);
        var today=DateOnly.FromDateTime(DateTime.UtcNow);
        db.Users.Add(new User{UserId=101,OrganizationId=1,
            DisplayName="Admin",IsActive=true,CreatedAt=DateTime.UtcNow});
        db.UserEmploymentPeriods.Add(new UserEmploymentPeriod{
            UserEmploymentPeriodId=1,UserId=101,EmploymentStatus=status,
            EffectiveFrom=today.AddDays(-10)});
        db.Roles.Add(new Role{RoleId=1,RoleCode="admin",RoleName="Admin",
            IsActive=true,CreatedAt=DateTime.UtcNow});
        db.UserRoles.Add(new UserRole{UserRoleId=1,UserId=101,RoleId=1,
            AssignedAt=DateTime.UtcNow});
        db.UserRoleAssignments.Add(new UserRoleAssignment{
            UserRoleAssignmentId=1,UserId=101,RoleId=1,
            EffectiveFrom=today.AddDays(-10),
            EffectiveTo=liveAdmin?null:today.AddDays(-1),
            CreatedAt=DateTime.UtcNow});
        db.Locations.Add(new Location{LocationId=9,OrganizationId=1,TeamId=7,
            LocationCode="LOC-9",LocationName="Protected",LocationType="Customer",
            Address="Road 1",ApprovalStatus="Approved",GeocodingStatus="Completed",
            IsActive=true,CreatedByUserId=101,CreatedAt=DateTime.UtcNow});
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static V160FinalRepository Repository(AppDbContext db) =>
        new(db,new V170AccessControl(db));

    [Fact]
    public async Task Expired_admin_token_cannot_deactivate_or_delete_own_org_location()
    {
        await using var db=await SeedAsync(liveAdmin:false);
        var repo=Repository(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            repo.DeactivateManagedLocationAsync(Token(),9,CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            repo.GetManagedLocationDeleteImpactAsync(Token(),9,CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            repo.DeleteManagedLocationAsync(Token(),9,CancellationToken.None));
        var row=await db.Locations.AsNoTracking().SingleAsync();
        Assert.True(row.IsActive);
        Assert.Equal("Approved",row.ApprovalStatus);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData(EmploymentStatuses.Leave)]
    [InlineData(EmploymentStatuses.Terminated)]
    public async Task HR_leave_and_termination_block_deactivation(string status)
    {
        await using var db=await SeedAsync(status:status);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            Repository(db).DeactivateManagedLocationAsync(Token(),9,CancellationToken.None));
        Assert.True((await db.Locations.AsNoTracking().SingleAsync()).IsActive);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Published_shared_location_cannot_be_mutated_through_org_admin()
    {
        await using var db=await SeedAsync();
        var global=await db.Locations.SingleAsync();
        global.OrganizationId=null;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            Repository(db).DeactivateManagedLocationAsync(Token(),9,CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            Repository(db).GetManagedLocationDeleteImpactAsync(Token(),9,CancellationToken.None));
        Assert.Equal(1,await db.Locations.CountAsync());
    }

    [Fact]
    public async Task Historical_note_geocoding_and_deployment_refs_all_block_delete_impact()
    {
        await using var db=await SeedAsync();
        db.TeamLocationNoteHistories.Add(new TeamLocationNoteHistory{
            TeamLocationNoteHistoryId=1,TeamLocationNoteId=1,
            TeamId=7,LocationId=9,Action="Updated",ChangedByUserId=101});
        db.GeocodingAttempts.Add(new GeocodingAttempt{
            GeocodingAttemptId=1,LocationId=9,RequestedByUserId=101,
            Provider="Mock",RequestedAt=DateTime.UtcNow});
        db.DeploymentSiteLocationAssignments.Add(new DeploymentSiteLocationAssignment{
            DeploymentSiteLocationAssignmentId=1,LocationId=9,DeploymentSiteId=1,
            EffectiveFrom=DateOnly.FromDateTime(DateTime.UtcNow)});
        db.Locations.Add(new Location{LocationId=10,OrganizationId=1,TeamId=7,
            LocationName="Duplicate",LocationType="Customer",Address="Road 2",
            DuplicateOfLocationId=9,CreatedAt=DateTime.UtcNow});
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var impact=await Repository(db).GetManagedLocationDeleteImpactAsync(Token(),9,CancellationToken.None);
        Assert.False(impact.CanDelete);
        Assert.Contains("備註變更歷史",impact.Reason);
        Assert.Contains("地理解析歷史",impact.Reason);
        Assert.Contains("派駐據點期間",impact.Reason);
        Assert.Contains("合併/重複地點關聯",impact.Reason);
        // All original public counters may be zero; the new history blockers
        // must still prevent permanent deletion without changing that DTO.
        Assert.Equal(0,impact.TripReferenceCount);
        Assert.Equal(0,impact.ProjectReferenceCount);
        Assert.Equal(0,impact.FavoriteReferenceCount);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Without_history_impact_allows_delete_preview_but_never_mutates()
    {
        await using var db=await SeedAsync();
        var impact=await Repository(db).GetManagedLocationDeleteImpactAsync(Token(),9,CancellationToken.None);
        Assert.True(impact.CanDelete);
        Assert.Null(impact.Reason);
        Assert.True((await db.Locations.AsNoTracking().SingleAsync()).IsActive);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }
}
