using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FieldVisit.Api.Controllers;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Actual ASP.NET Core login HTTP + AuthService + UserRepository +
/// V170AccessControl + EF HR periods, backed ONLY by a private InMemory DB.
/// B3 stays OFF, no existing SQL server, no UAT or production operations.
/// This does not prove deployed API token revocation or formal B4 acceptance.
/// </summary>
public sealed class B4IsolatedHrLoginFactory : WebApplicationFactory<V180B3ChangeRequestsController>
{
    public const string LoginAccount="B4-HR-FIXTURE";
    public const string LoginPassword="B4-fixture-password-local-only";
    private const int UserId=84321;
    private readonly string _databaseName="B4HttpHr-"+Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("B4IsolatedHrTests");
        builder.ConfigureAppConfiguration((_,cfg)=>cfg.AddInMemoryCollection(
            new Dictionary<string,string?>
            {
                ["Auth:Mode"]="Demo",
                ["Auth:Issuer"]=B3DisabledHttpFactory.Issuer,
                ["Auth:Audience"]=B3DisabledHttpFactory.Audience,
                ["Auth:JwtKey"]=B3DisabledHttpFactory.TestKey,
                ["Auth:DemoPassword"]=LoginPassword,
                ["PackageB:B3:Enabled"]="false",
                ["ConnectionStrings:DefaultConnection"]=
                    "Server=127.0.0.1,14336;Database=NoConnectionAllowed;User ID=unused;Password=unused;Connect Timeout=1;Encrypt=False",
                ["BackgroundJobs:WorkerEnabled"]="false",
                ["BackgroundJobs:RecoverOnStartup"]="false",
                ["Swagger:Enabled"]="false"
            }));
        builder.ConfigureTestServices(services=>
        {
            // Replace ONLY the test host's registered AppDbContext.
            // The real UserRepository, HR evaluator and AuthService remain.
            services.RemoveAll<AppDbContext>();
            services.AddScoped<AppDbContext>(_=>new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase(_databaseName).Options));
            services.PostConfigure<JwtBearerOptions>("AppJwt",options=>
            {
                options.TokenValidationParameters.ValidIssuer=
                    B3DisabledHttpFactory.Issuer;
                options.TokenValidationParameters.ValidAudience=
                    B3DisabledHttpFactory.Audience;
                options.TokenValidationParameters.IssuerSigningKey=
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                        B3DisabledHttpFactory.TestKey));
            });
            foreach(var service in services.Where(s=>
                s.ServiceType==typeof(IHostedService) &&
                s.ImplementationType?.Name=="BackgroundJobHostedService").ToArray())
                services.Remove(service);
        });
    }

    public HttpClient Client()=>CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress=new Uri("https://localhost"),
        AllowAutoRedirect=false
    });

    public async Task SeedAsync(string? status, int fromDays=-30, int? toDays=null)
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new User
        {
            UserId=UserId,OrganizationId=1,EmployeeNo=LoginAccount,
            DisplayName="Isolated HR Test User",IsActive=true,
            CreatedAt=DateTime.UtcNow
        });
        if(status is not null)
            db.UserEmploymentPeriods.Add(new UserEmploymentPeriod
            {
                UserEmploymentPeriodId=1001,UserId=UserId,EmploymentStatus=status,
                EffectiveFrom=BusinessTime.Today.AddDays(fromDays),
                EffectiveTo=toDays.HasValue?BusinessTime.Today.AddDays(toDays.Value):null
            });
        await db.SaveChangesAsync();
    }

    public async Task AddStatusAsync(string status,int daysFromToday=0)
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserEmploymentPeriods.Add(new UserEmploymentPeriod
        {
            UserEmploymentPeriodId=1002,UserId=UserId,EmploymentStatus=status,
            EffectiveFrom=BusinessTime.Today.AddDays(daysFromToday)
        });
        await db.SaveChangesAsync();
    }

    public static Task<HttpResponseMessage> Login(HttpClient client,
        string account=LoginAccount,string password=LoginPassword)=>
        client.PostAsJsonAsync("/api/v1/auth/demo-login",new { Account=account,Password=password });
}

public sealed class V180HrLoginHttpPipelineTests
{
    private static async Task DeniedAsync(string? status,int from=-30,int? to=null)
    {
        using var factory=new B4IsolatedHrLoginFactory();
        using var client=factory.Client();
        await factory.SeedAsync(status,from,to);
        using var response=await B4IsolatedHrLoginFactory.Login(client);
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        var body=await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken",body,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HTTP_active_HR_login_issues_valid_app_JWT()
    {
        using var factory=new B4IsolatedHrLoginFactory();
        using var client=factory.Client();
        await factory.SeedAsync(EmploymentStatuses.Active);
        using var response=await B4IsolatedHrLoginFactory.Login(client);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token=json.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        client.DefaultRequestHeaders.Authorization=
            new AuthenticationHeaderValue("Bearer",token);
        using var me=await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK,me.StatusCode);
        Assert.Contains(B4IsolatedHrLoginFactory.LoginAccount,
            await me.Content.ReadAsStringAsync(),StringComparison.Ordinal);
    }

    [Fact]
    public Task HTTP_leave_HR_login_403_without_token()=>
        DeniedAsync(EmploymentStatuses.Leave);

    [Fact]
    public Task HTTP_terminated_HR_login_403_without_token()=>
        DeniedAsync(EmploymentStatuses.Terminated);

    [Fact]
    public Task HTTP_prehire_HR_login_403_without_token()=>
        DeniedAsync(EmploymentStatuses.PreHire);

    [Fact]
    public Task HTTP_missing_HR_login_403_without_token()=>
        DeniedAsync(null);

    [Fact]
    public Task HTTP_future_active_HR_login_403_without_token()=>
        DeniedAsync(EmploymentStatuses.Active,from:1);

    [Fact]
    public Task HTTP_expired_active_HR_login_403_without_token()=>
        DeniedAsync(EmploymentStatuses.Active,from:-30,to:-1);

    [Fact]
    public async Task HTTP_future_leave_does_not_revoke_current_active_login()
    {
        using var factory=new B4IsolatedHrLoginFactory();
        using var client=factory.Client();
        await factory.SeedAsync(EmploymentStatuses.Active);
        await factory.AddStatusAsync(EmploymentStatuses.Leave,daysFromToday:1);
        using var response=await B4IsolatedHrLoginFactory.Login(client);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }

    [Fact]
    public async Task HTTP_live_HR_change_blocks_relogin_without_new_identity()
    {
        using var factory=new B4IsolatedHrLoginFactory();
        using var client=factory.Client();
        await factory.SeedAsync(EmploymentStatuses.Active);
        using(var first=await B4IsolatedHrLoginFactory.Login(client))
            Assert.Equal(HttpStatusCode.OK,first.StatusCode);
        await factory.AddStatusAsync(EmploymentStatuses.Leave);
        using var second=await B4IsolatedHrLoginFactory.Login(client);
        Assert.Equal(HttpStatusCode.Forbidden,second.StatusCode);
        Assert.DoesNotContain("accessToken",
            await second.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HTTP_invalid_demo_password_rejected_without_token()
    {
        using var factory=new B4IsolatedHrLoginFactory();
        using var client=factory.Client();
        await factory.SeedAsync(EmploymentStatuses.Active);
        using var response=await B4IsolatedHrLoginFactory.Login(client,
            password:"incorrect-local-password");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.DoesNotContain("accessToken",
            await response.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HTTP_unknown_account_rejected_without_token()
    {
        using var factory=new B4IsolatedHrLoginFactory();
        using var client=factory.Client();
        await factory.SeedAsync(EmploymentStatuses.Active);
        using var response=await B4IsolatedHrLoginFactory.Login(client,
            account:"UNKNOWN-TEST-IDENTITY");
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        Assert.DoesNotContain("accessToken",
            await response.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);
    }
}
