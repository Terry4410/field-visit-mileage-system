using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180OptionCHttpCandidateContractTests
{
    [Fact]
    public async Task Candidate_route_is_real_http_and_fails_closed_when_flag_is_off()
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
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("OPTIONC_CANDIDATE_DISABLED", await response.Content.ReadAsStringAsync());
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
