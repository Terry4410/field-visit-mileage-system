namespace FieldVisit.Application;

/// <summary>
/// Pure rules for authorizing direct single-trip reads by a Supervisor.
/// The authoritative read scope is still resolved by IV170AccessControl.
/// </summary>
public static class V170TripAccessRules
{
    public static bool CanSupervisorReadTrip(
        CurrentUserDto user,
        int tripOrganizationId,
        int? tripTeamId,
        V170ReadScope readScope)
    {
        var isSupervisor =
            user.Roles.Any(
                x => x.Equals(
                    "supervisor",
                    StringComparison.OrdinalIgnoreCase));

        if (!isSupervisor)
            return false;

        if (!user.OrganizationId.HasValue
            || user.OrganizationId.Value != tripOrganizationId)
        {
            return false;
        }

        if (readScope.OrganizationWide)
            return true;

        return tripTeamId.HasValue
            && readScope.TeamIds.Contains(tripTeamId.Value);
    }
}
