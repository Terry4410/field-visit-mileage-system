using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180LocationAdminMutationRulesTests
{
    private static CurrentUserDto Actor(string[] roles,int? org=1) =>
        new(11,"admin11","Admin",null,org,7,"Team",
            roles,new[]{new TeamScopeDto(7,"Team",true)});

    [Fact]
    public void Active_admin_requires_all_three_role_sources()
    {
        V180LocationAdminMutationRules.RequireCurrentAdmin(
            Actor(new[]{"admin"}),new[]{"admin"},new[]{"admin"});
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180LocationAdminMutationRules.RequireCurrentAdmin(
                Actor(new[]{"admin"}),new[]{"visitor"},new[]{"admin"}));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180LocationAdminMutationRules.RequireCurrentAdmin(
                Actor(new[]{"admin"}),new[]{"admin"},new[]{"visitor"}));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180LocationAdminMutationRules.RequireCurrentAdmin(
                Actor(new[]{"leader"}),new[]{"admin"},new[]{"admin"}));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180LocationAdminMutationRules.RequireCurrentAdmin(
                Actor(new[]{"admin"},null),new[]{"admin"},new[]{"admin"}));
    }

    [Fact]
    public void Dual_role_does_not_restore_revoked_admin()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180LocationAdminMutationRules.RequireCurrentAdmin(
                Actor(new[]{"visitor","admin"}),new[]{"visitor"},new[]{"visitor","admin"}));
    }

    [Fact]
    public void Location_org_must_be_exact_and_non_null_for_admin_mutations()
    {
        V180LocationAdminMutationRules.RequireScopedLocation(Actor(new[]{"admin"}),1);
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180LocationAdminMutationRules.RequireScopedLocation(Actor(new[]{"admin"}),null));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180LocationAdminMutationRules.RequireScopedLocation(Actor(new[]{"admin"}),2));
        Assert.Throws<UnauthorizedAccessException>(() =>
            V180LocationAdminMutationRules.RequireScopedLocation(Actor(new[]{"admin"},null),1));
    }
}
