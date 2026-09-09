using System.Reflection;
using FieldVisit.Api.Controllers;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180TeamCenterUiCutoverTests
{
    private static readonly byte[] Version = [1,2,3,4,5,6,7,8];
    private static readonly DateOnly Start = new(2026,1,1);
    private static readonly DateOnly End = new(2026,12,31);
    private static CurrentUserDto Admin(int org = 1) => new(99,"ADMIN","Admin",null,org,null,null,["admin"]);
    private static AppDbContext MemoryDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Detail_reads_preserve_notes_and_rowversion_and_are_organization_scoped()
    {
        await using var db = MemoryDb();
        db.Organizations.AddRange(
            new Organization{OrganizationId=1,OrganizationCode="O1",OrganizationName="Org 1",IsActive=true,RowVersion=Version},
            new Organization{OrganizationId=2,OrganizationCode="O2",OrganizationName="Org 2",IsActive=true,RowVersion=Version});
        db.Teams.Add(new Team{TeamId=10,OrganizationId=1,TeamCode="T1",TeamName="Team",EffectiveFrom=Start,IsActive=true,Notes="team note",RowVersion=Version});
        db.Centers.Add(new Center{CenterId=20,OrganizationId=1,CenterCode="C1",CenterName="Center",EffectiveFrom=Start,IsActive=true,Notes="center note",RowVersion=Version});
        await db.SaveChangesAsync();
        var reader = new V180TeamCenterAdminReader(db);
        var team = await reader.GetTeamAsync(Admin(),10,default);
        var center = await reader.GetCenterAsync(Admin(),20,default);
        Assert.Equal("team note",team!.Notes);
        Assert.Equal("center note",center!.Notes);
        Assert.Equal(Convert.ToBase64String(Version),team.Version);
        Assert.Null(await reader.GetTeamAsync(Admin(2),10,default));
        Assert.Null(await reader.GetCenterAsync(Admin(2),20,default));
    }

    [Fact]
    public async Task Assignment_read_exposes_history_identity_version_and_fails_closed_on_org_mismatch()
    {
        await using var db = MemoryDb();
        db.Organizations.AddRange(
            new Organization{OrganizationId=1,OrganizationCode="O1",OrganizationName="Org 1",IsActive=true,RowVersion=Version},
            new Organization{OrganizationId=2,OrganizationCode="O2",OrganizationName="Org 2",IsActive=true,RowVersion=Version});
        db.Teams.Add(new Team{TeamId=10,OrganizationId=1,TeamCode="T1",TeamName="Team",EffectiveFrom=Start,IsActive=true,RowVersion=Version});
        db.Centers.Add(new Center{CenterId=20,OrganizationId=1,CenterCode="C1",CenterName="Center",EffectiveFrom=Start,IsActive=true,RowVersion=Version});
        db.TeamCenterAssignments.Add(new TeamCenterAssignment{TeamCenterAssignmentId=30,TeamId=10,CenterId=20,EffectiveFrom=Start,EffectiveTo=End,ChangeReason="move",RowVersion=Version});
        await db.SaveChangesAsync();
        var reader = new V180TeamCenterAdminReader(db);
        var rows = await reader.ListAssignmentsAsync(Admin(),10,true,Start,default);
        var row = Assert.Single(rows);
        Assert.Equal(30,row.TeamCenterAssignmentId);
        Assert.Equal("C1",row.CenterCode);
        Assert.Equal("move",row.ChangeReason);
        Assert.Equal(Convert.ToBase64String(Version),row.Version);

        db.Centers.Add(new Center{CenterId=21,OrganizationId=2,CenterCode="C2",CenterName="Wrong Org",EffectiveFrom=Start,IsActive=true,RowVersion=Version});
        db.TeamCenterAssignments.Add(new TeamCenterAssignment{TeamCenterAssignmentId=31,TeamId=10,CenterId=21,EffectiveFrom=End.AddDays(1),RowVersion=Version});
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ListAssignmentsAsync(Admin(),10,true,Start,default));
    }

    [Fact]
    public void Additive_admin_read_routes_are_available_for_safe_lifecycle_editing()
    {
        var controller = typeof(V180TeamCenterAdminController);
        foreach (var (method,route) in new[]{
            ("Team","teams/{teamId:int}"),
            ("Center","centers/{centerId:int}"),
            ("Assignments","team-center-assignments")})
        {
            var attribute = controller.GetMethod(method)!.GetCustomAttributes<HttpMethodAttribute>().Single();
            Assert.Equal(route,attribute.Template);
        }
    }

    [Fact]
    public void Team_management_ui_uses_only_v18_lifecycle_master_writes_and_keeps_people_v18()
    {
        var source = Source("frontend/src/pages/TeamManagementPage.tsx");
        Assert.Contains("/admin/v180/teams",source);
        Assert.Contains("/admin/v180/centers",source);
        Assert.Contains("/admin/v180/team-center-assignments",source);
        Assert.Contains("/admin/v180/people/${u.employmentId}/access",source);
        Assert.Contains("version:editTeam.version",source);
        Assert.Contains("version:editCenter.version",source);
        Assert.Contains("version:editAssignment.version",source);
        Assert.DoesNotContain("api(\"/admin/teams\"",source);
        Assert.DoesNotContain("/admin/teams/${",source);
        Assert.DoesNotContain("'/admin/teams/search'",source);
    }

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName,"backend","FieldVisitSystem.sln")))
            directory=directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName,relative));
    }
}
