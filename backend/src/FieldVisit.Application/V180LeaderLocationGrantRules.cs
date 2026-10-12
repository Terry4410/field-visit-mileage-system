namespace FieldVisit.Application;

/// <summary>
/// Immutable input to the VAL-B1-002 decision.  It deliberately contains no
/// JWT, UI scope, or legacy assignment inference: those values may be stale
/// or may have been created by the 1800_002 compatibility backfill.
/// </summary>
public sealed record V180LeaderLocationGrantEvidence(
    bool CurrentHrStatusAllowsAccess,
    bool CurrentLeaderRole,
    bool SameOrganization,
    bool CurrentTeamMembership,
    bool CurrentDirectAppointment,
    bool CurrentDelegationFromCurrentAppointment,
    bool IndependentlyAttested);

/// <summary>
/// VAL-B1-002 is a conjunctive server-side rule.  A leader role, TeamIds,
/// historic membership, or an inferred TeamLeaderAssignment never creates a
/// location-management grant.  The caller must obtain the booleans from a
/// fresh authoritative read and retain the independent attestation evidence.
/// </summary>
public static class V180LeaderLocationGrantRules
{
    public static bool AllowsManagedTeamLocationWrite(
        V180LeaderLocationGrantEvidence evidence) =>
        evidence.CurrentHrStatusAllowsAccess
        && evidence.CurrentLeaderRole
        && evidence.SameOrganization
        && evidence.CurrentTeamMembership
        && (evidence.CurrentDirectAppointment
            || evidence.CurrentDelegationFromCurrentAppointment)
        && evidence.IndependentlyAttested;
}
