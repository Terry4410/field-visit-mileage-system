using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Data;
using FieldVisit.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FieldVisit.Infrastructure;

public sealed class V180OptionCLeaderReview
{
    public long ChangeRequestLeaderReviewId { get; set; }
    public long ChangeRequestId { get; set; }
    public string ReviewStage { get; set; } = "LeaderFirstLevel";
    public string Status { get; set; } = "Pending";
    public int? ReviewerUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? Reason { get; set; }
    public Guid? DecisionKey { get; set; }
    public string? EvidenceJson { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed record V180OptionCLeaderReviewDecision(
    string RequestRowVersion, Guid DecisionKey, string Reason, bool Approve);

public sealed record V180OptionCLeaderReviewView(
    Guid RequestPublicId, string EntityId, int? TeamId, string OperationCode,
    string RiskCode, string RequestStatus, string LeaderStatus,
    int RequestedByUserId, DateTime SubmittedAt, string? BeforeJson,
    string ProposedJson, string RowVersion, string? Reason);

public sealed class V180OptionCLeaderReviewService(
    AppDbContext db, ICurrentUserService current, IConfiguration config)
{
    private async Task ReadyAsync(CancellationToken ct)
    {
        if (!config.GetValue<bool>("OptionC:Candidate:Enabled"))
            throw new InvalidOperationException("OPTIONC_CANDIDATE_DISABLED");
        var connection = db.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException("OPTIONC_SQLSERVER_REQUIRED");
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);
        V180OptionCExecutionPolicy.RequireIsolatedSqlTest(connection);
        await V180OptionCPublicationCatalogRules.RequireAsync(connection, ct);
        try
        {
            if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
            await using var command = new SqlCommand(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.ChangeRequestLeaderReviews',N'U') IS NULL THEN 0 ELSE 1 END",
                connection);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 1)
                throw new InvalidOperationException("OPTIONC_LEADER_REVIEW_SCHEMA_NOT_VERIFIED");
        }
        catch (SqlException ex) { throw new InvalidOperationException("OPTIONC_LEADER_REVIEW_SCHEMA_NOT_VERIFIED", ex); }
    }

    public async Task<IReadOnlyList<V180OptionCLeaderReviewView>> QueueAsync(CancellationToken ct)
    {
        await ReadyAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = current.GetRequired();
        var org = await new V180OptionCLiveAuthorizationAdapter(db)
            .ResolveLiveOrganizationAsync(actor.UserId, ct);
        var rows = await db.ChangeRequests.AsNoTracking()
            .Where(x => x.OrganizationId == org && x.EntityKind == "Location"
                && x.Status == "Pending" && x.TeamId != null)
            .OrderBy(x => x.SubmittedAt).Take(100).ToListAsync(ct);
        var result = new List<V180OptionCLeaderReviewView>();
        foreach (var row in rows)
        {
            try
            {
                await new V180OptionCLiveAuthorizationAdapter(db).AuthorizeLeaderReviewAsync(
                    row.RequestedByUserId, actor.UserId, row.OrganizationId, row.TeamId!.Value,
                    BusinessTime.Today, ct);
                var detail = await db.LeaderReviews.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.ChangeRequestId == row.ChangeRequestId
                        && x.ReviewStage == "LeaderFirstLevel", ct);
                if (detail is null || detail.Status == "Pending")
                    result.Add(ToView(row, detail));
            }
            catch (UnauthorizedAccessException) { /* scope is filtered, not disclosed */ }
        }
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<IReadOnlyList<V180OptionCLeaderReviewView>> MineAsync(CancellationToken ct)
    {
        await ReadyAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = current.GetRequired();
        var org = await new V180OptionCLiveAuthorizationAdapter(db)
            .ResolveLiveOrganizationAsync(actor.UserId, ct);
        var rows = await db.ChangeRequests.AsNoTracking()
            .Where(x => x.OrganizationId == org && x.RequestedByUserId == actor.UserId)
            .OrderByDescending(x => x.SubmittedAt).Take(100).ToListAsync(ct);
        var views = new List<V180OptionCLeaderReviewView>();
        foreach (var row in rows)
            views.Add(ToView(row, await db.LeaderReviews.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ChangeRequestId == row.ChangeRequestId
                    && x.ReviewStage == "LeaderFirstLevel", ct)));
        await tx.CommitAsync(ct);
        return views;
    }

    public async Task<V180OptionCLeaderReviewView> DecideAsync(
        Guid publicId, V180OptionCLeaderReviewDecision input, CancellationToken ct)
    {
        await ReadyAsync(ct);
        if (input.DecisionKey == Guid.Empty || string.IsNullOrWhiteSpace(input.Reason)
            || input.Reason.Length > 1000)
            throw new InvalidOperationException("OPTIONC_LEADER_REVIEW_INPUT_INVALID");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = current.GetRequired();
        var org = await new V180OptionCLiveAuthorizationAdapter(db)
            .ResolveLiveOrganizationAsync(actor.UserId, ct);
        var row = await db.ChangeRequests.FromSqlInterpolated(
            V180B3ApprovalLockSql.RequestByPublicId(publicId, org))
            .SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("OPTIONC_REQUEST_NOT_FOUND");
        if (row.Status != "Pending" || row.TeamId is not > 0
            || row.OrganizationId != org || row.EntityKind != "Location")
            throw new InvalidOperationException("OPTIONC_LEADER_REVIEW_NOT_PENDING");
        var expected = Convert.ToBase64String(row.RowVersion);
        if (!string.Equals(expected, input.RequestRowVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("ROWVERSION_CONFLICT");
        var auth = new V180OptionCLiveAuthorizationAdapter(db);
        var evidence = await auth.AuthorizeLeaderReviewAsync(
            row.RequestedByUserId, actor.UserId, row.OrganizationId, row.TeamId.Value,
            BusinessTime.Today, ct);
        if (await db.ChangeRequestEvents.AnyAsync(x => x.DecisionKey == input.DecisionKey, ct)
            || await db.LeaderReviews.AnyAsync(x => x.DecisionKey == input.DecisionKey, ct))
            throw new InvalidOperationException("OPTIONC_DECISION_KEY_REPLAY");
        var detail = await db.LeaderReviews.SingleOrDefaultAsync(
            x => x.ChangeRequestId == row.ChangeRequestId && x.ReviewStage == "LeaderFirstLevel", ct);
        if (detail is not null && detail.Status != "Pending")
            throw new InvalidOperationException("OPTIONC_LEADER_REVIEW_ALREADY_DECIDED");
        detail ??= new V180OptionCLeaderReview {
            ChangeRequestId = row.ChangeRequestId, ReviewStage = "LeaderFirstLevel" };
        detail.Status = input.Approve ? "Approved" : "Rejected";
        detail.ReviewerUserId = actor.UserId;
        detail.ReviewedAt = DateTime.UtcNow;
        detail.Reason = input.Reason;
        detail.DecisionKey = input.DecisionKey;
        detail.EvidenceJson = JsonSerializer.Serialize(evidence);
        if (detail.ChangeRequestLeaderReviewId == 0) db.LeaderReviews.Add(detail);
        if (!input.Approve)
        {
            // Canonical CK_B3_ChangeRequests_ReviewState requires a terminal
            // rejection's independent reviewer, timestamp and reason.
            // First-level approval leaves the request Pending for final review.
            row.Status = "Rejected";
            row.ReviewedByUserId = actor.UserId;
            row.ReviewedAt = detail.ReviewedAt;
            row.ReviewReason = input.Reason;
        }
        db.ChangeRequestEvents.Add(new V180B3ChangeEvent {
            ChangeRequestId = row.ChangeRequestId,
            EventType = input.Approve ? "LeaderApproved" : "LeaderRejected",
            ActorUserId = actor.UserId, OccurredAt = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid(), DecisionKey = input.DecisionKey,
            DetailsJson = JsonSerializer.Serialize(new {
                input.Reason, input.Approve, Stage = "LeaderFirstLevel",
                row.OperationCode, row.RiskCode, Evidence = evidence })
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) {
            V180B3SqlSafetyRules.RethrowRecognizedUniqueConflict(ex); throw;
        }
        if (!input.Approve)
        {
            // A Leader rejection is terminal. Abandon the private stage in
            // the SAME transaction; never leave a ReadyForPublish stage
            // attached to a rejected request.
            var sqlConnection = db.Database.GetDbConnection() as SqlConnection
                ?? throw new InvalidOperationException("OPTIONC_SQLSERVER_REQUIRED");
            var sqlTransaction = tx.GetDbTransaction() as SqlTransaction
                ?? throw new InvalidOperationException("OPTIONC_SQL_TRANSACTION_REQUIRED");
            await using (var stage = new SqlCommand("""
                UPDATE dbo.LocationPublicationStages
                SET StageStatus=N'Abandoned',UpdatedAt=SYSUTCDATETIME()
                WHERE ChangeRequestId=@request
                  AND StageStatus IN(N'PendingGeocode',N'PendingDuplicateReview',N'ReadyForPublish');
                SELECT CONVERT(int,@@ROWCOUNT);
                """, sqlConnection, sqlTransaction))
            {
                stage.Parameters.AddWithValue("@request", row.ChangeRequestId);
                if (Convert.ToInt32(await stage.ExecuteScalarAsync(ct)) != 1)
                    throw new InvalidOperationException("OPTIONC_LEADER_REJECT_STAGE_CONFLICT");
            }
            await using (var reservation = new SqlCommand("""
                IF OBJECT_ID(N'dbo.LocationCreateReservations',N'U') IS NOT NULL
                    UPDATE dbo.LocationCreateReservations
                    SET ReservationStatus=N'Abandoned',UpdatedAt=SYSUTCDATETIME()
                    WHERE ChangeRequestId=@request AND ReservationStatus=N'Pending';
                """, sqlConnection, sqlTransaction))
            {
                reservation.Parameters.AddWithValue("@request", row.ChangeRequestId);
                await reservation.ExecuteNonQueryAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
        return ToView(row, detail);
    }

    private static V180OptionCLeaderReviewView ToView(
        V180B3ChangeRequest row, V180OptionCLeaderReview? detail) =>
        new(row.RequestPublicId, row.EntityId, row.TeamId, row.OperationCode,
            row.RiskCode, row.Status, detail?.Status ?? "Pending",
            row.RequestedByUserId, row.SubmittedAt, row.BeforeJson, row.ProposedJson,
            Convert.ToBase64String(row.RowVersion), row.ReviewReason ?? detail?.Reason);

    internal static class Schema
    {
        internal static async Task<bool> ExistsAsync(SqlConnection connection,
            SqlTransaction transaction, CancellationToken ct)
        {
            await using var command = new SqlCommand(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.ChangeRequestLeaderReviews',N'U') IS NULL THEN 0 ELSE 1 END",
                connection, transaction);
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
        }

        internal static async Task RequireApprovedAsync(SqlConnection connection,
            SqlTransaction transaction, long requestId, int requesterId, CancellationToken ct)
        {
            await using var command = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.ChangeRequestLeaderReviews
                WHERE ChangeRequestId=@requestId AND ReviewStage=N'LeaderFirstLevel'
                  AND Status=N'Approved' AND ReviewerUserId IS NOT NULL
                  AND ReviewerUserId<>@requesterId
                """, connection, transaction);
            command.Parameters.AddWithValue("@requestId", requestId);
            command.Parameters.AddWithValue("@requesterId", requesterId);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 1)
                throw new InvalidOperationException("OPTIONC_LEADER_REVIEW_REQUIRED");
        }
    }
}
