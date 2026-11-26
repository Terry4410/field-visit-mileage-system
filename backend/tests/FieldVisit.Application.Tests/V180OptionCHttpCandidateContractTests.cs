using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OptionCHttpCandidateContractTests
{
    [Fact]
    public async Task Candidate_route_rejects_signed_token_without_live_role()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("OptionC:Candidate:Enabled", "false");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=invalid;Database=invalid");
            builder.UseSetting("Auth:JwtKey", "test-key-that-is-at-least-32-characters-long");
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token());
        using var response = await client.GetAsync("api/v1/optionc-candidate/requests/mine");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Candidate_flag_fails_closed_before_database_access()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["OptionC:Candidate:Enabled"] = "false" }).Build();
        var service = new FieldVisit.Infrastructure.V180OptionCCandidateService(null!, null!, configuration);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.OwnAsync(CancellationToken.None));
        Assert.Equal("OPTIONC_CANDIDATE_DISABLED", error.Message);
    }

    [Fact]
    public async Task Candidate_route_requires_authentication()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("api/v1/optionc-candidate/requests/mine");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string Token()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-key-that-is-at-least-32-characters-long"));
        var token = new JwtSecurityToken("FieldVisit.UAT", "FieldVisit.UAT.Users",
            new[] { new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Name, "Candidate Test") },
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
