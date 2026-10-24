using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

/// <summary>Option C single request-store scope; does not grant approval.</summary>
public static class V180OptionCSubmissionScope
{
    public static void RequireOwnPublishedCustomer(
        CurrentUserDto actor, Location location,
        bool liveVisitor, bool visitorTeamValid, bool liveLeader)
    {
        if (actor.UserId <= 0 || !actor.OrganizationId.HasValue
            || location.OrganizationId != actor.OrganizationId
            || location.CreatedByUserId != actor.UserId
            || !location.TeamId.HasValue
            || !actor.TeamIds.Contains(location.TeamId.Value)
            || !string.Equals(location.LocationType, "Customer", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(location.ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase)
            || !location.IsActive
            || !(liveVisitor && visitorTeamValid || liveLeader))
            throw new UnauthorizedAccessException("OPTIONC_SUBMISSION_OWN_PUBLISHED_CUSTOMER_REQUIRED");
    }
}
