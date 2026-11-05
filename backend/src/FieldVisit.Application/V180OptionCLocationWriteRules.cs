using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

/// <summary>
/// Option C fail-closed guard for legacy direct Location write endpoints.
/// This does not approve a request; the canonical ChangeRequests engine is
/// the only future path for published/high-risk changes.
/// </summary>
public static class V180OptionCLocationWriteRules
{
    public static void EnsureUnpublishedDraft(Location location)
    {
        if (location.IsActive
            || !string.Equals(location.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("LOCATION_PUBLISHED_CHANGE_REQUIRES_UNIFIED_APPROVAL");
    }

    public static void EnsureDirectDraftUpdate(
        Location location, CurrentUserDto actor,
        int? targetTeamId, string? targetLocationType, bool requestedActive)
    {
        EnsureUnpublishedDraft(location);
        if (actor.Roles.Any(r => r.Equals("admin", StringComparison.OrdinalIgnoreCase)))
            return;

        if (actor.Roles.Any(r => r.Equals("leader", StringComparison.OrdinalIgnoreCase)))
        {
            if (location.OrganizationId != actor.OrganizationId
                || !location.TeamId.HasValue
                || !actor.TeamIds.Contains(location.TeamId.Value)
                || targetTeamId != location.TeamId
                || !string.Equals(location.LocationType, "Customer", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(targetLocationType, "Customer", StringComparison.OrdinalIgnoreCase)
                || requestedActive)
                throw new UnauthorizedAccessException("LOCATION_LEADER_DRAFT_SCOPE_DENIED");
        }
    }

    public static void EnsureLeaderDraftCreate(
        CurrentUserDto actor, int? targetTeamId, string? type, bool requestedActive)
    {
        if (!actor.Roles.Any(r => r.Equals("leader", StringComparison.OrdinalIgnoreCase))
            || actor.Roles.Any(r => r.Equals("admin", StringComparison.OrdinalIgnoreCase)))
            return;
        if (!targetTeamId.HasValue || !actor.TeamIds.Contains(targetTeamId.Value)
            || !string.Equals(type, "Customer", StringComparison.OrdinalIgnoreCase)
            || requestedActive)
            throw new UnauthorizedAccessException("LOCATION_LEADER_CREATE_SCOPE_DENIED");
    }
}
