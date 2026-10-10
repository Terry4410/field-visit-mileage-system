using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// External Supervisor (or unknown identity category) must not obtain write
/// authority through malformed Admin role data. InMemory; no database deploy.
/// Existing legacy Internal accounts without a profile remain supported.
/// </summary>
public sealed class V180ExternalAdminWriteDenialTests
{
    private static CurrentUserDto Actor() => new(
        920,"A920","Admin",null,1,7,"Team",
        new[]{"admin"},new[]{new TeamScopeDto(7,"Team",true)});

    private static async Task<AppDbContext> DbAsync(string? identityType)
    {
        var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"b4-external-admin-{Guid.NewGuid()}").Options);
        var today=DateOnly.FromDateTime(DateTime.UtcNow);
        db.Users.Add(new User{UserId=920,OrganizationId=1,DisplayName="Admin",
            IsActive=true,CreatedAt=DateTime.UtcNow});
        db.UserEmploymentPeriods.Add(new UserEmploymentPeriod{
            UserEmploymentPeriodId=920,UserId=920,
            EmploymentStatus=EmploymentStatuses.Active,
            EffectiveFrom=today.AddDays(-3)});
        db.Roles.Add(new Role{RoleId=1,RoleCode="admin",
            RoleName="Admin",IsActive=true,CreatedAt=DateTime.UtcNow});
        db.UserRoles.Add(new UserRole{UserRoleId=920,UserId=920,
            RoleId=1,AssignedAt=DateTime.UtcNow});
        db.UserRoleAssignments.Add(new UserRoleAssignment{
            UserRoleAssignmentId=920,UserId=920,RoleId=1,
            EffectiveFrom=today.AddDays(-3),CreatedAt=DateTime.UtcNow});
        if(identityType is not null)
            db.UserIdentityProfiles.Add(new UserIdentityProfile{
                UserId=920,UserType=identityType,UserCode="A920",
                CreatedAt=DateTime.UtcNow});
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    [Theory]
    [InlineData(UserTypes.External)]
    [InlineData("Unknown")]
    [InlineData("")]
    public async Task External_or_unknown_identity_cannot_mutate_despite_live_admin(string type)
    {
        await using var db=await DbAsync(type);
        var ex=await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            V180CurrentAdminWriteGuard.RequireAsync(db,Actor(),CancellationToken.None));
        Assert.Contains("ADMIN_WRITE_INTERNAL_IDENTITY_REQUIRED",ex.Message);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(UserTypes.Internal)]
    public async Task Internal_and_legacy_internal_profiles_retain_valid_guard(string? type)
    {
        await using var db=await DbAsync(type);
        var actual=await V180CurrentAdminWriteGuard.RequireAsync(
            db,Actor(),CancellationToken.None);
        Assert.Equal(920,actual.UserId);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }
}
