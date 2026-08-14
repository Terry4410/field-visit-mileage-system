namespace FieldVisit.Application;

public sealed record TripQueryRequest(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    int? TeamId = null,
    int? VisitorId = null,
    string? LocationKeyword = null,
    int? ProjectId = null,
    int? VisitTypeId = null,
    string? Status = null,
    bool IncludeCancelled = false,
    int Page = 1,
    int PageSize = 50,
    string Sort = "date_desc");

public sealed record QueryStopDto(
    int StopSequence,
    int? LocationId,
    string? LocationCode,
    string LocationName,
    string? Address,
    int? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    int? VisitTypeId,
    string? VisitTypeCode,
    string? VisitTypeName,
    string? VisitPurpose,
    string? Notes);

public sealed record TripQueryRowDto(
    long VisitTripId,
    string TripNo,
    DateOnly VisitDate,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    int VisitorId,
    string EmployeeNo,
    string VisitorName,
    int? TeamId,
    string? TeamName,
    string Route,
    string ProjectNames,
    string VisitTypeNames,
    decimal? ClaimedDistanceKm,
    decimal? SystemDistanceKm,
    decimal? ApprovedDistanceKm,
    decimal? RatePerKmSnapshot,
    decimal? SubsidyAmount,
    string MileageState,
    string Status,
    string StatusName,
    int SnapshotVersion,
    bool IsSnapshot,
    string? Notes,
    string? ReturnReason,
    string? CorrectionStatus,
    IReadOnlyList<QueryStopDto> Stops);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record CorrectionStopProposal(
    int StopSequence,
    string? LocationCode,
    string LocationName,
    string? Address,
    string? ProjectCode,
    string? ProjectName,
    string? VisitTypeCode,
    string? VisitTypeName,
    string? VisitPurpose,
    string? Notes);

public sealed record CorrectionProposal(
    DateOnly VisitDate,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    string? Notes,
    decimal? ClaimedDistanceKm,
    decimal? ApprovedDistanceKm,
    decimal? RatePerKm,
    decimal? SubsidyAmount,
    IReadOnlyList<CorrectionStopProposal> Stops);

public sealed record CorrectionDraftDto(
    long VisitTripId,
    string TripNo,
    int BaseSnapshotVersion,
    CorrectionProposal Proposal);

public sealed record CreateCorrectionRequest(long VisitTripId, string Reason, CorrectionProposal Proposal);
public sealed record ReviewCorrectionRequest(bool Approve, string? Comments, string RowVersion);
public sealed record CloseCorrectionRequest(bool Approve, string? Comments, string RowVersion);

public sealed record CorrectionRequestDto(
    long CorrectionRequestId,
    long VisitTripId,
    string TripNo,
    string VisitorName,
    string? TeamName,
    int BaseSnapshotVersion,
    int? ResultSnapshotVersion,
    string Status,
    string Reason,
    DateTime RequestedAt,
    string RequestedBy,
    DateTime? LeaderReviewedAt,
    string? LeaderReviewedBy,
    string? LeaderComments,
    DateTime? AdminClosedAt,
    string? AdminClosedBy,
    string? AdminComments,
    bool RequiresAdminClose,
    CorrectionProposal Proposal,
    IReadOnlyList<CorrectionChangeDto> Changes,
    string RowVersion);

public sealed record CorrectionChangeDto(string FieldName, string? OldValue, string? NewValue);

public sealed record UserOptionDto(int UserId, string EmployeeNo, string DisplayName, int? TeamId, string? TeamName);

public sealed record AdminUserAccessDto(
    int UserId,
    string EmployeeNo,
    string DisplayName,
    string? Email,
    bool IsActive,
    IReadOnlyList<string> Roles,
    IReadOnlyList<TeamScopeDto> TeamScopes);

public sealed record ManagedTeamDto(int TeamId, int OrganizationId, string TeamCode, string TeamName, bool IsActive);
public sealed record SaveManagedTeamRequest(string TeamCode, string TeamName, bool IsActive = true);

public sealed record SaveUserAccessRequest(bool IsActive, IReadOnlyList<string> Roles, IReadOnlyList<SaveTeamScopeRequest> TeamScopes);
public sealed record SaveTeamScopeRequest(int TeamId, bool IsPrimary);

public sealed record ManagedLocationDto(
    int LocationId,
    string LocationCode,
    int? TeamId,
    string? TeamName,
    string LocationName,
    string LocationType,
    string? City,
    string? District,
    string? Address,
    string? PlusCode,
    decimal? Latitude,
    decimal? Longitude,
    bool IsTemporary,
    string ApprovalStatus,
    string GeocodingStatus,
    bool IsActive,
    DateTime CreatedAt,
    string RowVersion);

public sealed record SaveManagedLocationRequest(
    int? TeamId,
    string LocationName,
    string LocationType,
    string? City,
    string? District,
    string? Address,
    string? PlusCode,
    bool IsActive,
    string? RowVersion);

public sealed record ImportPreviewItemDto(int RowNumber, string EntityType, string Action, string Status, string DisplayKey, string? ErrorMessage);
public sealed record ImportPreviewDto(Guid ImportBatchId, string ImportType, int TotalCount, int ValidCount, int ErrorCount, IReadOnlyList<ImportPreviewItemDto> Items);
public sealed record ImportConfirmResultDto(Guid ImportBatchId, int Created, int Updated, int Unchanged, int Failed, IReadOnlyList<string> Errors);

public sealed record BackgroundJobDto(
    Guid BackgroundJobId,
    string JobType,
    string Status,
    string? Mode,
    int TotalCount,
    int SuccessCount,
    int FailedCount,
    int SkippedCount,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);

public sealed record CreateGeocodingJobRequest(string Mode = "Selected", DateOnly? StartDate = null, DateOnly? EndDate = null, IReadOnlyList<int>? LocationIds = null);

public sealed record DashboardSummaryDto(
    int ThisMonthTrips,
    int PendingApproval,
    int Approved,
    int PendingLocations,
    int PendingCorrections,
    decimal? CurrentRatePerKm);

public sealed record ReportExportContext(string FileName, byte[] Content, string ContentType);
