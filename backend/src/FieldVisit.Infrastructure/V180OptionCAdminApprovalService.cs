using System.Data;
using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Server-owned authorization evidence used by the isolated Option C admin
/// candidate.  It is deliberately not a pair of caller-supplied booleans:
/// the service obtains this value from a provider while the decision
/// transaction is already holding the canonical request lock.
/// </summary>
internal sealed record V180OptionCLiveApprovalEvidence(
    bool RequesterEligible,
    bool IndependentAdminEligible,
    bool OrganizationMatches,
    bool TeamMatches);

internal interface IV180OptionCLiveApprovalEvidenceProvider
{
    Task<V180OptionCLiveApprovalEvidence> ReadAsync(
        SqlConnection connection, SqlTransaction transaction,
        long changeRequestId, int requesterUserId, int reviewerUserId,
        int organizationId, int? teamId, CancellationToken ct);
}

/// <summary>
/// Disposable-fixture provider.  The table is created only by the isolated
/// SQL harness; no production/UAT table or permission is introduced.  The
/// shape mirrors the authoritative facts the real HR/role/team adapter must
/// supply before a future deployment registration is considered.
/// </summary>
internal sealed class V180OptionCIsolatedApprovalEvidenceProvider
    : IV180OptionCLiveApprovalEvidenceProvider
{
    public async Task<V180OptionCLiveApprovalEvidence> ReadAsync(
        SqlConnection connection, SqlTransaction transaction,
        long changeRequestId, int requesterUserId, int reviewerUserId,
        int organizationId, int? teamId, CancellationToken ct)
    {
        const string sql = """
            SELECT
              CAST(CASE WHEN requester.IsActive=1
                    AND requester.IsRequesterEligible=1 THEN 1 ELSE 0 END AS bit),
              CAST(CASE WHEN reviewer.IsActive=1
                    AND reviewer.IsIndependentAdminEligible=1 THEN 1 ELSE 0 END AS bit),
              CAST(CASE WHEN requester.OrganizationId=@organization
                    AND reviewer.OrganizationId=@organization THEN 1 ELSE 0 END AS bit),
              CAST(CASE WHEN @team IS NOT NULL AND requester.TeamId=@team
                    AND reviewer.TeamId=@team THEN 1 ELSE 0 END AS bit)
            FROM dbo.OptionCLiveApprovalActors requester
            JOIN dbo.OptionCLiveApprovalActors reviewer
              ON reviewer.UserId=@reviewer
            WHERE requester.UserId=@requester;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@request", changeRequestId);
        command.Parameters.AddWithValue("@requester", requesterUserId);
        command.Parameters.AddWithValue("@reviewer", reviewerUserId);
        command.Parameters.AddWithValue("@organization", organizationId);
        command.Parameters.AddWithValue("@team", (object?)teamId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new UnauthorizedAccessException("OPTIONC_LIVE_AUTH_EVIDENCE_UNAVAILABLE");
        return new(
            reader.GetBoolean(0), reader.GetBoolean(1),
            reader.GetBoolean(2), reader.GetBoolean(3));
    }
}

/// <summary>
/// Unregistered Option C Admin Approve/Reject candidate.  It owns neither
/// the connection nor the commit: callers must provide a Serializable
/// transaction and must reconcile an ambiguous commit on a fresh connection.
/// No controller, DI registration, feature flag or UAT route references it.
/// </summary>
internal sealed class V180OptionCAdminApprovalService(
    IV180OptionCLiveApprovalEvidenceProvider evidenceProvider)
{
    internal async Task ApproveAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid requestPublicId, int organizationId, int reviewerUserId,
        Guid decisionKey, string reason, CancellationToken ct)
    {
        RequireTransaction(connection, transaction, requestPublicId,
            organizationId, reviewerUserId, decisionKey, reason);
        var request = await LockRequestAsync(connection, transaction,
            requestPublicId, organizationId, ct);
        var evidence = await evidenceProvider.ReadAsync(connection, transaction,
            request.Id, request.RequesterId, reviewerUserId, request.OrganizationId,
            request.TeamId, ct);
        RequireEvidence(request, reviewerUserId, evidence);
        await V180OptionCAtomicPublicationWriter.ExecuteAsync(
            connection, transaction, requestPublicId, organizationId,
            reviewerUserId, decisionKey, reason, evidence, ct);
    }

    internal async Task RejectAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid requestPublicId, int organizationId, int reviewerUserId,
        Guid decisionKey, string reason, CancellationToken ct)
    {
        RequireTransaction(connection, transaction, requestPublicId,
            organizationId, reviewerUserId, decisionKey, reason);
        var request = await LockRequestAsync(connection, transaction,
            requestPublicId, organizationId, ct);
        var evidence = await evidenceProvider.ReadAsync(connection, transaction,
            request.Id, request.RequesterId, reviewerUserId, request.OrganizationId,
            request.TeamId, ct);
        RequireEvidence(request, reviewerUserId, evidence);

        // Reserve DecisionKey globally in this SERIALIZABLE transaction.
        // The filtered unique index remains the final concurrency guard.
        await using (var replay = new SqlCommand("""
            SELECT COUNT_BIG(1)
            FROM dbo.ChangeRequestEvents WITH(UPDLOCK,HOLDLOCK)
            WHERE DecisionKey=@decision;
            """, connection, transaction))
        {
            replay.Parameters.AddWithValue("@decision", decisionKey);
            if (Convert.ToInt64(await replay.ExecuteScalarAsync(ct)) != 0)
                throw new UnauthorizedAccessException("OPTIONC_DECISION_REPLAY");
        }

        var now = DateTime.UtcNow;
        await using (var update = new SqlCommand("""
            UPDATE dbo.ChangeRequests SET Status=N'Rejected',
              ReviewedByUserId=@reviewer, ReviewedAt=@now, ReviewReason=@reason
            WHERE ChangeRequestId=@id AND RequestPublicId=@publicId
              AND RowVersion=@rowVersion AND Status=N'Pending'
              AND ReviewedByUserId IS NULL AND ReviewedAt IS NULL
              AND AppliedAt IS NULL AND RequestedByUserId<>@reviewer;
            SELECT CONVERT(int,@@ROWCOUNT);
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("@id", request.Id);
            update.Parameters.AddWithValue("@publicId", requestPublicId);
            update.Parameters.AddWithValue("@rowVersion", request.RowVersion);
            update.Parameters.AddWithValue("@reviewer", reviewerUserId);
            update.Parameters.AddWithValue("@now", now);
            update.Parameters.AddWithValue("@reason", reason);
            if (Convert.ToInt32(await update.ExecuteScalarAsync(ct)) != 1)
                throw new InvalidOperationException("OPTIONC_REJECT_UPDATE_CONFLICT");
        }

        // Reject and close any persisted, still-open Stage atomically with
        // the Request and canonical decision audit.
        await using (var stage = new SqlCommand("""
            UPDATE dbo.LocationPublicationStages SET
              StageStatus=N'Abandoned',UpdatedAt=@now
            WHERE ChangeRequestId=@id
              AND StageStatus IN(N'PendingGeocode',N'PendingDuplicateReview',N'ReadyForPublish');
            SELECT CONVERT(int,@@ROWCOUNT);
            """, connection, transaction))
        {
            stage.Parameters.AddWithValue("@id", request.Id);
            stage.Parameters.AddWithValue("@now", now);
            if (Convert.ToInt32(await stage.ExecuteScalarAsync(ct)) != 1)
                throw new InvalidOperationException("OPTIONC_REJECT_STAGE_CONFLICT");
        }

        await using var audit = new SqlCommand("""
            INSERT dbo.ChangeRequestEvents(ChangeRequestId,EventType,ActorUserId,
              OccurredAt,CorrelationId,DecisionKey,DetailsJson)
            VALUES(@id,N'Rejected',@reviewer,@now,NEWID(),@decision,
              JSON_QUERY(@details));
            """, connection, transaction);
        audit.Parameters.AddWithValue("@id", request.Id);
        audit.Parameters.AddWithValue("@reviewer", reviewerUserId);
        audit.Parameters.AddWithValue("@now", now);
        audit.Parameters.AddWithValue("@decision", decisionKey);
        audit.Parameters.AddWithValue("@details",
            System.Text.Json.JsonSerializer.Serialize(new { Reason = reason }));
        await audit.ExecuteNonQueryAsync(ct);
    }

    private static void RequireTransaction(SqlConnection connection,
        SqlTransaction transaction, Guid requestPublicId, int organizationId,
        int reviewerUserId, Guid decisionKey, string reason)
    {
        if (connection.State != ConnectionState.Open
            || transaction.Connection != connection
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || requestPublicId == Guid.Empty || organizationId <= 0
            || reviewerUserId <= 0 || decisionKey == Guid.Empty
            || string.IsNullOrWhiteSpace(reason) || reason != reason.Trim())
            throw new InvalidOperationException("OPTIONC_ADMIN_TRANSACTION_REQUIRED");
        V180OptionCExecutionPolicy.RequireIsolatedSqlTest(connection);
    }

    private static void RequireEvidence(LockedRequest request, int reviewer,
        V180OptionCLiveApprovalEvidence evidence)
    {
        if (request.RequesterId == reviewer
            || request.Status != "Pending"
            || !evidence.RequesterEligible
            || !evidence.IndependentAdminEligible
            || !evidence.OrganizationMatches || !evidence.TeamMatches)
            throw new UnauthorizedAccessException("OPTIONC_LIVE_ADMIN_AUTH_DENIED");
    }

    private static async Task<LockedRequest> LockRequestAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid requestPublicId, int organizationId, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT ChangeRequestId,RequestedByUserId,OrganizationId,TeamId,
              Status,RowVersion
            FROM dbo.ChangeRequests WITH(UPDLOCK,HOLDLOCK)
            WHERE RequestPublicId=@publicId AND OrganizationId=@organization
              AND EntityKind=N'Location'
              AND OperationCode=N'UpdatePublishedLocation'
              AND RiskCode=N'High';
            """, connection, transaction);
        command.Parameters.AddWithValue("@publicId", requestPublicId);
        command.Parameters.AddWithValue("@organization", organizationId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new KeyNotFoundException("OPTIONC_REQUEST_NOT_FOUND_OR_AMBIGUOUS");
        return new(reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.GetString(4),
            reader.GetFieldValue<byte[]>(5));
    }

    private sealed record LockedRequest(long Id, int RequesterId,
        int OrganizationId, int? TeamId, string Status, byte[] RowVersion);
}
