using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace FieldVisit.Application.Tests;

public sealed partial class V180TripMileageHttpSqlE2ETests
{
    [Fact]
    public async Task CompanyBrowserSeed()
    {
        var dba = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FV_CORE_DBA") ?? throw new Exception("DBA_REQUIRED"));
        Assert.Equal("FieldVisitCompanyCoreTest", dba.InitialCatalog);
        Assert.Equal("127.0.0.1,1433", dba.DataSource);
        Assert.Equal("sa", dba.UserID);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(dba.ConnectionString).Options);
        var f = await CompanySeedAsync(db);
        var now = DateTime.UtcNow;
        var since = new DateOnly(2020,1,1);
        var role = new Role { RoleCode="admin", RoleName="Admin", IsActive=true, CreatedAt=now };
        var user = new User { OrganizationId=f.Visitor.OrganizationId, TeamId=f.Team.TeamId, EmployeeNo="E2E-A", DisplayName="E2E Admin", IsActive=true, CreatedAt=now };
        db.AddRange(role,user); await db.SaveChangesAsync();
        var person = new Person { DisplayName=user.DisplayName, LegacyUserId=user.UserId, CreatedAt=now };
        db.Persons.Add(person); await db.SaveChangesAsync();
        var emp = new Employment { PersonId=person.PersonId, OrganizationId=f.Team.OrganizationId, EmployeeNo=user.EmployeeNo, LegacyUserId=user.UserId, HireDate=since, SourceType="Manual" };
        db.Employments.Add(emp); await db.SaveChangesAsync();
        db.AddRange(
            new UserIdentityProfile { UserId=user.UserId, EmploymentId=emp.EmploymentId, UserType=UserTypes.Internal, UserCode=user.EmployeeNo!, IdentityProvider="Demo", CreatedAt=now },
            new EmploymentStatusPeriod { EmploymentId=emp.EmploymentId, EmploymentStatus=EmploymentStatuses.Active, EffectiveFrom=since, SourceType="Manual" },
            new UserEmploymentPeriod { UserId=user.UserId, EmploymentStatus=EmploymentStatuses.Active, EffectiveFrom=since, SourceType="Manual", CreatedAt=now },
            new UserRole { UserId=user.UserId, RoleId=role.RoleId, AssignedAt=now },
            new UserRoleAssignment { UserId=user.UserId, RoleId=role.RoleId, EffectiveFrom=since, CreatedAt=now },
            new TeamMembership { EmploymentId=emp.EmploymentId, TeamId=f.Team.TeamId, IsPrimary=true, EffectiveFrom=since },
            new UserTeamAssignment { UserId=user.UserId, TeamId=f.Team.TeamId, IsPrimary=true, EffectiveFrom=since, CreatedAt=now },
            new UserTeamScope { UserId=user.UserId, TeamId=f.Team.TeamId, IsPrimary=true, IsActive=true, AssignedAt=now });
        await db.SaveChangesAsync();
        Assert.Equal(3,await db.Users.CountAsync(x=>x.EmployeeNo!=null && x.EmployeeNo.StartsWith("E2E-")));
        Console.WriteLine("BROWSER_FIXTURE_THREE_ACTORS=PASS");
    }
}
