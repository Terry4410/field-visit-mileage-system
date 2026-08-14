using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170IdentityBindingRulesTests
{
    [Fact]
    public void External_create_defaults_to_demo()
    {
        var request =
            V170ExternalSupervisorRules.Normalize(
                new SaveExternalSupervisorRequest(
                    "外部督導",
                    "supervisor@example.com",
                    "政府機關",
                    "督導",
                    new DateOnly(2026, 8, 14),
                    new DateOnly(2026, 12, 31),
                    "Organization",
                    [],
                    false,
                    false));

        Assert.Equal(
            "Demo",
            request.IdentityProvider);

        Assert.Null(
            request.EntraTenantId);

        Assert.Null(
            request.EntraObjectId);
    }

    [Fact]
    public void External_update_without_identity_keeps_identity_unspecified()
    {
        var request =
            V170ExternalSupervisorUpdateRules.Normalize(
                new UpdateExternalSupervisorRequest(
                    "外部督導",
                    "supervisor@example.com",
                    "政府機關",
                    "督導",
                    new DateOnly(2026, 8, 14),
                    new DateOnly(2026, 12, 31),
                    "Organization",
                    [],
                    false,
                    false,
                    true,
                    new DateOnly(2026, 8, 14)),
                new DateOnly(2026, 8, 14));

        Assert.Null(
            request.IdentityProvider);

        Assert.Null(
            request.EntraTenantId);

        Assert.Null(
            request.EntraObjectId);
    }

    [Fact]
    public void Internal_update_without_identity_keeps_identity_unspecified()
    {
        var request =
            V170InternalUserAccessRules.Normalize(
                new UpdateInternalUserAccessRequest(
                    ["admin"],
                    [],
                    true,
                    new DateOnly(2026, 8, 14)),
                new DateOnly(2026, 8, 14));

        Assert.Null(
            request.IdentityProvider);

        Assert.Null(
            request.EntraTenantId);

        Assert.Null(
            request.EntraObjectId);
    }

    [Fact]
    public void Entra_requires_both_tenant_and_object_id()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170IdentityBindingRules.Normalize(
                    "EntraId",
                    Guid.Parse(
                        "11111111-1111-1111-1111-111111111111"),
                    null,
                    defaultToDemo: false));
    }

    [Fact]
    public void Entra_alias_is_normalized_to_entraid()
    {
        var result =
            V170IdentityBindingRules.Normalize(
                "entra",
                Guid.Parse(
                    "11111111-1111-1111-1111-111111111111"),
                Guid.Parse(
                    "22222222-2222-2222-2222-222222222222"),
                defaultToDemo: false);

        Assert.True(
            result.IsSpecified);

        Assert.Equal(
            "EntraId",
            result.IdentityProvider);
    }
}
