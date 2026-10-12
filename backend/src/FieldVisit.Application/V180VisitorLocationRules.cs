using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

/// <summary>
/// Visitor-only location policy.  This policy intentionally uses only the
/// Location columns that exist in the verified 1.8.0-010 schema.
/// </summary>
public static class V180VisitorLocationRules
{
    public static void EnsureCreate(
        CurrentUserDto user,
        int? teamId,
        string locationType,
        bool isActive,
        IReadOnlyCollection<int> effectiveTeams)
    {
        if (!IsVisitor(user)) throw new UnauthorizedAccessException("VISITOR_ROLE_REQUIRED");
        if (!teamId.HasValue || !effectiveTeams.Contains(teamId.Value))
            throw new UnauthorizedAccessException("VISITOR_TEAM_SCOPE_REQUIRED");
        if (!string.Equals(locationType?.Trim(), "Customer", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("VISITOR_CUSTOMER_LOCATION_ONLY");
        if (isActive) throw new UnauthorizedAccessException("VISITOR_DRAFT_MUST_BE_INACTIVE");
    }

    public static void EnsureUpdate(
        CurrentUserDto user,
        Location row,
        int? requestedTeamId,
        string locationType,
        bool isActive,
        IReadOnlyCollection<int> effectiveTeams)
    {
        if (!IsVisitor(user)) throw new UnauthorizedAccessException("VISITOR_ROLE_REQUIRED");
        if (row.CreatedByUserId != user.UserId)
            throw new UnauthorizedAccessException("VISITOR_LOCATION_OWNER_REQUIRED");
        if (row.ApprovalStatus != "Pending" || row.IsActive)
            throw new UnauthorizedAccessException("VISITOR_PENDING_INACTIVE_DRAFT_ONLY");
        if (!string.Equals(row.LocationType, "Customer", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(locationType?.Trim(), "Customer", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("VISITOR_CUSTOMER_LOCATION_ONLY");
        if (!requestedTeamId.HasValue || requestedTeamId != row.TeamId || !effectiveTeams.Contains(requestedTeamId.Value))
            throw new UnauthorizedAccessException("VISITOR_TEAM_TRANSFER_FORBIDDEN");
        if (isActive) throw new UnauthorizedAccessException("VISITOR_DRAFT_MUST_BE_INACTIVE");
    }

    public static bool IsVisitor(CurrentUserDto user) =>
        user.Roles.Any(x => x.Equals("visitor", StringComparison.OrdinalIgnoreCase));
}
