using FieldVisit.Application;
using Xunit;
namespace FieldVisit.Application.Tests;
public sealed class V180B1CLocationGuardTests
{
    [Fact] public void Revoked_role_does_not_survive_valid_token()
    {
        var result=V180LocationLiveRoleRules.Evaluate(new[]{"leader"},
            new[]{"visitor"},new[]{"leader","visitor"});
        Assert.False(result.Leader);
        Assert.False(result.Visitor);
    }
    [Fact] public void Projection_and_active_role_must_both_match()
    {
        Assert.False(V180LocationLiveRoleRules.Evaluate(
            new[]{"visitor"},new[]{"visitor"},Array.Empty<string>()).Visitor);
        Assert.True(V180LocationLiveRoleRules.Evaluate(
            new[]{"visitor"},new[]{"visitor","leader"},new[]{"visitor","leader"}).Visitor);
        Assert.False(V180LocationLiveRoleRules.Evaluate(
            new[]{"visitor"},new[]{"visitor","leader"},new[]{"visitor","leader"}).Leader);
    }
    [Fact] public void Note_only_update_does_not_reset_geocoding()
    {
        Assert.False(V180LocationMaterialChangeRules.RequiresGeocoding(
            "台北市忠孝東路1號","ABC"," 台北市忠孝東路1號 ","ABC"));
        Assert.False(V180LocationMaterialChangeRules.RequiresDuplicateRecheck(
            "Company","Address","ABC","0012",
            "Company","Address","ABC","0012"));
    }
    [Fact] public void Address_change_resets_geocode_and_identity_change_rechecks()
    {
        Assert.True(V180LocationMaterialChangeRules.RequiresGeocoding("A",null,"B",null));
        Assert.True(V180LocationMaterialChangeRules.RequiresGeocoding(null,"A",null,"B"));
        Assert.True(V180LocationMaterialChangeRules.RequiresDuplicateRecheck(
            "A","Address",null,"1","B","Address",null,"1"));
        Assert.True(V180LocationMaterialChangeRules.RequiresDuplicateRecheck(
            "A","Address",null,"1","A","Address",null,"2"));
        Assert.False(V180LocationMaterialChangeRules.RequiresGeocoding(
            "Address",null,"Address",null));
    }
}
