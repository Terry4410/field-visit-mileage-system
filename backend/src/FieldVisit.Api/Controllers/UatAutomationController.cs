using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FieldVisit.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Api.Controllers;

public static class UatAutomationSafety
{
    public const string PurposePrefix = "UAT-AUTO-";

    public static bool IsEligibleEnvironment(string? authMode, string? appVersion) =>
        string.Equals(authMode?.Trim(), "Demo", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(appVersion)
        && appVersion.Contains("-uat-candidate", StringComparison.OrdinalIgnoreCase);

    public static bool IsExactAutomationPurpose(string? actualPurpose, string? expectedPurpose) =>
        !string.IsNullOrWhiteSpace(actualPurpose)
        && !string.IsNullOrWhiteSpace(expectedPurpose)
        && expectedPurpose.StartsWith(PurposePrefix, StringComparison.Ordinal)
        && string.Equals(actualPurpose, expectedPurpose, StringComparison.Ordinal);

    public static bool KeyMatches(string? supplied, string? configured)
    {
        if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(configured))
            return false;

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, configuredHash);
    }

    public static bool IsDedicatedMileageJob(string? jobType, string? mode, string? payloadJson, long tripId)
    {
        if (!string.Equals(jobType, "Mileage", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(mode, "Selected", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(payloadJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("selectedTripIds", out var ids)
                || ids.ValueKind != JsonValueKind.Array
                || ids.GetArrayLength() != 1)
                return false;

            return ids[0].TryGetInt64(out var selected) && selected == tripId;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record UatAutomationCleanupRequest(
    long VisitTripId,
    string ExpectedPurpose,
    Guid? BackgroundJobId = null);

public sealed record UatAutomationCleanupResult(
    long VisitTripId,
    int CorrectionRequestsDeleted,
    int SnapshotsDeleted,
    int BackgroundJobsDeleted,
    int TripsDeleted);

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/v1/uat-automation")]
public sealed class UatAutomationController(
    AppDbContext db,
    IConfiguration config) : ControllerBase
{
    [HttpPost("cleanup-trip")]
    public async Task<ActionResult<UatAutomationCleanupResult>> CleanupTrip(
        UatAutomationCleanupRequest request,
        CancellationToken ct)
    {
        if (!UatAutomationSafety.IsEligibleEnvironment(
                config["Auth:Mode"],
                config["App:Version"]))
            return NotFound();

        var suppliedKey = Request.Headers["X-UAT-Automation-Key"].FirstOrDefault();
        if (!UatAutomationSafety.KeyMatches(suppliedKey, config["Auth:DemoPassword"]))
            return Forbid();

        if (request.VisitTripId <= 0
            || string.IsNullOrWhiteSpace(request.ExpectedPurpose)
            || !request.ExpectedPurpose.StartsWith(
                UatAutomationSafety.PurposePrefix,
                StringComparison.Ordinal))
            return UnprocessableEntity(new { detail = "UAT cleanup request 不符合安全格式。" });

        var trip = await db.VisitTrips
            .AsNoTracking()
            .Where(x => x.VisitTripId == request.VisitTripId)
            .Select(x => new { x.VisitTripId, x.Purpose })
            .SingleOrDefaultAsync(ct);

        if (trip is null)
            return NotFound();

        if (!UatAutomationSafety.IsExactAutomationPurpose(
                trip.Purpose,
                request.ExpectedPurpose))
            return UnprocessableEntity(new { detail = "只允許清除 Purpose 精確符合 UAT-AUTO- 標記的測試行程。" });

        var tripIdText = request.VisitTripId.ToString(CultureInfo.InvariantCulture);

        if (request.BackgroundJobId.HasValue)
        {
            var job = await db.BackgroundJobs
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.BackgroundJobId == request.BackgroundJobId.Value,
                    ct);

            if (job is not null
                && !UatAutomationSafety.IsDedicatedMileageJob(
                    job.JobType,
                    job.Mode,
                    job.PayloadJson,
                    request.VisitTripId))
                return UnprocessableEntity(new { detail = "指定的背景工作不是此測試行程專用的 Selected Mileage Job。" });
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var correctionIds = await db.CorrectionRequests
            .Where(x => x.VisitTripId == request.VisitTripId)
            .Select(x => x.CorrectionRequestId)
            .ToListAsync(ct);

        if (correctionIds.Count > 0)
        {
            await db.CorrectionRequestChanges
                .Where(x => correctionIds.Contains(x.CorrectionRequestId))
                .ExecuteDeleteAsync(ct);
        }

        var correctionCount = await db.CorrectionRequests
            .Where(x => x.VisitTripId == request.VisitTripId)
            .ExecuteDeleteAsync(ct);

        var snapshotIds = await db.VisitTripSnapshots
            .Where(x => x.VisitTripId == request.VisitTripId)
            .Select(x => x.VisitTripSnapshotId)
            .ToListAsync(ct);

        if (snapshotIds.Count > 0)
        {
            await db.VisitTripSnapshotStops
                .Where(x => snapshotIds.Contains(x.VisitTripSnapshotId))
                .ExecuteDeleteAsync(ct);
        }

        var snapshotCount = await db.VisitTripSnapshots
            .Where(x => x.VisitTripId == request.VisitTripId)
            .ExecuteDeleteAsync(ct);

        await db.ApprovalRecords
            .Where(x => x.VisitTripId == request.VisitTripId)
            .ExecuteDeleteAsync(ct);

        await db.VisitTripStatusHistories
            .Where(x => x.VisitTripId == request.VisitTripId)
            .ExecuteDeleteAsync(ct);

        var backgroundJobsDeleted = 0;
        if (request.BackgroundJobId.HasValue)
        {
            var jobId = request.BackgroundJobId.Value;
            await db.BackgroundJobItems
                .Where(x => x.BackgroundJobId == jobId)
                .ExecuteDeleteAsync(ct);

            await db.AuditLogs
                .Where(x => x.EntityType == "BackgroundJob" && x.EntityId == jobId.ToString())
                .ExecuteDeleteAsync(ct);

            backgroundJobsDeleted = await db.BackgroundJobs
                .Where(x => x.BackgroundJobId == jobId)
                .ExecuteDeleteAsync(ct);
        }
        else
        {
            await db.BackgroundJobItems
                .Where(x => x.EntityType == "VisitTrip" && x.EntityId == tripIdText)
                .ExecuteDeleteAsync(ct);
        }

        await db.AuditLogs
            .Where(x => x.EntityType == "Trip" && x.EntityId == tripIdText)
            .ExecuteDeleteAsync(ct);

        var tripCount = await db.VisitTrips
            .Where(x => x.VisitTripId == request.VisitTripId)
            .ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);

        return Ok(new UatAutomationCleanupResult(
            request.VisitTripId,
            correctionCount,
            snapshotCount,
            backgroundJobsDeleted,
            tripCount));
    }
}
