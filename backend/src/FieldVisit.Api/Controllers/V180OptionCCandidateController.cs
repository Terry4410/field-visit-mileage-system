using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace FieldVisit.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/optionc-candidate")]
public sealed class V180OptionCCandidateController(V180OptionCCandidateService service, V180OptionCMergeService mergeService, V180OptionCStageEvidenceService evidenceService, V180OptionCLeaderReviewService leaderReviewService) : ControllerBase
{
    [HttpPost("locations/{locationId:int}/changes")]
    public Task<V180B3RequestView> Submit(int locationId, V180B3SubmitLocation input, CancellationToken ct)
    {
        if (locationId != input.LocationId) throw new InvalidOperationException("OPTIONC_LOCATION_ROUTE_MISMATCH");
        return service.SubmitAsync(input, ct);
    }

    [HttpPost("locations/{locationId:int}/low-risk-direct")]
    public Task<V180B3RequestView> SaveLowRisk(
        int locationId, V180OptionCLowRiskDirectUpdate input, CancellationToken ct)
        => service.SaveLowRiskAsync(locationId, input, ct);

    [HttpPost("locations")]
    public Task<V180B3RequestView> CreateLocation(
        V180OptionCCreateLocation input, CancellationToken ct)
        => service.CreateAsync(input, ct);

    // Import Preview remains unchanged. This explicit isolated-only route
    // submits previously staged Create rows for independent canonical
    // Leader/Admin review; legacy /imports/{id}/confirm stays fail-closed.
    [HttpPost("imports/{importBatchId:guid}/submit-creates-for-review")]
    [Authorize(Roles = "admin,leader")]
    public Task<V180OptionCExcelCreateResult> SubmitExcelCreates(
        Guid importBatchId,CancellationToken ct)
        => service.SubmitExcelCreatesAsync(importBatchId,ct);

    // Published Excel Update: no legacy Confirm mutation, no mixed Create
    // bypass. Each row becomes an independently reviewed canonical request.
    [HttpPost("imports/{importBatchId:guid}/submit-updates-for-review")]
    public Task<V180OptionCExcelCreateResult> SubmitExcelUpdates(
        Guid importBatchId, CancellationToken ct)
        => service.SubmitExcelUpdatesAsync(importBatchId,ct);

    [HttpGet("imports/{importBatchId:guid}/request-links")]
    public Task<IReadOnlyList<V180OptionCExcelRowLink>> ExcelRequestLinks(
        Guid importBatchId, CancellationToken ct)
        => service.ExcelRequestLinksAsync(importBatchId,ct);

    [HttpPost("locations/merge-fields")]
    public Task<V180B3RequestView> SubmitMerge(V180OptionCMergeSubmit input, CancellationToken ct)
        => mergeService.SubmitAsync(input, ct);

    [HttpGet("requests/{id:guid}/stage")]
    public Task<V180OptionCStageEvidenceView> Stage(Guid id, CancellationToken ct)
        => evidenceService.ReadForAdminAsync(id, ct);

    [HttpPost("requests/{id:guid}/geocode-evidence")]
    public Task<V180OptionCStageEvidenceView> GeocodeEvidence(Guid id, V180OptionCGeocodeEvidence input, CancellationToken ct)
        => evidenceService.RecordGeocodeAsync(id, input, ct);

    [HttpPost("requests/{id:guid}/duplicate-evidence")]
    public Task<V180OptionCStageEvidenceView> DuplicateEvidence(Guid id, V180OptionCDuplicateEvidence input, CancellationToken ct)
        => evidenceService.RecordDuplicateReviewAsync(id, input, ct);

    [HttpGet("leader/review-queue")]
    public Task<IReadOnlyList<V180OptionCLeaderReviewView>> LeaderQueue(CancellationToken ct)
        => leaderReviewService.QueueAsync(ct);

    [HttpGet("leader/requests/mine")]
    public Task<IReadOnlyList<V180OptionCLeaderReviewView>> LeaderMine(CancellationToken ct)
        => leaderReviewService.MineAsync(ct);

    [HttpPost("requests/{id:guid}/leader-review")]
    public Task<V180OptionCLeaderReviewView> LeaderReview(Guid id, V180OptionCLeaderReviewDecision input, CancellationToken ct)
        => leaderReviewService.DecideAsync(id, input, ct);

    [HttpGet("requests/mine")]
    public Task<IReadOnlyList<V180B3RequestView>> Mine(CancellationToken ct) => service.OwnAsync(ct);

    [HttpGet("requests/mine/search")]
    [Authorize(Roles="visitor,leader")]
    public Task<IReadOnlyList<V180B3RequestView>> MineSearch([FromQuery] DateOnly? start, [FromQuery] DateOnly? end, [FromQuery] string? q, [FromQuery] string? kind, [FromQuery] string? status, CancellationToken ct)
        => service.OwnAsync(ct, new(start,end,q,kind,status));

    [HttpGet("requests/mine.xlsx")]
    [Authorize(Roles="visitor,leader")]
    public async Task<IActionResult> MineExcel([FromQuery] DateOnly? start, [FromQuery] DateOnly? end, [FromQuery] string? q, [FromQuery] string? kind, [FromQuery] string? status, CancellationToken ct)
    {
        var filter=new V180OwnLocationRequestFilter(start,end,q,kind,status);
        var data=await service.OwnAsync(ct,filter);
        using var book=new XSSFWorkbook();var sheet=book.CreateSheet("我的地點申請");
        static void Write(ISheet sheet,int index,string[] values)
        {
            var row=sheet.CreateRow(index);for(var i=0;i<values.Length;i++)row.CreateCell(i,CellType.String).SetCellValue(values[i]);
        }
        static string Local(DateTime? dt)=>dt?.AddHours(8).ToString("yyyy/MM/dd HH:mm")??"";
        Write(sheet,0,["申請日期","地點名稱","申請類型","異動摘要","原始內容","申請內容","審核狀態","處理日期","審核意見"]);
        for(var i=0;i<data.Count;i++)
        {
            var r=data[i];var before=V180OwnLocationRequestFilter.Read(r.BeforeJson);var after=V180OwnLocationRequestFilter.Read(r.ProposedJson);
            var changed=V180OwnLocationRequestFilter.Fields.Where(k=>before[k]!=after[k]).ToArray();
            Write(sheet,i+1,[Local(r.SubmittedAt),V180OwnLocationRequestFilter.Name(r),V180OwnLocationRequestFilter.TypeLabel(r),
                string.Join("、",changed.Select(V180OwnLocationRequestFilter.Label)),
                string.Join("\n",changed.Select(k=>V180OwnLocationRequestFilter.Label(k)+"："+before[k])),
                string.Join("\n",changed.Select(k=>V180OwnLocationRequestFilter.Label(k)+"："+after[k])),
                r.Status switch{"Pending"=>"待審核","Applied"=>"已完成","Rejected"=>"已退回",_=>"狀態待查核"},Local(r.ReviewedAt),r.ReviewReason??""]);
        }
        sheet.CreateFreezePane(0,1);using var stream=new MemoryStream();book.Write(stream);var bytes=stream.ToArray();
        // Recheck the same live actor scope and versioned rows after generating the workbook.
        var current=await service.OwnAsync(ct,filter);
        if(!data.Select(r=>(r.RequestPublicId,r.RowVersion)).SequenceEqual(current.Select(r=>(r.RequestPublicId,r.RowVersion))))
            throw new InvalidOperationException("申請資料已變更，請重新查詢後下載。");
        return File(bytes,"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","我的地點申請.xlsx");
    }

    [HttpGet("requests/{id:guid}")]
    public Task<V180B3RequestView> Status(Guid id, CancellationToken ct)
        => service.StatusAsync(id, ct);

    [HttpGet("requests/queue")]
    public Task<IReadOnlyList<V180B3RequestView>> Queue(CancellationToken ct) => service.QueueAsync(ct);

    [HttpPost("requests/{id:guid}/approve")]
    public Task<V180B3RequestView> Approve(Guid id, V180B3Review input, CancellationToken ct)
        => service.ApproveAsync(id, input, ct);

    // Explicitly isolated candidate only: service checks candidate flag,
    // loopback SQL and live HR/Leader/Admin authority before publication.
    [HttpPost("requests/{id:guid}/publish-reviewed")]
    public Task<V180B3RequestView> PublishReviewed(
        Guid id, V180B3Review input, CancellationToken ct)
        => service.PublishReviewedAsync(id, input, ct);

    [HttpPost("requests/{id:guid}/reject")]
    public Task<V180B3RequestView> Reject(Guid id, V180B3Review input, CancellationToken ct)
        => service.RejectAsync(id, input, ct);

    [HttpGet("requests/{id:guid}/reconcile")]
    public Task<string> Reconcile(Guid id, [FromQuery] Guid decisionKey, CancellationToken ct)
        => service.ReconcileAsync(id, decisionKey, ct);
}
