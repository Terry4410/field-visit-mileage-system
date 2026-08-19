namespace FieldVisit.Application;

public sealed record TeamScopeDto(int TeamId, string TeamName, bool IsPrimary);

public sealed record DataScopeDto(
    string ScopeType,
    int? OrganizationId,
    int? TeamId,
    string? TeamName);

public sealed record CurrentUserDto(
    int UserId,
    string EmployeeNo,
    string DisplayName,
    string? Email,
    int? OrganizationId,
    int? TeamId,
    string? TeamName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<TeamScopeDto>? TeamScopes = null,
    IReadOnlyList<DataScopeDto>? DataScopes = null)
{
    public IReadOnlyList<int> TeamIds =>
        TeamScopes is { Count: > 0 }
            ? TeamScopes.Select(x => x.TeamId).Distinct().ToList()
            : TeamId.HasValue ? new[] { TeamId.Value } : Array.Empty<int>();
}

public sealed record DemoLoginRequest(string Account, string Password);
public sealed record DemoLoginResponse(string AccessToken, DateTime ExpiresAtUtc, CurrentUserDto User);

public sealed record TripStopInput(
    int? LocationId,
    int? ProjectId,
    int? VisitTypeId,
    string SourceType,
    string LocationName,
    string? Address,
    string? VisitPurpose,
    string? Notes);

public sealed record SaveTripRequest(
    DateOnly VisitDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    decimal? ClaimedDistanceKm,
    string? Purpose,
    string? Notes,
    bool TimeOverlapConfirmed,
    IReadOnlyList<TripStopInput> Stops,
    int? TeamId = null);

public sealed record SubmitTripRequest(bool ConfirmTimeOverlap);
public sealed record TimeOverlapRequest(DateOnly VisitDate, TimeOnly StartTime, TimeOnly EndTime, long? ExcludeVisitTripId);
public sealed record TimeOverlapItem(long VisitTripId, string TripNo, TimeOnly? StartTime, TimeOnly? EndTime, string Status);
public sealed record TimeOverlapResult(bool HasOverlap, string? Code, string? Message, IReadOnlyList<TimeOverlapItem> OverlappingTrips);

public sealed record TripDto(
    long VisitTripId,
    string TripNo,
    int UserId,
    string VisitorName,
    int? TeamId,
    string? TeamName,
    DateOnly VisitDate,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    bool HasTimeOverlapWarning,
    bool TimeOverlapConfirmed,
    string Status,
    string StatusName,
    string? Purpose,
    string? Notes,
    string? ReturnReason,
    decimal? ClaimedDistanceKm,
    decimal? SystemDistanceKm,
    decimal? ApprovedDistanceKm,
    decimal? RatePerKmSnapshot,
    decimal? ApprovedAmount,
    IReadOnlyList<TripStopInput> Stops,
    string RowVersion);

public sealed record MileageBatchRequest(string Mode, DateOnly? StartDate, DateOnly? EndDate, IReadOnlyList<long>? SelectedTripIds);
public sealed record MileageBatchItem(long VisitTripId, string TripNo, string Status, decimal? SystemDistanceKm, string? ErrorCode, string? ErrorMessage);
public sealed record MileageBatchResult(int Total, int Success, int Failed, int Skipped, IReadOnlyList<MileageBatchItem> Items);

public sealed record ApproveTripRequest(decimal? ApprovedDistanceKm, string RowVersion, string? Comments);
public sealed record ReturnTripRequest(string Reason, string RowVersion);
public sealed record BatchApproveItem(long VisitTripId, decimal ApprovedDistanceKm, string RowVersion);
public sealed record BatchApproveRequest(IReadOnlyList<BatchApproveItem> Items);
public sealed record BatchApproveResult(int Success, int Failed, IReadOnlyList<string> Errors);

public sealed record LocationDto(
    int LocationId,
    int? TeamId,
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

public sealed record UpdateLocationRequest(string LocationName, string? City, string? District, string? Address, string? PlusCode, string RowVersion);
public sealed record PromoteLocationRequest(string RowVersion);
public sealed record BatchPublishLocationsRequest(IReadOnlyList<int> LocationIds);
public sealed record BatchPublishLocationsResult(int Success, int Failed, IReadOnlyList<string> Errors);

public sealed record ProjectDto(int ProjectId, int? TeamId, string ProjectCode, string ProjectName, string? Description, string LocationMode, DateOnly? StartDate, DateOnly? EndDate, bool IsActive);
public sealed record SaveProjectRequest(int? TeamId, string ProjectCode, string ProjectName, string? Description, string LocationMode, DateOnly? StartDate, DateOnly? EndDate, bool IsActive);
public sealed record VisitTypeDto(int VisitTypeId, string VisitTypeCode, string VisitTypeName, string? Description, int SortOrder, bool IsActive);
public sealed record SaveVisitTypeRequest(string VisitTypeCode, string VisitTypeName, string? Description, int SortOrder, bool IsActive);
public sealed record TeamDto(int TeamId, int OrganizationId, string TeamCode, string TeamName);

public sealed record MileageRateDto(int MileageRateRuleId, int? OrganizationId, string RuleName, string VehicleType, decimal RatePerKm, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);
public sealed record MileageRateImpactDto(
    DateOnly EffectiveFrom,
    string VehicleType,
    int ApprovedTripCount,
    DateOnly? FirstApprovedVisitDate,
    DateOnly? LastApprovedVisitDate,
    bool RequiresAcknowledgement);
public sealed record CreateMileageRateRequest(
    string RuleName,
    string VehicleType,
    decimal RatePerKm,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool AcknowledgeHistoricalImpact = false);
public sealed record UpdateMileageRateRequest(
    string RuleName,
    string VehicleType,
    decimal RatePerKm,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    bool AcknowledgeHistoricalImpact = false);

public sealed record MileageReportRow(
    string TripNo,
    DateOnly VisitDate,
    string VisitorName,
    string? TeamName,
    string Route,
    decimal? ClaimedDistanceKm,
    decimal? SystemDistanceKm,
    decimal? ApprovedDistanceKm,
    decimal? RatePerKmSnapshot,
    decimal? ApprovedAmount,
    string Status,
    string StatusName);

public sealed record RouteCalculationResult(bool Success, decimal? DistanceKm, string? ErrorCode, string? ErrorMessage);
public sealed record GeocodingResult(bool Success, decimal? Latitude, decimal? Longitude, string? ErrorCode, string? ErrorMessage);
