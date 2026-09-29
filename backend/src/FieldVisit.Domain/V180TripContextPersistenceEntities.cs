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
    public byte[] RowVersion { get; set; } = [];
}
