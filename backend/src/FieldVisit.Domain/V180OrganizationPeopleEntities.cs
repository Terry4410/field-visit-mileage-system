namespace FieldVisit.Domain.Entities;

public sealed class Center
{
    public int CenterId { get; set; }
    public int OrganizationId { get; set; }
    public string CenterCode { get; set; } = "";
    public string CenterName { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public DateTime? InactivatedAt { get; set; }
    public int? InactivatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<DeploymentSite> DeploymentSites { get; set; } = [];
}

public sealed class TeamCenterAssignment
{
    public long TeamCenterAssignmentId { get; set; }
    public int TeamId { get; set; }
    public int CenterId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? ChangeReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class Person
{
    public long PersonId { get; set; }
    public string DisplayName { get; set; } = "";
    public int? LegacyUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class Employment
{
    public long EmploymentId { get; set; }
    public long PersonId { get; set; }
    public int OrganizationId { get; set; }
    public string? EmployeeNo { get; set; }
    public string? Email { get; set; }
    public DateOnly? HireDate { get; set; }
    public DateOnly? TerminationDate { get; set; }
    public int? LegacyUserId { get; set; }
    public string SourceType { get; set; } = "";
    public string? SourceReference { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class EmploymentStatusPeriod
{
    public long EmploymentStatusPeriodId { get; set; }
    public long EmploymentId { get; set; }
    public string EmploymentStatus { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string SourceType { get; set; } = "";
    public string? SourceReference { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class EmploymentRoleAssignment
{
    public long EmploymentRoleAssignmentId { get; set; }
    public long EmploymentId { get; set; }
    public int RoleId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int? AssignedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class TeamMembership
{
    public long TeamMembershipId { get; set; }
    public long EmploymentId { get; set; }
    public int TeamId { get; set; }
    public bool IsPrimary { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? ChangeReason { get; set; }
    public int? AssignedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class TeamLeaderAssignment
{
    public long TeamLeaderAssignmentId { get; set; }
    public int TeamId { get; set; }
    public long EmploymentId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int? AssignedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class TeamLeaderDelegation
{
    public long TeamLeaderDelegationId { get; set; }
    public long TeamLeaderAssignmentId { get; set; }
    public long DelegateEmploymentId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly EffectiveTo { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
