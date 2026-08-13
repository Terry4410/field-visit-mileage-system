using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170ExternalSupervisorUpdateRulesTests
{
    private static readonly DateOnly Today =
        new(2026, 8, 13);

    [Fact]
    public void Retroactive_Change_Requires_Confirmation()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170ExternalSupervisorUpdateRules.Normalize(
                    ValidRequest(
                        changeEffectiveFrom:
                            new DateOnly(2026, 8, 12),
                        confirmRetroactive:
                            false),
                    Today));
    }

    [Fact]
    public void Retroactive_Change_Allows_Confirmation()
    {
        var result =
            V170ExternalSupervisorUpdateRules.Normalize(
                ValidRequest(
                    changeEffectiveFrom:
                        new DateOnly(2026, 8, 12),
                    confirmRetroactive:
                        true),
                Today);

        Assert.True(result.ConfirmRetroactive);
    }

    [Fact]
    public void ChangeEffectiveFrom_Must_Be_Inside_Authorization()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170ExternalSupervisorUpdateRules.Normalize(
                    ValidRequest(
                        authorizationFrom:
                            new DateOnly(2026, 9, 1),
                        changeEffectiveFrom:
                            new DateOnly(2026, 8, 20)),
                    Today));
    }

    [Fact]
    public void Update_Normalizes_Email_And_Scope()
    {
        var result =
            V170ExternalSupervisorUpdateRules.Normalize(
                new UpdateExternalSupervisorRequest(
                    " 王督導 ",
                    " Supervisor@Example.COM ",
                    " 主管機關 ",
                    " 督導 ",
                    new DateOnly(2026, 8, 13),
                    new DateOnly(2026, 12, 31),
                    "organization",
                    Array.Empty<int>(),
                    true,
                    false,
                    true,
                    new DateOnly(2026, 8, 13),
                    false),
                Today);

        Assert.Equal(
            "supervisor@example.com",
            result.Email);

        Assert.Equal(
            "Organization",
            result.ScopeType);

        Assert.Equal(
            "王督導",
            result.DisplayName);
    }

    private static UpdateExternalSupervisorRequest
        ValidRequest(
            DateOnly? authorizationFrom = null,
            DateOnly? changeEffectiveFrom = null,
            bool confirmRetroactive = false)
        => new(
            "王督導",
            "supervisor@example.com",
            "主管機關",
            "督導",
            authorizationFrom
                ?? new DateOnly(2026, 8, 1),
            new DateOnly(2026, 12, 31),
            "Organization",
            Array.Empty<int>(),
            false,
            false,
            true,
            changeEffectiveFrom
                ?? new DateOnly(2026, 8, 13),
            confirmRetroactive);
}
