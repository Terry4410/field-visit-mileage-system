namespace FieldVisit.Application;

/// <summary>
/// A public/shared Location does not grant read access to arbitrary team
/// notes or their audit trail. This guard validates the caller-selected
/// team before the maintenance query uses it.
/// </summary>
public static class V180LocationMaintenanceReadRules
{
    public static void RequireAllowedTeam(CurrentUserDto user,int? requestedTeamId,bool admin)
    {
        if(!user.OrganizationId.HasValue)
            throw new UnauthorizedAccessException("LOCATION_MAINTENANCE_ORG_REQUIRED");
        if(requestedTeamId.HasValue && requestedTeamId.Value<=0)
            throw new UnauthorizedAccessException("LOCATION_MAINTENANCE_TEAM_INVALID");
        if(!admin && requestedTeamId.HasValue
            && !user.TeamIds.Contains(requestedTeamId.Value))
            throw new UnauthorizedAccessException("LOCATION_MAINTENANCE_TEAM_SCOPE_DENIED");
    }
}
