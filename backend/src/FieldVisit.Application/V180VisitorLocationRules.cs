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

    public static void EnsureCurrentAssignment(bool currentRole, string? employmentStatus, IReadOnlyCollection<int> teams, int? organizationId)
    {
        if (!currentRole) throw new UnauthorizedAccessException("VISITOR_ROLE_NOT_CURRENT");
        if (!string.Equals(employmentStatus, "Active", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("VISITOR_HR_STATUS_NOT_ACTIVE");
        if (organizationId is null) throw new UnauthorizedAccessException("VISITOR_ORGANIZATION_REQUIRED");
        if (teams.Count == 0) throw new UnauthorizedAccessException("VISITOR_TEAM_ASSIGNMENT_NOT_CURRENT");
    }

    public static void EnsureRequiredRowVersion(byte[] currentValue, string? expectedBase64)
    {
        if (string.IsNullOrWhiteSpace(expectedBase64))
            throw new InvalidOperationException("ROWVERSION_CONFLICT: Missing RowVersion.");
        byte[] expected;
        try { expected = Convert.FromBase64String(expectedBase64); }
        catch (FormatException) { throw new InvalidOperationException("ROWVERSION_CONFLICT: Invalid RowVersion."); }
        if (expected.Length == 0 || !currentValue.SequenceEqual(expected))
            throw new InvalidOperationException("ROWVERSION_CONFLICT: Stale RowVersion.");
    }

    public static bool IsVisitor(CurrentUserDto user) =>
        user.Roles.Any(x => x.Equals("visitor", StringComparison.OrdinalIgnoreCase));
}
