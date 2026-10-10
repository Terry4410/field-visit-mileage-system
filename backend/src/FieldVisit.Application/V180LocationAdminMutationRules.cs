namespace FieldVisit.Application;

/// <summary>
/// Existing explicitly Admin-only Location operations require live HR status
/// (checked in repository) plus token/effective-date/projection role intersection.
/// A readable global master is never writable via organization-specific maintenance.
/// This policy does not authorize B3 approval or any self-approval.
/// </summary>
public static class V180LocationAdminMutationRules
{
    public static void RequireCurrentAdmin(CurrentUserDto actor,
        IEnumerable<string> datedRoleCodes,IEnumerable<string> projectedRoleCodes)
    {
        if (!actor.OrganizationId.HasValue ||
            !V180LocationLiveRoleRules.Evaluate(
                actor.Roles,datedRoleCodes,projectedRoleCodes).Admin)
            throw new UnauthorizedAccessException(
                "B2_ADMIN_CURRENT_ROLE_DENIED: 管理者角色已失效或不完整。");
    }

    public static void RequireScopedLocation(CurrentUserDto actor,int? locationOrganizationId)
    {
        if(!actor.OrganizationId.HasValue || !locationOrganizationId.HasValue
           ||actor.OrganizationId.Value!=locationOrganizationId.Value)
            throw new UnauthorizedAccessException(
                "B2_ADMIN_LOCATION_ORGANIZATION_DENIED: 全域或其他組織主檔必須走獨立治理流程。");
    }
}
