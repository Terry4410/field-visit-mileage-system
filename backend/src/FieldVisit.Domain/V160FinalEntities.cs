namespace FieldVisit.Domain.Entities;

public sealed class ImportBatch
{
    public Guid ImportBatchId { get; set; }
    public string ImportType { get; set; } = "";
    public int OrganizationId { get; set; }
    public int RequestedByUserId { get; set; }
    public string Status { get; set; } = "Previewed";
    public int TotalCount { get; set; }
    public int ValidCount { get; set; }
    public int ErrorCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}

public sealed class ImportBatchItem
{
    public long ImportBatchItemId { get; set; }
    public Guid ImportBatchId { get; set; }
    public int RowNumber { get; set; }
    public string EntityType { get; set; } = "";
    public string Action { get; set; } = "";
    public string Status { get; set; } = "Valid";
    public string DisplayKey { get; set; } = "";
    public string DataJson { get; set; } = "{}";
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}
