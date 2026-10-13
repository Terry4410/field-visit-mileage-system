using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Read-only, isolated-only UNKNOWN COMMIT observation. It cannot retry,
/// commit, reverse, or declare a user-facing success. A fresh connection is
/// required after a failed commit acknowledgement.
/// </summary>
internal static class V180OptionCUnknownCommitReconciler
{
    internal static async Task<string> ObserveAsync(
        string connectionString, Guid requestPublicId, Guid decisionKey,
        int reviewerUserId, CancellationToken ct)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            { Pooling = false, ConnectTimeout = 5 };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(ct);
            try { V180OptionCExecutionPolicy.RequireIsolatedSqlTest(connection); }
            catch (InvalidOperationException) { return "INDETERMINATE"; }
            await using var cmd = new SqlCommand("""
                SELECT r.Status,r.ReviewedByUserId,
                    (SELECT COUNT(*) FROM dbo.ChangeRequestEvents e
                     WHERE e.ChangeRequestId=r.ChangeRequestId
                       AND e.EventType=N'Approved' AND e.DecisionKey=@decision
                       AND e.ActorUserId=@reviewer),
                    (SELECT COUNT(*) FROM dbo.ChangeRequestEvents e
                     WHERE e.ChangeRequestId=r.ChangeRequestId
                       AND e.EventType=N'Applied' AND e.DecisionKey IS NULL
                       AND e.ActorUserId=@reviewer),
                    (SELECT COUNT(*) FROM dbo.LocationPublishedVersions v
                     WHERE v.ChangeRequestId=r.ChangeRequestId AND v.IsCurrent=1),
                    (SELECT COUNT(*) FROM dbo.LocationPublishedVersions v
                     WHERE v.LocationId=TRY_CONVERT(int,r.EntityId) AND v.IsCurrent=1
                       AND v.VersionNumber>1),
                    (SELECT COUNT(*) FROM dbo.LocationPublicationStages s
                     WHERE s.ChangeRequestId=r.ChangeRequestId
                       AND s.StageStatus=N'Published')
                FROM dbo.ChangeRequests r WITH(HOLDLOCK)
                WHERE r.RequestPublicId=@request
                """, connection);
            cmd.Parameters.AddWithValue("@request", requestPublicId);
            cmd.Parameters.AddWithValue("@decision", decisionKey);
            cmd.Parameters.AddWithValue("@reviewer", reviewerUserId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return "INDETERMINATE";
            var status = reader.GetString(0);
            var reviewer = reader.IsDBNull(1) ? -1 : reader.GetInt32(1);
            var approved = reader.GetInt32(2);
            var applied = reader.GetInt32(3);
            var requestVersion = reader.GetInt32(4);
            var currentVersion = reader.GetInt32(5);
            var stage = reader.GetInt32(6);
            if (await reader.ReadAsync(ct)) return "INDETERMINATE";
            if (status == "Applied" && reviewer == reviewerUserId
                && approved == 1 && applied == 1 && requestVersion == 1
                && currentVersion == 1 && stage == 1)
                return "COMMITTED_EVIDENCE_REQUIRES_REVIEW";
            if (status == "Pending" && reviewer == -1
                && approved == 0 && applied == 0
                && requestVersion == 0 && currentVersion == 0 && stage == 0)
                return "NO_COMMIT_EVIDENCE_REQUIRES_REVIEW";
            return "INDETERMINATE";
        }
        catch
        {
            // Lost read access is itself an indeterminate result, never a
            // license to retry the original publication.
            return "INDETERMINATE";
        }
    }
}
