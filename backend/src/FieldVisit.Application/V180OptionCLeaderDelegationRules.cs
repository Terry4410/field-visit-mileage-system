namespace FieldVisit.Application;

/// <summary>
/// Pure Option C delegated-leader preflight. The database query remains the
/// authority for appointments; this policy requires every independent gate.
/// </summary>
public static class V180OptionCLeaderDelegationRules
{
    public static bool IsActive(DateOnly from, DateOnly to, DateOnly today)
        => from <= today && today <= to;

    public static bool IsAuthorized(
        bool liveHrAndRole, bool tokenTeamScope, bool teamActive,
        bool effectiveTeamAssignment, bool effectiveAppointment,
        bool sameOrganization)
        => liveHrAndRole && tokenTeamScope && teamActive
           && effectiveTeamAssignment && effectiveAppointment && sameOrganization;
}
