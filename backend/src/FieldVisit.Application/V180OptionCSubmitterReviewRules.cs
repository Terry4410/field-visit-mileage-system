namespace FieldVisit.Application;

/// <summary>
/// Fail-closed Option C independent-review submitter revalidation.
/// All booleans MUST be sourced from fresh server-side HR, role, organization,
/// team and appointment reads inside the reviewer transaction.
/// </summary>
public static class V180OptionCSubmitterReviewRules
{
    public static bool IsEligible(
        bool liveHr, bool sameOrganization, bool teamEffective,
        bool teamAssigned, bool liveVisitorRole, bool visitorTeamMembership,
        bool liveLeaderRole, bool leaderAppointment)
        => liveHr && sameOrganization && teamEffective && teamAssigned
           && (liveVisitorRole && visitorTeamMembership
               || liveLeaderRole && leaderAppointment);
}
