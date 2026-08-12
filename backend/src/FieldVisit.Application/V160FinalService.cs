namespace FieldVisit.Application;

public sealed class V160FinalService(
    ICurrentUserService current,
    IV160FinalRepository repository,
    IReportDocumentService reports,
    IWorkbookImportService imports,
    IBackgroundJobService jobs)
{
    public Task<PagedResult<TripQueryRowDto>> QueryTripsAsync(TripQueryRequest request, CancellationToken ct) =>
        repository.QueryTripsAsync(current.GetRequired(), NormalizeQuery(request), false, ct);

    public async Task<ReportExportContext> ExportAsync(string format, TripQueryRequest request, CancellationToken ct)
    {
        var user = current.GetRequired();
        var normalized = NormalizeQuery(request) with { Page = 1, PageSize = 100000 };
        var result = await repository.QueryTripsAsync(user, normalized, true, ct);
        ReportExportContext file = format.Trim().ToLowerInvariant() switch
        {
            "xlsx" or "excel" => await reports.CreateExcelAsync(result.Items, normalized, user, ct),
            "pdf" => await reports.CreatePdfAsync(result.Items, normalized, user, ct),
            _ => throw new InvalidOperationException("只支援 xlsx 或 pdf。")
        };
        await repository.AuditExportAsync(user, format, normalized, result.TotalCount, ct);
        return file;
    }

    public Task<CorrectionDraftDto> GetCorrectionDraftAsync(long tripId, CancellationToken ct) =>
        repository.GetCorrectionDraftAsync(RequireRole("visitor"), tripId, ct);

    public Task<CorrectionRequestDto> CreateCorrectionAsync(CreateCorrectionRequest request, CancellationToken ct) =>
        repository.CreateCorrectionAsync(RequireRole("visitor"), request, ct);

    public Task<IReadOnlyList<CorrectionRequestDto>> CorrectionsAsync(string? status, CancellationToken ct) =>
        repository.GetCorrectionsAsync(current.GetRequired(), status, ct);

    public Task<CorrectionRequestDto> ReviewCorrectionAsync(long id, ReviewCorrectionRequest request, CancellationToken ct) =>
        repository.ReviewCorrectionAsync(RequireRole("leader"), id, request, ct);

    public Task<CorrectionRequestDto> CloseCorrectionAsync(long id, CloseCorrectionRequest request, CancellationToken ct) =>
        repository.CloseCorrectionAsync(RequireRole("admin"), id, request, ct);

    public Task<IReadOnlyList<UserOptionDto>> VisitorsAsync(CancellationToken ct) =>
        repository.GetScopedVisitorsAsync(current.GetRequired(), ct);

    public Task<IReadOnlyList<AdminUserAccessDto>> UsersAsync(CancellationToken ct) =>
        repository.GetUsersAsync(RequireRole("admin"), ct);

    public Task<AdminUserAccessDto> SaveUserAccessAsync(int userId, SaveUserAccessRequest request, CancellationToken ct) =>
        repository.SaveUserAccessAsync(RequireRole("admin"), userId, request, ct);

    public Task<IReadOnlyList<ManagedTeamDto>> TeamsAsync(bool includeInactive, CancellationToken ct) =>
        repository.GetManagedTeamsAsync(RequireRole("admin"), includeInactive, ct);

    public Task<ManagedTeamDto> CreateTeamAsync(SaveManagedTeamRequest request, CancellationToken ct) =>
        repository.CreateManagedTeamAsync(RequireRole("admin"), request, ct);

    public Task<ManagedTeamDto> UpdateTeamAsync(int teamId, SaveManagedTeamRequest request, CancellationToken ct) =>
        repository.UpdateManagedTeamAsync(RequireRole("admin"), teamId, request, ct);

    public Task DeactivateTeamAsync(int teamId, CancellationToken ct) =>
        repository.DeactivateManagedTeamAsync(RequireRole("admin"), teamId, ct);

    public Task<IReadOnlyList<ManagedLocationDto>> ManagedLocationsAsync(bool includeInactive, CancellationToken ct) =>
        repository.GetManagedLocationsAsync(RequireAny("admin", "leader"), includeInactive, ct);

    public Task<ManagedLocationDto> CreateManagedLocationAsync(SaveManagedLocationRequest request, CancellationToken ct) =>
        repository.CreateManagedLocationAsync(RequireAny("admin", "leader"), request, ct);

    public Task<ManagedLocationDto> UpdateManagedLocationAsync(int id, SaveManagedLocationRequest request, CancellationToken ct) =>
        repository.UpdateManagedLocationAsync(RequireAny("admin", "leader"), id, request, ct);

    public Task DeactivateManagedLocationAsync(int id, CancellationToken ct) =>
        repository.DeactivateManagedLocationAsync(RequireRole("admin"), id, ct);

    public Task<DashboardSummaryDto> DashboardAsync(CancellationToken ct) =>
        repository.GetDashboardAsync(current.GetRequired(), ct);

    public Task<ReportExportContext> ImportTemplateAsync(string type, CancellationToken ct) =>
        imports.CreateTemplateAsync(current.GetRequired(), type, ct);

    public Task<ImportPreviewDto> PreviewImportAsync(string type, byte[] content, CancellationToken ct) =>
        imports.PreviewAsync(RequireAny("admin", "leader"), type, content, ct);

    public Task<ReportExportContext> ImportErrorReportAsync(Guid batchId, CancellationToken ct) =>
        imports.CreateErrorReportAsync(RequireAny("admin", "leader"), batchId, ct);

    public Task<ImportConfirmResultDto> ConfirmImportAsync(Guid batchId, CancellationToken ct) =>
        imports.ConfirmAsync(RequireAny("admin", "leader"), batchId, ct);

    public Task<BackgroundJobDto> EnqueueMileageAsync(MileageBatchRequest request, CancellationToken ct) =>
        jobs.EnqueueMileageAsync(RequireRole("leader"), request, ct);

    public Task<BackgroundJobDto> EnqueueGeocodingAsync(CreateGeocodingJobRequest request, CancellationToken ct) =>
        jobs.EnqueueGeocodingAsync(RequireAny("leader", "admin"), request, ct);

    public Task<BackgroundJobDto> JobAsync(Guid id, CancellationToken ct) => jobs.GetAsync(current.GetRequired(), id, ct);

    private CurrentUserDto RequireRole(string role)
    {
        var user = current.GetRequired();
        if (!user.Roles.Any(x => x.Equals(role, StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("目前角色無權執行此操作。");
        return user;
    }

    private CurrentUserDto RequireAny(params string[] roles)
    {
        var user = current.GetRequired();
        if (!roles.Any(role => user.Roles.Any(x => x.Equals(role, StringComparison.OrdinalIgnoreCase))))
            throw new UnauthorizedAccessException("目前角色無權執行此操作。");
        return user;
    }

    private static TripQueryRequest NormalizeQuery(TripQueryRequest request)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = request.PageSize is 20 or 50 or 100 ? request.PageSize : 50;
        var start = request.StartDate;
        var end = request.EndDate;
        if (start.HasValue && end.HasValue && end < start) throw new InvalidOperationException("查詢結束日期不可早於開始日期。");
        if (!start.HasValue && !end.HasValue)
        {
            var today = BusinessTime.Today;
            start = new DateOnly(today.Year, today.Month, 1);
            end = today;
        }
        return request with { StartDate = start, EndDate = end, Page = page, PageSize = pageSize };
    }
}
