using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Locked server-side inputs for the future B3 atomic apply path.  This type
/// is internal and is deliberately neither registered with DI nor reachable
/// from an HTTP action.  Its values must come from request-first / location-
/// second UPDLOCK,HOLDLOCK reads in the same serializable transaction.
/// </summary>
internal sealed record V180B3AtomicApplyLockedContext(
    int LocationId, int? LocationOrganizationId, int? LocationTeamId,
    int? LocationCreatedByUserId, string? LocationType,
    string? LocationApprovalStatus, bool LocationIsActive,
    byte[] LocationRowVersion, V180B3LocationFields LocationFields,
    int LiveReviewerUserId, int? LiveReviewerOrganizationId,
    bool LiveAdminAuthorized, bool SubmitterTeamMembershipStillValid,
    Guid SubmittedDecisionKey, string SubmittedReviewReason,
    bool DecisionKeyAlreadyExists);

/// <summary>
/// WORK-only, unregistered SQL writer for a future approved 1800_012 schema.
/// It cannot enable B3, open a connection, begin/commit a transaction, or
/// become an HTTP path.  The existing public ApproveAsync boundary remains
/// DENY ALL.  A caller facing an unknown COMMIT result must reconcile from a
/// fresh connection; it must never blindly retry this writer.
/// </summary>
internal static class V180B3AtomicApplyExecutor
{
    internal static async Task<Guid> ExecuteAsync(
        SqlConnection connection, SqlTransaction transaction,
        V180B3ApprovalApplyPlan plan, V180B3ChangeRequest lockedRequest,
        V180B3AtomicApplyLockedContext locked, CancellationToken ct)
    {
        if (connection.State != ConnectionState.Open
            || transaction.Connection != connection
            || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("B3_ATOMIC_TRANSACTION_REQUIRED");

        // This repeats the authorization/snapshot/replay boundary against
        // server-side locked values.  None of these flags may be client input.
        V180B3ApprovalApplyPreflight.RequireLockedApprovalBoundary(
            plan, lockedRequest, locked.LocationFields, locked.LocationRowVersion,
            locked.LocationId, locked.LocationOrganizationId,
            locked.LocationTeamId, locked.LocationCreatedByUserId,
            locked.LocationType, locked.LocationApprovalStatus,
            locked.LocationIsActive, locked.LiveReviewerUserId,
            locked.LiveReviewerOrganizationId, locked.LiveAdminAuthorized,
            locked.SubmitterTeamMembershipStillValid, locked.SubmittedDecisionKey,
            locked.SubmittedReviewReason, locked.DecisionKeyAlreadyExists);

        var proposed = JsonSerializer.Deserialize<V180B3LocationFields>(
            plan.ProposedJson) ?? throw new InvalidOperationException(
                "B3_APPROVAL_PLAN_INVALID");
        var locationRows = await WriteAsync(connection, transaction, """
            UPDATE dbo.Locations SET
                LocationName=@name,City=@city,District=@district,
                Address=@address,PlusCode=@plus,TaxId=@tax,MasterNote=@note
            WHERE LocationId=@id AND OrganizationId=@organization AND TeamId=@team
                AND CreatedByUserId=@requester AND LocationType=N'Customer'
                AND ApprovalStatus=N'Approved' AND IsActive=1
                AND RowVersion=@locationVersion
            """, ct,
            ("@name", proposed.LocationName), ("@city", proposed.City),
            ("@district", proposed.District), ("@address", proposed.Address),
            ("@plus", proposed.PlusCode), ("@tax", proposed.TaxId),
            ("@note", proposed.MasterNote), ("@id", locked.LocationId),
            ("@organization", lockedRequest.OrganizationId),
            ("@team", lockedRequest.TeamId),
            ("@requester", lockedRequest.RequestedByUserId),
            ("@locationVersion", locked.LocationRowVersion));
        V180B3ApprovalApplyPreflight.RequireExactlyOneGuardedSourceUpdate(
            plan, locationRows, locked.LocationId, locked.LocationRowVersion);

        var now = DateTime.UtcNow;
        var requestRows = await WriteAsync(connection, transaction, """
            UPDATE dbo.ChangeRequests SET Status=N'Applied',
                ReviewedByUserId=@reviewer,ReviewedAt=@now,AppliedAt=@now,
                ReviewReason=@reason
            WHERE ChangeRequestId=@id AND RequestPublicId=@publicId
                AND OrganizationId=@organization AND TeamId=@team
                AND RequestedByUserId=@requester AND Status=N'Pending'
                AND RowVersion=@requestVersion
            """, ct,
            ("@reviewer", locked.LiveReviewerUserId), ("@now", now),
            ("@reason", plan.ReviewReason), ("@id", lockedRequest.ChangeRequestId),
            ("@publicId", plan.RequestPublicId),
            ("@organization", lockedRequest.OrganizationId),
            ("@team", lockedRequest.TeamId),
            ("@requester", lockedRequest.RequestedByUserId),
            ("@requestVersion", lockedRequest.RowVersion));
        V180B3ApprovalApplyPreflight.RequireExactlyOneGuardedRequestTransition(
            plan, requestRows, lockedRequest, lockedRequest.RowVersion);

        var correlation = Guid.NewGuid();
        var approved = new V180B3ChangeEvent {
            ChangeRequestId = lockedRequest.ChangeRequestId, EventType = "Approved",
            ActorUserId = locked.LiveReviewerUserId, OccurredAt = now,
            CorrelationId = correlation, DecisionKey = plan.DecisionKey
        };
        var applied = new V180B3ChangeEvent {
            ChangeRequestId = lockedRequest.ChangeRequestId, EventType = "Applied",
            ActorUserId = locked.LiveReviewerUserId, OccurredAt = now,
            CorrelationId = correlation
        };
        V180B3ApprovalApplyPreflight.RequireAtomicAuditPair(
            plan, lockedRequest, approved, applied);
        await WriteAuditAsync(connection, transaction, approved, ct);
        await WriteAuditAsync(connection, transaction, applied, ct);
        return correlation;
    }

    private static async Task WriteAuditAsync(SqlConnection connection,
        SqlTransaction transaction, V180B3ChangeEvent audit,
        CancellationToken ct)
    {
        var rows = await WriteAsync(connection, transaction, """
            INSERT INTO dbo.ChangeRequestEvents(
                ChangeRequestId,EventType,ActorUserId,OccurredAt,
                CorrelationId,DecisionKey,DetailsJson)
            VALUES(@request,@type,@actor,@occurred,@correlation,@decision,NULL)
            """, ct, ("@request", audit.ChangeRequestId),
            ("@type", audit.EventType), ("@actor", audit.ActorUserId),
            ("@occurred", audit.OccurredAt), ("@correlation", audit.CorrelationId),
            ("@decision", audit.DecisionKey));
        if (rows != 1) throw new InvalidOperationException("B3_ATOMIC_AUDIT_WRITE_CONFLICT");
    }

    private static async Task<int> WriteAsync(SqlConnection connection,
        SqlTransaction transaction, string sql, CancellationToken ct,
        params (string Name, object? Value)[] values)
    {
        await using var command = new SqlCommand(sql, connection, transaction)
        { CommandTimeout = 20 };
        foreach (var (name, value) in values)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(ct);
    }
}
