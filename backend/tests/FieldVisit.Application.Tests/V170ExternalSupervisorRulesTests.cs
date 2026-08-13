using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170ExternalSupervisorRulesTests
{
    [Fact]
    public void Organization_Scope_Cannot_Have_Teams()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170ExternalSupervisorRules.Normalize(
                    new SaveExternalSupervisorRequest(
                        "王督導",
                        "supervisor@example.com",
                        "主管機關",
                        null,
                        new DateOnly(2026, 8, 13),
                        new DateOnly(2026, 12, 31),
                        "Organization",
                        new[] { 1 },
                        false,
                        false)));
    }

    [Fact]
    public void Team_Scope_Requires_At_Least_One_Team()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170ExternalSupervisorRules.Normalize(
                    new SaveExternalSupervisorRequest(
                        "王督導",
                        "supervisor@example.com",
                        "主管機關",
                        null,
                        new DateOnly(2026, 8, 13),
                        new DateOnly(2026, 12, 31),
                        "Team",
                        Array.Empty<int>(),
                        false,
                        false)));
    }

    [Fact]
    public void Authorization_End_Cannot_Precede_Start()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170ExternalSupervisorRules.Normalize(
                    new SaveExternalSupervisorRequest(
                        "王督導",
                        "supervisor@example.com",
                        "主管機關",
                        null,
                        new DateOnly(2026, 8, 13),
                        new DateOnly(2026, 8, 12),
                        "Organization",
                        Array.Empty<int>(),
                        false,
                        false)));
    }

    [Fact]
    public void Normalize_Trims_And_Normalizes_Email()
    {
        var result =
            V170ExternalSupervisorRules.Normalize(
                new SaveExternalSupervisorRequest(
                    " 王督導 ",
                    " Supervisor@Example.COM ",
                    " 主管機關 ",
                    " 督導 ",
                    new DateOnly(2026, 8, 13),
                    new DateOnly(2026, 12, 31),
                    "organization",
                    Array.Empty<int>(),
                    true,
                    false));

        Assert.Equal("王督導", result.DisplayName);
        Assert.Equal(
            "supervisor@example.com",
            result.Email);
        Assert.Equal(
            "主管機關",
            result.ExternalOrganization);
        Assert.Equal(
            "Organization",
            result.ScopeType);
    }
}
