namespace FieldVisit.Application;

/// <summary>
/// B3 may stage a visitor-owned proposal only while the team itself belongs
/// to the same organization, is enabled and is effective on the business date.
/// This is an additional deny gate, not a management appointment.
/// </summary>
public static class V180B3LocationTeamRules
{
    public static bool IsEffectiveForOrganization(
        int? actorOrganizationId, int? teamOrganizationId, bool teamIsActive,
        DateOnly? effectiveFrom, DateOnly? effectiveTo, DateOnly today) =>
        actorOrganizationId.HasValue &&
        teamOrganizationId.HasValue &&
        actorOrganizationId == teamOrganizationId &&
        teamIsActive &&
        (!effectiveFrom.HasValue || effectiveFrom.Value <= today) &&
        (!effectiveTo.HasValue || effectiveTo.Value >= today);
}
