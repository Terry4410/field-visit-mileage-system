using FieldVisit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/admin/people")]
public sealed class V170PeopleAdminController(
    V170PeopleAdminService service)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<
        PagedResult<V170PeopleRowDto>>> Query(
        [FromQuery] V170PeopleQueryRequest request,
        CancellationToken ct)
        => Ok(
            await service.QueryAsync(
                request,
                ct));

    [HttpGet("{userId:int}")]
    public async Task<ActionResult<
        V170PersonDetailDto>> Get(
        int userId,
        CancellationToken ct)
        => Ok(
            await service.GetAsync(
                userId,
                ct));

    [HttpPost("external-supervisors")]
    public async Task<ActionResult<
        V170PersonDetailDto>>
        CreateExternalSupervisor(
            [FromBody]
            SaveExternalSupervisorRequest request,
            CancellationToken ct)
    {
        var result =
            await service
                .CreateExternalSupervisorAsync(
                    request,
                    ct);

        return CreatedAtAction(
            nameof(Get),
            new
            {
                userId = result.UserId
            },
            result);
    }

    [HttpPut("external-supervisors/{userId:int}")]
    public async Task<ActionResult<
        V170PersonDetailDto>>
        UpdateExternalSupervisor(
            int userId,
            [FromBody]
            UpdateExternalSupervisorRequest request,
            CancellationToken ct)
        => Ok(
            await service
                .UpdateExternalSupervisorAsync(
                    userId,
                    request,
                    ct));

    [HttpPut("internal-users/{userId:int}/access")]
    public async Task<ActionResult<
        V170PersonDetailDto>>
        UpdateInternalUserAccess(
            int userId,
            [FromBody]
            UpdateInternalUserAccessRequest request,
            CancellationToken ct)
        => Ok(
            await service
                .UpdateInternalUserAccessAsync(
                    userId,
                    request,
                    ct));
    [HttpGet("bulk/current.xlsx")]
    public async Task<IActionResult>
        DownloadBulkCurrent(
            CancellationToken ct)
    {
        var file =
            await service
                .ExportBulkCurrentAsync(ct);

        return File(
            file.Content,
            file.ContentType,
            file.FileName);
    }

    [HttpGet("bulk/template.xlsx")]
    public async Task<IActionResult>
        DownloadBulkTemplate(
            CancellationToken ct)
    {
        var file =
            await service
                .CreateBulkTemplateAsync(ct);

        return File(
            file.Content,
            file.ContentType,
            file.FileName);
    }

    [HttpPost("bulk/preview")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<
        V170PeopleBulkPreviewDto>>
        PreviewBulk(
            IFormFile file,
            CancellationToken ct)
    {
        if (file is null
            || file.Length == 0)
        {
            throw new InvalidOperationException(
                "請選擇 Excel 檔案。");
        }

        if (!Path.GetExtension(
                file.FileName)
            .Equals(
                ".xlsx",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "只支援 .xlsx 檔案。");
        }

        await using var ms =
            new MemoryStream();

        await file.CopyToAsync(
            ms,
            ct);

        return Ok(
            await service.PreviewBulkAsync(
                ms.ToArray(),
                ct));
    }

    [HttpPost("bulk/{importBatchId:guid}/confirm")]
    public async Task<ActionResult<
        V170PeopleBulkConfirmResultDto>>
        ConfirmBulk(
            Guid importBatchId,
            [FromBody]
            V170PeopleBulkConfirmRequest request,
            CancellationToken ct)
        => Ok(
            await service.ConfirmBulkAsync(
                importBatchId,
                request,
                ct));

    [HttpGet("bulk/{importBatchId:guid}/errors.xlsx")]
    public async Task<IActionResult>
        BulkErrors(
            Guid importBatchId,
            CancellationToken ct)
    {
        var file =
            await service
                .BulkErrorReportAsync(
                    importBatchId,
                    ct);

        return File(
            file.Content,
            file.ContentType,
            file.FileName);
    }


}
