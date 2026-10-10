using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Guard the three bulk write entry points against a stale Admin token.
/// InMemory only. No UAT DB, actual workbook mutation or migrations.
/// </summary>
public sealed class V180BulkWriteAdminGatesTests
{
    private static CurrentUserDto Actor(int? organization=1,string role="admin") =>
        new(901,"admin901","Admin",null,organization,7,"Team 7",
            [role],[new TeamScopeDto(7,"Team 7",true)]);

    private static async Task<AppDbContext> SeedAsync(
        bool active=true,string hr=EmploymentStatuses.Active,
        bool dated=true,bool projected=true)
    {
        var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"v180-bulk-admin-{Guid.NewGuid()}").Options);
        var today=DateOnly.FromDateTime(DateTime.UtcNow);
        db.Users.Add(new User{
            UserId=901,OrganizationId=1,DisplayName="Admin",
            IsActive=active,CreatedAt=DateTime.UtcNow});
        db.UserEmploymentPeriods.Add(new UserEmploymentPeriod{
            UserEmploymentPeriodId=901,UserId=901,EmploymentStatus=hr,
            EffectiveFrom=today.AddDays(-20)});
        db.Roles.Add(new Role{RoleId=1,RoleCode="admin",RoleName="Admin",
            IsActive=true,CreatedAt=DateTime.UtcNow});
        if(projected)db.UserRoles.Add(new UserRole{
            UserRoleId=901,UserId=901,RoleId=1,AssignedAt=DateTime.UtcNow});
        db.UserRoleAssignments.Add(new UserRoleAssignment{
            UserRoleAssignmentId=901,UserId=901,RoleId=1,
            EffectiveFrom=today.AddDays(-20),
            EffectiveTo=dated?null:today.AddDays(-1),
            CreatedAt=DateTime.UtcNow});
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static async Task VerifyDeniedNoWritesAsync(
        AppDbContext db, CurrentUserDto admin)
    {
        // Invalid payloads are intentional; authorization must fail first,
        // without parsing the workbook or querying/modifying target members.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            new V180PersonnelBulkService(db).ConfirmAsync(
                admin,[],CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            new V180TeamMembershipBulkService(db).ConfirmTeamMembershipAsync(
                admin,[],CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            new V180TeamMembershipBulkService(db).BatchAddTeamMembersAsync(
                admin,999,new V180BatchAddTeamMembersRequest(
                    [],DateOnly.FromDateTime(DateTime.UtcNow)),CancellationToken.None));

        Assert.Empty(await db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(await db.TeamMemberships.AsNoTracking().ToListAsync());
        Assert.Empty(await db.UserTeamAssignments.AsNoTracking().ToListAsync());
        Assert.Empty(await db.EmploymentStatusPeriods.AsNoTracking().ToListAsync());
        Assert.Equal(1,await db.UserRoleAssignments.CountAsync());
        Assert.Equal(1,await db.Users.CountAsync());
    }

    [Fact] public async Task Expired_admin_role_denies_all_three_bulk_mutations()
    {
        await using var db=await SeedAsync(dated:false);
        await VerifyDeniedNoWritesAsync(db,Actor());
    }

    [Theory]
    [InlineData(EmploymentStatuses.Leave)]
    [InlineData(EmploymentStatuses.Terminated)]
    [InlineData(EmploymentStatuses.PreHire)]
    public async Task HR_ineligible_admin_denies_all_three_bulk_mutations(string state)
    {
        await using var db=await SeedAsync(hr:state);
        await VerifyDeniedNoWritesAsync(db,Actor());
    }

    [Fact] public async Task Disabled_account_denies_all_three_bulk_mutations()
    {
        await using var db=await SeedAsync(active:false);
        await VerifyDeniedNoWritesAsync(db,Actor());
    }

    [Fact] public async Task Missing_role_projection_denies_bulk_mutations()
    {
        await using var db=await SeedAsync(projected:false);
        await VerifyDeniedNoWritesAsync(db,Actor());
    }

    [Fact] public async Task Leader_role_cannot_inherit_admin_bulk_permissions()
    {
        await using var db=await SeedAsync();
        await VerifyDeniedNoWritesAsync(db,Actor(role:"leader"));
    }

    [Fact] public async Task Cross_org_and_null_org_are_denied()
    {
        await using var db=await SeedAsync();
        await VerifyDeniedNoWritesAsync(db,Actor(organization:2));
        await VerifyDeniedNoWritesAsync(db,Actor(organization:null));
    }
}
