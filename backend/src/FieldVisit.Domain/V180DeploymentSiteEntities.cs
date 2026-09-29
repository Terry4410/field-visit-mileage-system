using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FieldVisit.Domain.Entities;

[Table("DeploymentSites")]
public sealed class DeploymentSite
{
    public int DeploymentSiteId { get; set; }
    public int CenterId { get; set; }
    [MaxLength(50)] public string SiteCode { get; set; } = "";
    [MaxLength(200)] public string SiteName { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedByUserId { get; set; }
    public DateTime? InactivatedAt { get; set; }
    public int? InactivatedByUserId { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public Center Center { get; set; } = null!;
    public List<DeploymentSiteLocationAssignment> LocationAssignments { get; set; } = [];
    public List<TeamDeploymentSiteAssignment> TeamAssignments { get; set; } = [];
    public List<EmploymentDeploymentSiteAssignment> EmploymentAssignments { get; set; } = [];
}

[Table("DeploymentSiteLocationAssignments")]
public sealed class DeploymentSiteLocationAssignment
{
    public long DeploymentSiteLocationAssignmentId { get; set; }
    public int DeploymentSiteId { get; set; }
    public int LocationId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    [MaxLength(500)] public string? ChangeReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public DeploymentSite DeploymentSite { get; set; } = null!;
}

[Table("TeamDeploymentSiteAssignments")]
public sealed class TeamDeploymentSiteAssignment
{
    public long TeamDeploymentSiteAssignmentId { get; set; }
    public int TeamId { get; set; }
    public int DeploymentSiteId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public DeploymentSite DeploymentSite { get; set; } = null!;
}

[Table("EmploymentDeploymentSiteAssignments")]
public sealed class EmploymentDeploymentSiteAssignment
{
    public long EmploymentDeploymentSiteAssignmentId { get; set; }
    public long EmploymentId { get; set; }
    public int DeploymentSiteId { get; set; }
    public bool IsPrimary { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public DeploymentSite DeploymentSite { get; set; } = null!;
}
