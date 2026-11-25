using System.Net;
using System.Net.Http.Json;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class InstalledRateSqlFactAttribute : FactAttribute
{
    public InstalledRateSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FV_RATE_DBA")))
            Skip = "Requires the separately installed, disposable 69-table SQL Server fixture.";
    }
}

public sealed class InstalledRateSqlTests
{
    [InstalledRateSqlFact]
    public async Task Admin_rate_api_obeys_installed_schema_and_retains_existing_rate()
    {
        var dba = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FV_RATE_DBA"));
        var run = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FV_RATE_RUNTIME"));
        Assert.Equal("127.0.0.1,1433", dba.DataSource);
        Assert.Equal(dba.DataSource, run.DataSource);
        Assert.Equal("FieldVisitCompanyCoreTest", dba.InitialCatalog);
        Assert.Equal(dba.InitialCatalog, run.InitialCatalog);
        Assert.Equal("sa", dba.UserID);
        Assert.Equal("fv_core_test", run.UserID);
        await using var sql = new SqlConnection(dba.ConnectionString);
        await sql.OpenAsync();
        async Task<object?> Scalar(string statement)
        {
            await using var command = new SqlCommand(statement, sql);
            return await command.ExecuteScalarAsync();
        }
        Assert.Equal(69, Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo')")));
        Assert.Equal(1, Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM sys.triggers WHERE name='TR_MileageRateRules_ProtectSeries' AND is_disabled=0")));
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(dba.ConnectionString).Options);
        var now = DateTime.UtcNow;
        var from = BusinessTime.Today.AddDays(-20);
        var org = new Organization { OrganizationCode="RATEFIX", OrganizationName="Synthetic rate test", IsActive=true, CreatedAt=now };
        var role = new Role { RoleCode="admin", RoleName="Admin", IsActive=true, CreatedAt=now };
        db.AddRange(org,role);
        await db.SaveChangesAsync();
        var team = new Team { OrganizationId=org.OrganizationId, TeamCode="RATE-T", TeamName="Synthetic team", IsActive=true, EffectiveFrom=from, CreatedAt=now };
        db.Add(team); await db.SaveChangesAsync();
        var user = new User { OrganizationId=org.OrganizationId, TeamId=team.TeamId, EmployeeNo="RATE-A", DisplayName="Synthetic Admin", IsActive=true, CreatedAt=now };
        db.Add(user); await db.SaveChangesAsync();
        var person = new Person { DisplayName=user.DisplayName, LegacyUserId=user.UserId, CreatedAt=now };
        db.Add(person); await db.SaveChangesAsync();
        var employment = new Employment { PersonId=person.PersonId, OrganizationId=org.OrganizationId, EmployeeNo=user.EmployeeNo, LegacyUserId=user.UserId, HireDate=from, SourceType="Manual" };
        db.Add(employment); await db.SaveChangesAsync();
        db.AddRange(
            new UserIdentityProfile { UserId=user.UserId, EmploymentId=employment.EmploymentId, UserType=UserTypes.Internal, UserCode=user.EmployeeNo!, IdentityProvider="Demo", CreatedAt=now },
            new EmploymentStatusPeriod { EmploymentId=employment.EmploymentId, EmploymentStatus=EmploymentStatuses.Active, EffectiveFrom=from, SourceType="Manual" },
            new UserEmploymentPeriod { UserId=user.UserId, EmploymentStatus=EmploymentStatuses.Active, EffectiveFrom=from, SourceType="Manual", CreatedAt=now },
            new UserRole { UserId=user.UserId, RoleId=role.RoleId, AssignedAt=now },
            new UserRoleAssignment { UserId=user.UserId, RoleId=role.RoleId, EffectiveFrom=from, CreatedAt=now },
            new UserTeamAssignment { UserId=user.UserId, TeamId=team.TeamId, IsPrimary=true, EffectiveFrom=from, CreatedAt=now },
            new UserTeamScope { UserId=user.UserId, TeamId=team.TeamId, IsPrimary=true, IsActive=true, AssignedAt=now },
            new TeamMembership { EmploymentId=employment.EmploymentId, TeamId=team.TeamId, IsPrimary=true, EffectiveFrom=from });
        await db.SaveChangesAsync();
        var day = BusinessTime.Today.AddDays(1);
        // Same physical input produced by the original API. Prove the real trigger
        // rejects it, independently of the application value converter.
        await using (var cmd = new SqlCommand("INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive,CreatedAt) VALUES(@org,N'BASELINE-REJECT',N'Car',2.50,@day,@day,1,SYSUTCDATETIME())", sql))
        {
            cmd.Parameters.AddWithValue("@org", org.OrganizationId);
            cmd.Parameters.AddWithValue("@day", day.ToDateTime(TimeOnly.MinValue));
            var failure = await Assert.ThrowsAsync<SqlException>(async () => { await cmd.ExecuteNonQueryAsync(); });
            Assert.Contains(failure.Number, new[] { 547, 54431 });
        }
        Assert.Equal(0, Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM dbo.MileageRateRules WHERE RuleName=N'BASELINE-REJECT'")));
        Console.WriteLine("INSTALLED_RATE_ORIGINAL_MIXED_CASE_REJECTED=PASS");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.MileageRateRules(OrganizationId,RuleName,VehicleType,RatePerKm,EffectiveFrom,EffectiveTo,IsActive,CreatedAt) VALUES({org.OrganizationId},{"RATE-EXISTING"},{"MOTORCYCLE"},{2.5m},{from.ToDateTime(TimeOnly.MinValue)},{day.AddDays(-1).ToDateTime(TimeOnly.MinValue)},{true},{now})");
        const string password = "disposable-test-only-rate-password";
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", run.ConnectionString);
            builder.UseSetting("Auth:Mode", "Demo");
            builder.UseSetting("Auth:DemoPassword", password);
            builder.UseSetting("Auth:JwtKey", "disposable-rate-test-key-over-32-characters");
            builder.UseSetting("BackgroundJobs:WorkerEnabled", "false");
            builder.UseSetting("BackgroundJobs:RecoverOnStartup", "false");
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        async Task<T> Read<T>(HttpResponseMessage res)
        {
            using (res)
            {
                Assert.True(res.IsSuccessStatusCode, "Rate API status " + (int)res.StatusCode);
                return (await res.Content.ReadFromJsonAsync<T>())!;
            }
        }
        var login = await Read<DemoLoginResponse>(await client.PostAsJsonAsync("api/v1/auth/demo-login", new DemoLoginRequest(user.EmployeeNo!,password)));
        Assert.Equal(user.UserId,login.User.UserId);
        Assert.Contains("admin",login.User.Roles);
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.AccessToken);
        client.DefaultRequestHeaders.Add("X-Active-Role","admin");
        var impact = await Read<MileageRateImpactDto>(await client.GetAsync($"api/v1/mileage-rate-rules/impact?effectiveFrom={day:yyyy-MM-dd}&vehicleType=Car"));
        Assert.Equal(0, impact.ApprovedTripCount);
        var car = await Read<MileageRateDto>(await client.PostAsJsonAsync("api/v1/mileage-rate-rules",new CreateMileageRateRequest("RATE-CAR","Car",2.5m,day,day)));
        Assert.Equal("Car",car.VehicleType);
        var motorcycle = await Read<MileageRateDto>(await client.PostAsJsonAsync("api/v1/mileage-rate-rules",new CreateMileageRateRequest("RATE-MOTO","motorcycle",2.5m,day,day)));
        Assert.Equal("Motorcycle",motorcycle.VehicleType);
        Assert.Equal("CAR",await Scalar("SELECT VehicleType FROM dbo.MileageRateRules WHERE RuleName=N'RATE-CAR'"));
        Assert.Equal("MOTORCYCLE",await Scalar("SELECT VehicleType FROM dbo.MileageRateRules WHERE RuleName=N'RATE-MOTO'"));
        using(var overlap = await client.PostAsJsonAsync("api/v1/mileage-rate-rules",new CreateMileageRateRequest("RATE-OVERLAP","CAR",3m,day,day)))
            Assert.Equal(HttpStatusCode.UnprocessableEntity,overlap.StatusCode);
        car=await Read<MileageRateDto>(await client.PutAsJsonAsync($"api/v1/mileage-rate-rules/{car.MileageRateRuleId}",new UpdateMileageRateRequest("RATE-CAR","cAr",2.75m,day,day,true)));
        Assert.Equal(2.75m,car.RatePerKm); Assert.Equal("Car",car.VehicleType);
        using (var off = await client.DeleteAsync($"api/v1/mileage-rate-rules/{car.MileageRateRuleId}")) Assert.Equal(HttpStatusCode.NoContent,off.StatusCode);
        var rows=await Read<List<MileageRateDto>>(await client.GetAsync("api/v1/mileage-rate-rules"));
        Assert.False(rows.Single(x=>x.MileageRateRuleId==car.MileageRateRuleId).IsActive);
        Assert.Equal("Car",rows.Single(x=>x.MileageRateRuleId==car.MileageRateRuleId).VehicleType);
        await using var runtimeDb=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(run.ConnectionString).Options);
        var repo=new MileageRepository(runtimeDb);
        var existing=await repo.GetEffectiveRateAsync(org.OrganizationId,"Motorcycle",day.AddDays(-1),CancellationToken.None);
        Assert.NotNull(existing); Assert.Equal("RATE-EXISTING",existing.RuleName); Assert.Equal(2.5m,existing.RatePerKm);
        var effective=await repo.GetEffectiveRateAsync(org.OrganizationId,"Motorcycle",day,CancellationToken.None);
        Assert.Equal(motorcycle.MileageRateRuleId,effective?.MileageRateRuleId);
        Assert.Equal(0,Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM dbo.MileageRateRules WHERE RuleName=N'RATE-OVERLAP'")));
        await using var restricted=new SqlConnection(run.ConnectionString); await restricted.OpenAsync();
        await using (var check = new SqlCommand("SELECT IS_ROLEMEMBER(N'db_owner')+IS_ROLEMEMBER(N'db_datareader')+IS_ROLEMEMBER(N'db_datawriter')",restricted))
            Assert.Equal(0,Convert.ToInt32(await check.ExecuteScalarAsync()));
        await using (var forbidden = new SqlCommand("DELETE FROM dbo.MileageRateRules WHERE RuleName=N'RATE-CAR'",restricted))
        {
            var failure=await Assert.ThrowsAsync<SqlException>(async()=>{await forbidden.ExecuteNonQueryAsync();});
            Assert.Contains(failure.Number,new[]{229,230});
        }
        Assert.Equal(1,Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM dbo.MileageRateRules WHERE RuleName=N'RATE-CAR' AND IsActive=0 AND VehicleType=N'CAR' AND RatePerKm=2.75")));
        Console.WriteLine("INSTALLED_RATE_API_LIFECYCLE=PASS physical_uppercase=true existing_rate_unchanged=true overlap_denied=true physical_delete_denied=true");
    }
}
