namespace FieldVisit.Application;

public interface IV160FinalRepository
{
    Task<PagedResult<TripQueryRowDto>> QueryTripsAsync(CurrentUserDto user, TripQueryRequest request, bool exportAll, CancellationToken ct);
    Task<CorrectionDraftDto> GetCorrectionDraftAsync(CurrentUserDto user, long visitTripId, CancellationToken ct);
    Task<CorrectionRequestDto> CreateCorrectionAsync(CurrentUserDto user, CreateCorrectionRequest request, CancellationToken ct);
    Task<IReadOnlyList<CorrectionRequestDto>> GetCorrectionsAsync(CurrentUserDto user, string? status, CancellationToken ct);
    Task<CorrectionRequestDto> ReviewCorrectionAsync(CurrentUserDto user, long correctionRequestId, ReviewCorrectionRequest request, CancellationToken ct);
    Task<CorrectionRequestDto> CloseCorrectionAsync(CurrentUserDto user, long correctionRequestId, CloseCorrectionRequest request, CancellationToken ct);

    Task<IReadOnlyList<UserOptionDto>> GetScopedVisitorsAsync(CurrentUserDto user, CancellationToken ct);
    Task<IReadOnlyList<AdminUserAccessDto>> GetUsersAsync(CurrentUserDto user, CancellationToken ct);
    Task<AdminUserAccessDto> SaveUserAccessAsync(CurrentUserDto user, int userId, SaveUserAccessRequest request, CancellationToken ct);
    Task<IReadOnlyList<ManagedTeamDto>> GetManagedTeamsAsync(CurrentUserDto user, bool includeInactive, CancellationToken ct);
    Task<ManagedTeamDto> CreateManagedTeamAsync(CurrentUserDto user, SaveManagedTeamRequest request, CancellationToken ct);
    Task<ManagedTeamDto> UpdateManagedTeamAsync(CurrentUserDto user, int teamId, SaveManagedTeamRequest request, CancellationToken ct);
    Task DeactivateManagedTeamAsync(CurrentUserDto user, int teamId, CancellationToken ct);

    Task<IReadOnlyList<ManagedLocationDto>> GetManagedLocationsAsync(CurrentUserDto user, bool includeInactive, CancellationToken ct);
    Task<PagedResult<ManagedLocationDto>> SearchManagedLocationsAsync(CurrentUserDto user, ManagedLocationQueryRequest request, CancellationToken ct);
    Task<ManagedLocationDto> CreateManagedLocationAsync(CurrentUserDto user, SaveManagedLocationRequest request, CancellationToken ct);
    Task<ManagedLocationDto> UpdateManagedLocationAsync(CurrentUserDto user, int locationId, SaveManagedLocationRequest request, CancellationToken ct);
    Task DeactivateManagedLocationAsync(CurrentUserDto user, int locationId, CancellationToken ct);
    Task<ManagedLocationDeleteImpactDto> GetManagedLocationDeleteImpactAsync(CurrentUserDto user, int locationId, CancellationToken ct);
    Task DeleteManagedLocationAsync(CurrentUserDto user, int locationId, CancellationToken ct);

    Task<DashboardSummaryDto> GetDashboardAsync(CurrentUserDto user, CancellationToken ct);
    Task AuditExportAsync(CurrentUserDto user, string format, TripQueryRequest request, int count, CancellationToken ct);
}

public interface IReportDocumentService
{
    Task<ReportExportContext> CreateExcelAsync(IReadOnlyList<TripQueryRowDto> rows, TripQueryRequest request, CurrentUserDto user, CancellationToken ct);
    Task<ReportExportContext> CreatePdfAsync(IReadOnlyList<TripQueryRowDto> rows, TripQueryRequest request, CurrentUserDto user, CancellationToken ct);
}

public interface IWorkbookImportService
{
    Task<ReportExportContext> CreateTemplateAsync(CurrentUserDto user, string importType, CancellationToken ct);
    Task<ImportPreviewDto> PreviewAsync(CurrentUserDto user, string importType, byte[] content, CancellationToken ct);
    Task<ReportExportContext> CreateErrorReportAsync(CurrentUserDto user, Guid importBatchId, CancellationToken ct);
    Task<ImportConfirmResultDto> ConfirmAsync(CurrentUserDto user, Guid importBatchId, CancellationToken ct);
}

public interface IBackgroundJobService
{
    Task<BackgroundJobDto> EnqueueMileageAsync(CurrentUserDto user, MileageBatchRequest request, CancellationToken ct);
    Task<BackgroundJobDto> EnqueueGeocodingAsync(CurrentUserDto user, CreateGeocodingJobRequest request, CancellationToken ct);
    Task<BackgroundJobDto> GetAsync(CurrentUserDto user, Guid jobId, CancellationToken ct);
    Task<bool> ProcessNextAsync(CancellationToken ct);
}

public interface IBackgroundJobSignal
{
    void Signal();
    Task WaitAsync(CancellationToken ct);
}
