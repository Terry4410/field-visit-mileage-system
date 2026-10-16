namespace FieldVisit.Application;

/// <summary>Mutations require intersection of JWT active role, effective-dated
/// assignment and current role projection; none grants write scope alone.</summary>
public static class V180LocationLiveRoleRules
{
    public sealed record EffectiveRoles(bool Admin,bool Leader,bool Visitor);
    public static EffectiveRoles Evaluate(IEnumerable<string> tokenRoles,
        IEnumerable<string> assignedRoles,IEnumerable<string> projectedRoles)
    {
        var allowed=new HashSet<string>(tokenRoles,StringComparer.OrdinalIgnoreCase);
        allowed.IntersectWith(assignedRoles);
        allowed.IntersectWith(projectedRoles);
        return new EffectiveRoles(allowed.Contains("admin"),
            allowed.Contains("leader"),allowed.Contains("visitor"));
    }
}
