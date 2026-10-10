using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FieldVisit.Api.Controllers;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Xunit;

namespace FieldVisit.Application.Tests;

/// <summary>
/// Real ASP.NET Core HTTP routing + JWT authentication + role authorization
/// with B3 OFF. No SQL Server, Azure, UAT, existing database, feature enable,
/// approval execution or migration is allowed by this test harness.
/// </summary>
public sealed class B3DisabledHttpFactory : WebApplicationFactory<V180B3ChangeRequestsController>
{
    internal const string Issuer="B3-FIXTURE-LOCAL-ONLY";
    internal const string Audience="B3-FIXTURE-TEST-CLIENT";
    internal const string TestKey="B3_HTTP_ISOLATED_TEST_ONLY_a3b5c7d9e0f2_";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("B3IsolatedTests");
        builder.ConfigureAppConfiguration((_,cfg)=>cfg.AddInMemoryCollection(
            new Dictionary<string,string?>
            {
                ["Auth:Mode"]="Demo",
                ["Auth:Issuer"]=Issuer,
                ["Auth:Audience"]=Audience,
                ["Auth:JwtKey"]=TestKey,
                ["PackageB:B3:Enabled"]="false",
                ["ConnectionStrings:DefaultConnection"]="Server=127.0.0.1,14336;Database=UnreachableB3Off;User ID=unused;Password=unused;Connect Timeout=1;Encrypt=False",
                ["BackgroundJobs:WorkerEnabled"]="false",
                ["BackgroundJobs:RecoverOnStartup"]="false",
                ["Swagger:Enabled"]="false"
            }));
        builder.ConfigureTestServices(services=>
        {
            // For minimal-hosting integration tests, Program registers its
            // JwtBearer validation parameters before test-only app config.
            // PostConfigure ONLY this in-memory TestServer's AppJwt options.
            // Do not change authentication middleware or relax validation.
            services.PostConfigure<JwtBearerOptions>("AppJwt",options=>
            {
                options.TokenValidationParameters.ValidIssuer=Issuer;
                options.TokenValidationParameters.ValidAudience=Audience;
                options.TokenValidationParameters.IssuerSigningKey=
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey));
            });
            // Background processing must never access SQL during HTTP-OFF tests.
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
    public string Token(string role,bool expired=false,bool wrongIssuer=false,
        bool wrongAudience=false,bool notYetValid=false,bool invalidSignature=false)
    {
        // Bind test JWT to the *actual* app authentication options, not
        // a guessed appsettings snapshot; assert that test config overrides
        // really reached the host before it issues any token.
        var cfg=Services.GetRequiredService<IConfiguration>();
        if(cfg["Auth:Issuer"]!=Issuer || cfg["Auth:Audience"]!=Audience
            || cfg["Auth:JwtKey"]!=TestKey)
            throw new InvalidOperationException(
                $"B3 HTTP fixture configuration mismatch: issuer={cfg["Auth:Issuer"]}, audience={cfg["Auth:Audience"]}, key-is-fixture={cfg["Auth:JwtKey"]==TestKey}");
        var scheme=Services.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetDefaultAuthenticateSchemeAsync().GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("No default app JWT scheme");
        var validation=Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(scheme.Name).TokenValidationParameters;
        var matchesSigningKey=validation.IssuerSigningKey is SymmetricSecurityKey key &&
            Encoding.UTF8.GetBytes(TestKey).SequenceEqual(key.Key);
        if(validation.ValidIssuer!=Issuer || validation.ValidAudience!=Audience ||
           !matchesSigningKey)
            throw new InvalidOperationException(
                $"B3 JWT middleware configuration mismatch: scheme={scheme.Name}, issuer={validation.ValidIssuer}, audience={validation.ValidAudience}, key-is-fixture={matchesSigningKey}");
        var claims=new[]{
            new Claim(ClaimTypes.NameIdentifier,"98765"),
            new Claim(ClaimTypes.Name,"B3 isolated fixture"),
            new Claim(ClaimTypes.Role,role)
        };
        var jwt=new JwtSecurityToken(
            issuer:wrongIssuer?"WRONG-ISSUER":Issuer,
            audience:wrongAudience?"WRONG-AUDIENCE":Audience,claims:claims,
            notBefore:notYetValid?DateTime.UtcNow.AddMinutes(12):DateTime.UtcNow.AddMinutes(-15),
            expires:expired?DateTime.UtcNow.AddMinutes(-5)
                :notYetValid?DateTime.UtcNow.AddMinutes(24):DateTime.UtcNow.AddMinutes(10),
            signingCredentials:new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                    invalidSignature?"WRONG_SIGNING_KEY_B3_HTTP_ISOLATED_98765":TestKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}

public sealed class V180B3OffHttpPipelineTests(B3DisabledHttpFactory factory)
    : IClassFixture<B3DisabledHttpFactory>
{
    private static readonly Guid RequestId=Guid.Parse("cfc34819-1a4d-4c16-8287-cb02823ab3a1");
    private static async Task<HttpResponseMessage> Send(HttpClient client,string endpoint)
    {
        var version=Convert.ToBase64String(new byte[8]);
        var review=new V180B3Review(version,Guid.NewGuid(),"fixture rejection");
        return endpoint switch
        {
            "Submit"=>await client.PostAsJsonAsync("/api/v1/change-requests/locations",
                new V180B3SubmitLocation(7,version,"fixture",new V180B3LocationFields(
                    "Test",null,null,"Test address",null,null,null))),
            "Mine"=>await client.GetAsync("/api/v1/change-requests/mine"),
            "Pending"=>await client.GetAsync("/api/v1/change-requests/admin/pending"),
            "Reject"=>await client.PostAsJsonAsync(
                $"/api/v1/change-requests/admin/{RequestId}/reject",review),
            "Approve"=>await client.PostAsJsonAsync(
                $"/api/v1/change-requests/admin/{RequestId}/approve",review),
            _=>throw new ArgumentOutOfRangeException(nameof(endpoint))
        };
    }
    private HttpClient Authorized(string role)
    {
        var client=factory.Client();
        client.DefaultRequestHeaders.Authorization=
            new AuthenticationHeaderValue("Bearer",factory.Token(role));
        return client;
    }

    [Theory]
    [InlineData("Submit")]
    [InlineData("Mine")]
    [InlineData("Pending")]
    [InlineData("Reject")]
    [InlineData("Approve")]
    public async Task HTTP_unauthenticated_B3_endpoints_return_401(string endpoint)
    {
        using var client=factory.Client();
        using var response=await Send(client,endpoint);
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }

    [Theory]
    [InlineData("Submit","admin")]
    [InlineData("Mine","auditor")]
    [InlineData("Pending","visitor")]
    [InlineData("Reject","leader")]
    [InlineData("Approve","visitor")]
    public async Task HTTP_authenticated_wrong_role_returns_403_before_B3_disabled(string endpoint,string role)
    {
        using var client=Authorized(role);
        using var response=await Send(client,endpoint);
        Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }

    [Theory]
    [InlineData("Submit","visitor")]
    [InlineData("Submit","leader")]
    [InlineData("Mine","visitor")]
    [InlineData("Mine","admin")]
    [InlineData("Pending","admin")]
    [InlineData("Reject","admin")]
    [InlineData("Approve","admin")]
    public async Task HTTP_authorized_role_receives_503_B3_DISABLED_no_database(string endpoint,string role)
    {
        using var client=Authorized(role);
        using var response=await Send(client,endpoint);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
        var content=await response.Content.ReadAsStringAsync();
        Assert.Contains("B3_DISABLED",content,StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public async Task HTTP_expired_or_wrong_issuer_JWT_cannot_read_B3_queue(bool expired,bool wrongIssuer)
    {
        using var client=factory.Client();
        client.DefaultRequestHeaders.Authorization=
            new AuthenticationHeaderValue("Bearer",factory.Token("admin",expired,wrongIssuer));
        using var response=await Send(client,"Pending");
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }

    [Theory]
    [InlineData("wrong-audience")]
    [InlineData("wrong-signature")]
    [InlineData("not-yet-valid")]
    [InlineData("malformed")]
    public async Task HTTP_bad_JWT_variant_rejected_with_401_before_disabled_gate(string variant)
    {
        using var client=factory.Client();
        var token=variant switch
        {
            "wrong-audience"=>factory.Token("admin",wrongAudience:true),
            "wrong-signature"=>factory.Token("admin",invalidSignature:true),
            "not-yet-valid"=>factory.Token("admin",notYetValid:true),
            "malformed"=>"not-a-valid-jwt-token",
            _=>throw new ArgumentOutOfRangeException(nameof(variant))
        };
        client.DefaultRequestHeaders.Authorization=
            new AuthenticationHeaderValue("Bearer",token);
        using var response=await Send(client,"Pending");
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }

    [Theory]
    [InlineData("locations")]
    [InlineData("mine")]
    [InlineData("admin/pending")]
    [InlineData("admin/reject")]
    [InlineData("admin/approve")]
    public async Task HTTP_wrong_method_cannot_bypass_endpoint_role_or_feature_gate(string action)
    {
        var path=action switch
        {
            "locations"=>"/api/v1/change-requests/locations",
            "mine"=>"/api/v1/change-requests/mine",
            "admin/pending"=>"/api/v1/change-requests/admin/pending",
            "admin/reject"=>$"/api/v1/change-requests/admin/{RequestId}/reject",
            "admin/approve"=>$"/api/v1/change-requests/admin/{RequestId}/approve",
            _=>throw new ArgumentOutOfRangeException(nameof(action))
        };
        using var client=factory.Client();
        var method=action is "locations" or "admin/reject" or "admin/approve"
            ?HttpMethod.Get:HttpMethod.Post;
        using var request=new HttpRequestMessage(method,path);
        using var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,response.StatusCode);
    }

}
