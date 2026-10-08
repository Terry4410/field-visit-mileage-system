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

    [HttpPost("internal-users")]
    public async Task<ActionResult<V170PersonDetailDto>> CreateInternalUser(
        [FromBody] CreateInternalUserRequest request,
        CancellationToken ct)
    {
        var result = await service.CreateInternalUserAsync(request, ct);
        return CreatedAtAction(
            nameof(Get),
            new { userId = result.UserId },
            result);
    }

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

    [HttpPut("internal-users/{userId:int}/employment")]
    public async Task<ActionResult<V170PersonDetailDto>> UpdateInternalEmployment(
        int userId,
        [FromBody] UpdateInternalEmploymentRequest request,
        CancellationToken ct)
        => Ok(await service.UpdateInternalEmploymentAsync(userId,request,ct));

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
                "請選擇匯入檔案。");
        }

        if (!ImportFileCompatibility.IsSupported(
                file.FileName))
        {
            throw new InvalidOperationException(
                "只支援 .xlsx、.xls、.csv 檔案。");
        }

        await using var ms =
            new MemoryStream();

        await file.CopyToAsync(
            ms,
            ct);

        var content =
            ImportFileCompatibility.NormalizeToXlsx(
                file.FileName,
                ms.ToArray(),
                "people");

        return Ok(
            await service.PreviewBulkAsync(
                content,
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


    [HttpGet("personnel-bulk/template.xlsx")]
    public async Task<IActionResult> PersonnelBulkTemplate(CancellationToken ct)
    {
        var file = await service.CreatePersonnelBulkTemplateAsync(ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost("personnel-bulk/preview")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<V180SimpleBulkPreviewDto>> PersonnelBulkPreview(
        IFormFile file,
        CancellationToken ct)
        => Ok(await service.PreviewPersonnelBulkAsync(await ReadXlsx(file, ct), ct));

    [HttpPost("personnel-bulk/confirm")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<V180SimpleBulkConfirmResultDto>> PersonnelBulkConfirm(
        IFormFile file,
        CancellationToken ct)
        => Ok(await service.ConfirmPersonnelBulkAsync(await ReadXlsx(file, ct), ct));

    [HttpGet("team-membership-bulk/template.xlsx")]
    public async Task<IActionResult> TeamMembershipBulkTemplate(CancellationToken ct)
    {
        var file = await service.CreateTeamMembershipBulkTemplateAsync(ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost("team-membership-bulk/preview")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<V180SimpleBulkPreviewDto>> TeamMembershipBulkPreview(
        IFormFile file,
        CancellationToken ct)
        => Ok(await service.PreviewTeamMembershipBulkAsync(await ReadXlsx(file, ct), ct));

    [HttpPost("team-membership-bulk/confirm")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<V180SimpleBulkConfirmResultDto>> TeamMembershipBulkConfirm(
        IFormFile file,
        CancellationToken ct)
        => Ok(await service.ConfirmTeamMembershipBulkAsync(await ReadXlsx(file, ct), ct));

    [HttpPost("team-memberships/{teamId:int}/batch-add")]
    public async Task<ActionResult<V180BatchAddTeamMembersResult>> BatchAddTeamMembers(
        int teamId,
        [FromBody] V180BatchAddTeamMembersRequest request,
        CancellationToken ct)
        => Ok(await service.BatchAddTeamMembersAsync(teamId, request, ct));

    private static async Task<byte[]> ReadXlsx(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new InvalidOperationException("請選擇匯入檔案。");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("此批次功能只接受 .xlsx。");

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        return stream.ToArray();
    }


}
