using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170AuthenticationRulesTests
{
    [Fact]
    public void Empty_Mode_Defaults_To_Demo()
    {
        Assert.Equal(
            AuthenticationModes.Demo,
            V170AuthenticationRules.NormalizeMode(null));
    }

    [Fact]
    public void Mode_Is_Normalized_Case_Insensitively()
    {
        Assert.Equal(
            AuthenticationModes.Demo,
            V170AuthenticationRules.NormalizeMode(" demo "));

        Assert.Equal(
            AuthenticationModes.Entra,
            V170AuthenticationRules.NormalizeMode("ENTRA"));
    }

    [Fact]
    public void Unknown_Mode_Is_Rejected()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170AuthenticationRules.NormalizeMode(
                    "Hybrid"));
    }

    [Fact]
    public void Wrong_Login_Mode_Is_Rejected()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () =>
                V170AuthenticationRules.EnsureMode(
                    AuthenticationModes.Entra,
                    AuthenticationModes.Demo));
    }

    [Fact]
    public void Required_Scope_Must_Be_Exact()
    {
        Assert.True(
            V170AuthenticationRules.HasRequiredScope(
                "openid profile access_as_user",
                "access_as_user"));
    }

    [Fact]
    public void Scope_Substring_Is_Not_Accepted()
    {
        Assert.False(
            V170AuthenticationRules.HasRequiredScope(
                "access_as_user_extra",
                "access_as_user"));
    }

    [Fact]
    public void Guid_Claims_Are_Validated()
    {
        var id =
            Guid.NewGuid();

        Assert.Equal(
            id,
            V170AuthenticationRules.ParseRequiredGuidClaim(
                id.ToString(),
                "oid"));

        Assert.Throws<UnauthorizedAccessException>(
            () =>
                V170AuthenticationRules
                    .ParseRequiredGuidClaim(
                        "not-a-guid",
                        "oid"));
    }

    [Fact]
    public void Email_Is_Normalized()
    {
        Assert.Equal(
            "user@example.com",
            V170AuthenticationRules.NormalizeEmail(
                " User@Example.COM "));

        Assert.Null(
            V170AuthenticationRules.NormalizeEmail(" "));
    }
}
