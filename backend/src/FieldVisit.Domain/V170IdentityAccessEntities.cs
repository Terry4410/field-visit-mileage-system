namespace FieldVisit.Domain.Entities;

public static class UserTypes
{
    public const string Internal = "Internal";
    public const string External = "External";
}

public static class EmploymentStatuses
{
    public const string Active = "Active";
    public const string Leave = "Leave";
    public const string Terminated = "Terminated";
    public const string PreHire = "PreHire";
}

public static class DataScopeTypes
{
    public const string Organization = "Organization";
    public const string Team = "Team";
}

public static class CapabilityCodes
{
    public const string ExportExcel = "ExportExcel";
    public const string ExportPdf = "ExportPdf";
}

/// <summary>
/// v1.7 identity metadata is intentionally separated from the legacy Users
/// table. This protects the v1.6.1 login/trip compatibility path while the
/// identity model is migrated in phases.
/// </summary>
public sealed class UserIdentityProfile
{
    public int UserId { get; set; }
    public string UserType { get; set; } = UserTypes.Internal;
    public string UserCode { get; set; } = "";
    public string IdentityProvider { get; set; } = "Demo";

    public string? ExternalOrganization { get; set; }
    public string? ExternalTitle { get; set; }

    public DateOnly? AuthorizationFrom { get; set; }
    public DateOnly? AuthorizationTo { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Effective-dated HR facts.
/// HR data must never overwrite Role, Team Membership or Data Scope.
/// </summary>
public sealed class UserEmploymentPeriod
{
    public long UserEmploymentPeriodId { get; set; }
    public int UserId { get; set; }

    public string EmploymentStatus { get; set; } = EmploymentStatuses.Active;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    public string SourceType { get; set; } = "Excel";
    public string? SourceReference { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Effective-dated role source of truth.
/// Existing UserRoles remain as a current-state compatibility projection.
/// </summary>
public sealed class UserRoleAssignment
{
    public long UserRoleAssignmentId { get; set; }
    public int UserId { get; set; }
    public int RoleId { get; set; }

    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    public int? AssignedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Actual team membership. Membership and read-only Data Scope are different
/// concepts: an external supervisor must not become a Team Member merely
/// because the supervisor can view that team's data.
/// </summary>
public sealed class UserTeamAssignment
{
    public long UserTeamAssignmentId { get; set; }
    public int UserId { get; set; }
    public int TeamId { get; set; }

    public bool IsPrimary { get; set; }

    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    public int? AssignedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Read visibility scope, primarily for external supervisors.
/// This table never grants approval or mutation authority.
/// </summary>
public sealed class UserDataScope
{
    public long UserDataScopeId { get; set; }
    public int UserId { get; set; }

    public string ScopeType { get; set; } = DataScopeTypes.Team;

    public int? OrganizationId { get; set; }
    public int? TeamId { get; set; }

    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    public int? GrantedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Capabilities independent from Role. v1.7 initially uses this for external
/// supervisor Excel/PDF export authorization.
/// </summary>
public sealed class UserCapability
{
    public long UserCapabilityId { get; set; }
    public int UserId { get; set; }

    public string CapabilityCode { get; set; } = "";
    public bool IsAllowed { get; set; }

    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    public int? GrantedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
