using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class V160FinalController(V160FinalService service) : ControllerBase
{
    [HttpGet("query/trips")]
    public async Task<ActionResult<PagedResult<TripQueryRowDto>>> QueryTrips([FromQuery] TripQueryRequest request, CancellationToken ct) =>
        Ok(await service.QueryTripsAsync(request, ct));

    [HttpGet("query/visitors")]
    public async Task<ActionResult<IReadOnlyList<UserOptionDto>>> Visitors(CancellationToken ct) => Ok(await service.VisitorsAsync(ct));

    [HttpGet("query/trips/export.xlsx")]
    public async Task<IActionResult> Excel([FromQuery] TripQueryRequest request, CancellationToken ct)
    {
        var file = await service.ExportAsync("xlsx", request, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("query/trips/export.pdf")]
    public async Task<IActionResult> Pdf([FromQuery] TripQueryRequest request, CancellationToken ct)
    {
        var file = await service.ExportAsync("pdf", request, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("corrections/draft/{tripId:long}")]
    [Authorize(Roles = "visitor")]
    public async Task<ActionResult<CorrectionDraftDto>> CorrectionDraft(long tripId, CancellationToken ct) =>
        Ok(await service.GetCorrectionDraftAsync(tripId, ct));

    [HttpPost("corrections")]
    [Authorize(Roles = "visitor")]
    public async Task<ActionResult<CorrectionRequestDto>> CreateCorrection(CreateCorrectionRequest request, CancellationToken ct) =>
        Ok(await service.CreateCorrectionAsync(request, ct));

    [HttpGet("corrections")]
    public async Task<ActionResult<IReadOnlyList<CorrectionRequestDto>>> Corrections([FromQuery] string? status, CancellationToken ct) =>
        Ok(await service.CorrectionsAsync(status, ct));

    [HttpPost("corrections/{id:long}/leader-review")]
    [Authorize(Roles = "leader")]
    public async Task<ActionResult<CorrectionRequestDto>> LeaderReview(long id, ReviewCorrectionRequest request, CancellationToken ct) =>
        Ok(await service.ReviewCorrectionAsync(id, request, ct));

    [HttpPost("corrections/{id:long}/admin-close")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<CorrectionRequestDto>> AdminClose(long id, CloseCorrectionRequest request, CancellationToken ct) =>
        Ok(await service.CloseCorrectionAsync(id, request, ct));

    [HttpGet("admin/users")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<IReadOnlyList<AdminUserAccessDto>>> Users(CancellationToken ct) => Ok(await service.UsersAsync(ct));

    [HttpPut("admin/users/{userId:int}/access")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<AdminUserAccessDto>> SaveUserAccess(int userId, SaveUserAccessRequest request, CancellationToken ct) =>
        Ok(await service.SaveUserAccessAsync(userId, request, ct));

    [HttpGet("admin/teams")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<IReadOnlyList<ManagedTeamDto>>> Teams([FromQuery] bool includeInactive = true, CancellationToken ct = default) =>
        Ok(await service.TeamsAsync(includeInactive, ct));

    [HttpPost("admin/teams")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<ManagedTeamDto>> CreateTeam(SaveManagedTeamRequest request, CancellationToken ct) =>
        Ok(await service.CreateTeamAsync(request, ct));

    [HttpPut("admin/teams/{teamId:int}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<ManagedTeamDto>> UpdateTeam(int teamId, SaveManagedTeamRequest request, CancellationToken ct) =>
        Ok(await service.UpdateTeamAsync(teamId, request, ct));

    [HttpDelete("admin/teams/{teamId:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeactivateTeam(int teamId, CancellationToken ct)
    {
        await service.DeactivateTeamAsync(teamId, ct);
        return NoContent();
    }

    [HttpGet("managed-locations")]
    [Authorize(Roles = "admin,leader")]
    public async Task<ActionResult<IReadOnlyList<ManagedLocationDto>>> ManagedLocations([FromQuery] bool includeInactive = true, CancellationToken ct = default) =>
        Ok(await service.ManagedLocationsAsync(includeInactive, ct));

    [HttpPost("managed-locations")]
    [Authorize(Roles = "admin,leader")]
    public async Task<ActionResult<ManagedLocationDto>> CreateLocation(SaveManagedLocationRequest request, CancellationToken ct) =>
        Ok(await service.CreateManagedLocationAsync(request, ct));

    [HttpPut("managed-locations/{locationId:int}")]
    [Authorize(Roles = "admin,leader")]
    public async Task<ActionResult<ManagedLocationDto>> UpdateLocation(int locationId, SaveManagedLocationRequest request, CancellationToken ct) =>
        Ok(await service.UpdateManagedLocationAsync(locationId, request, ct));

    [HttpDelete("managed-locations/{locationId:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeactivateLocation(int locationId, CancellationToken ct)
    {
        await service.DeactivateManagedLocationAsync(locationId, ct);
        return NoContent();
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardSummaryDto>> Dashboard(CancellationToken ct) => Ok(await service.DashboardAsync(ct));

    [HttpGet("imports/{type}/template")]
    [Authorize(Roles = "admin,leader")]
    public async Task<IActionResult> ImportTemplate(string type, CancellationToken ct)
    {
        var file = await service.ImportTemplateAsync(type, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost("imports/{type}/preview")]
    [Authorize(Roles = "admin,leader")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewImport(string type, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new InvalidOperationException("請選擇 Excel 檔案。");
        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("只支援 .xlsx 檔案。");
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return Ok(await service.PreviewImportAsync(type, ms.ToArray(), ct));
    }

    [HttpGet("imports/{importBatchId:guid}/errors.xlsx")]
    [Authorize(Roles = "admin,leader")]
    public async Task<IActionResult> ImportErrors(Guid importBatchId, CancellationToken ct)
    {
        var file = await service.ImportErrorReportAsync(importBatchId, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost("imports/{importBatchId:guid}/confirm")]
    [Authorize(Roles = "admin,leader")]
    public async Task<ActionResult<ImportConfirmResultDto>> ConfirmImport(Guid importBatchId, CancellationToken ct) =>
        Ok(await service.ConfirmImportAsync(importBatchId, ct));

    [HttpPost("jobs/mileage")]
    [Authorize(Roles = "leader")]
    public async Task<ActionResult<BackgroundJobDto>> EnqueueMileage(MileageBatchRequest request, CancellationToken ct) =>
        Ok(await service.EnqueueMileageAsync(request, ct));

    [HttpPost("jobs/geocoding")]
    [Authorize(Roles = "admin,leader")]
    public async Task<ActionResult<BackgroundJobDto>> EnqueueGeocoding(CreateGeocodingJobRequest request, CancellationToken ct) =>
        Ok(await service.EnqueueGeocodingAsync(request, ct));

    [HttpGet("jobs/{jobId:guid}")]
    public async Task<ActionResult<BackgroundJobDto>> Job(Guid jobId, CancellationToken ct) => Ok(await service.JobAsync(jobId, ct));
}
