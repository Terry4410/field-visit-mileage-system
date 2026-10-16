using System.Data;
using System.Text.Json;
using FieldVisit.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FieldVisit.Infrastructure;

/// <summary>
/// The only possible outcomes after a COMMIT acknowledgement failure.  This
/// is deliberately an observation, not a licence to retry or return success.
/// </summary>
internal enum V180B3UnknownCommitObservation
{
    CommittedEvidenceComplete,
    NoCommitEvidenceComplete,
    IndeterminateRequiresInvestigation
}

internal sealed class V180B3UnknownCommitException(
    V180B3UnknownCommitObservation observation, Exception inner)
    : InvalidOperationException("B3_UNKNOWN_COMMIT_" + observation, inner)
{
    internal V180B3UnknownCommitObservation Observation { get; } = observation;
}

/// <summary>
/// WORK-only orchestration candidate for an approved 1800_012 catalog.
/// This class is internal, unregistered and has no controller call site.
/// Public ApproveAsync remains the production/UAT DENY ALL boundary.  It is
/// intentionally unavailable unless the caller has independently arranged
/// schema 012; this class never runs a migration or enables a feature flag.
/// </summary>
internal sealed class V180B3AtomicApplyOrchestrator(
    AppDbContext db, ICurrentUserService current)
{
    internal async Task<Guid> ExecuteAsync(
        Guid id, V180B3Review input, CancellationToken ct)
    {
        V180B3RequestInputRules.RequireReviewTarget(id, input);
        await Require012CatalogAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        try
        {
            // Re-read the role/HR status inside the serializable transaction.
            var (reviewer, admin, _) = await V180B3LiveAuthorizationRead
                .EvaluateActorAsync(db, current.GetRequired(), BusinessTime.Today, ct);
            if (!admin || !reviewer.OrganizationId.HasValue)
                throw new UnauthorizedAccessException("B3_ADMIN_REQUIRED");

            // All decision paths lock the request first, then the Location.
            var request = await db.ChangeRequests.FromSqlInterpolated(
                V180B3ApprovalLockSql.RequestByPublicId(id,
                    reviewer.OrganizationId.Value)).SingleOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("B3_REQUEST_NOT_FOUND");
            V180B3ReviewTargetRules.RequireSupportedTarget(
                request, reviewer.OrganizationId.Value, id);
            if (!int.TryParse(request.EntityId, out var locationId) || locationId <= 0)
                throw new UnauthorizedAccessException("B3_REVIEW_TARGET_UNSUPPORTED");
            var location = await db.Locations.FromSqlInterpolated(
                V180B3ApprovalLockSql.LocationById(locationId,
                    reviewer.OrganizationId.Value)).SingleOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("B3_LOCATION_NOT_FOUND");

            var membership = request.TeamId.HasValue
                && await V180B3LiveAuthorizationRead.IsVisitorTeamValidAsync(db,
                    request.RequestedByUserId, request.OrganizationId,
                    request.TeamId.Value, BusinessTime.Today, ct);
            var fields = new V180B3LocationFields(location.LocationName,
                location.City, location.District, location.Address, location.PlusCode,
                location.TaxId, location.MasterNote);
            var plan = V180B3ApprovalApplyPreflight.PrepareImmutablePlan(request,
                reviewer.OrganizationId.Value, id, reviewer.UserId, input,
                location.LocationId, location.OrganizationId, location.TeamId,
                location.CreatedByUserId, location.LocationType,
                location.ApprovalStatus, location.IsActive, location.RowVersion, fields);
            var replay = await db.ChangeRequestEvents.AsNoTracking().AnyAsync(
                x => x.DecisionKey == input.DecisionKey, ct);

            // The writer repeats this boundary immediately before DML.  The
            // context is composed exclusively from locked DB rows/live reads.
            var connection = db.Database.GetDbConnection() as SqlConnection
                ?? throw new InvalidOperationException("B3_SQLSERVER_REQUIRED");
            var sqlTransaction = transaction.GetDbTransaction() as SqlTransaction
                ?? throw new InvalidOperationException("B3_SQLSERVER_REQUIRED");
            var correlation = await V180B3AtomicApplyExecutor.ExecuteAsync(
                connection, sqlTransaction, plan, request,
                new V180B3AtomicApplyLockedContext(location.LocationId,
                    location.OrganizationId, location.TeamId, location.CreatedByUserId,
                    location.LocationType, location.ApprovalStatus, location.IsActive,
                    location.RowVersion, fields, reviewer.UserId,
                    reviewer.OrganizationId, admin, membership, input.DecisionKey,
                    input.Reason, replay), ct);
            try
            {
                await transaction.CommitAsync(ct);
                return correlation;
            }
            catch (Exception commitError) when (IsUnknownCommitCandidate(commitError))
            {
                // A NEW connection is mandatory: the transaction above has an
                // unknown terminal state.  Never invoke ExecuteAsync again.
                var observation = await ObserveUnknownCommitAsync(
                    connection.ConnectionString, plan, request, reviewer.UserId, ct);
                throw new V180B3UnknownCommitException(observation, commitError);
            }
        }
        catch
        {
            // Rollback is best-effort only when COMMIT did not acknowledge.
            // No catch path performs a blind decision/apply retry.
            try
            {
                if (transaction.GetDbTransaction().Connection is not null)
                    await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the original write/unknown-COMMIT failure.  The
                // caller must use the attached observation, never a retry.
            }
            throw;
        }
    }

    private async Task Require012CatalogAsync(CancellationToken ct)
    {
        try
        {
            var latest = await db.Database.SqlQueryRaw<string>(
                V180B3SqlSafetyRules.LatestSchemaVersionSql).FirstOrDefaultAsync(ct);
            if (!string.Equals(latest, "1.8.0-012", StringComparison.Ordinal)
                || await db.Database.SqlQueryRaw<int>("""
                    SELECT CAST(CASE WHEN
                      EXISTS(SELECT 1 FROM sys.check_constraints
                        WHERE parent_object_id=OBJECT_ID(N'dbo.ChangeRequests')
                          AND name=N'CK_B3_ChangeRequests_KnownStatus'
                          AND is_disabled=0 AND is_not_trusted=0
                          AND LOWER(definition) LIKE N'%applied%')
                      AND EXISTS(SELECT 1 FROM sys.check_constraints
                        WHERE parent_object_id=OBJECT_ID(N'dbo.ChangeRequestEvents')
                          AND name=N'CK_B3_ChangeRequestEvents_EventType'
                          AND is_disabled=0 AND is_not_trusted=0
                          AND LOWER(definition) LIKE N'%approved%'
                          AND LOWER(definition) LIKE N'%applied%')
                    THEN 1 ELSE 0 END AS int) AS Value
                    """).SingleAsync(ct) != 1)
                throw new InvalidOperationException("B3_012_SCHEMA_NOT_VERIFIED");
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException("B3_012_SCHEMA_NOT_VERIFIED", ex);
        }
    }

    private static bool IsUnknownCommitCandidate(Exception error) =>
        error is SqlException or TimeoutException or OperationCanceledException;

    private static async Task<V180B3UnknownCommitObservation>
        ObserveUnknownCommitAsync(string connectionString,
            V180B3ApprovalApplyPlan plan, V180B3ChangeRequest request,
            int reviewerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return V180B3UnknownCommitObservation.IndeterminateRequiresInvestigation;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        try
        {
            await using var row = new SqlCommand("""
                SELECT r.Status,r.ReviewedByUserId,r.ReviewReason,r.RowVersion,
                    l.RowVersion,l.LocationName,l.City,l.District,l.Address,
                    l.PlusCode,l.TaxId,l.MasterNote
                FROM dbo.ChangeRequests r JOIN dbo.Locations l
                    ON l.LocationId=@location
                WHERE r.RequestPublicId=@request AND r.ChangeRequestId=@requestId
                  AND r.OrganizationId=@organization
                """, connection, tx);
            row.Parameters.AddWithValue("@location", plan.LocationId);
            row.Parameters.AddWithValue("@request", plan.RequestPublicId);
            row.Parameters.AddWithValue("@requestId", request.ChangeRequestId);
            row.Parameters.AddWithValue("@organization", request.OrganizationId);
            string? status = null, reason = null;
            int? reviewer = null;
            byte[]? requestVersion = null, locationVersion = null;
            V180B3LocationFields? fields = null;
            await using (var reader = await row.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    return V180B3UnknownCommitObservation.IndeterminateRequiresInvestigation;
                status = reader.GetString(0);
                reviewer = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                reason = reader.IsDBNull(2) ? null : reader.GetString(2);
                requestVersion = reader.GetFieldValue<byte[]>(3);
                locationVersion = reader.GetFieldValue<byte[]>(4);
                string? F(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
                fields = new V180B3LocationFields(reader.GetString(5), F(6), F(7),
                    F(8), F(9), F(10), F(11));
                if (await reader.ReadAsync(ct))
                    return V180B3UnknownCommitObservation.IndeterminateRequiresInvestigation;
            }
            var auditPair = await ScalarIntAsync(connection, tx, """
                SELECT COUNT(*) FROM dbo.ChangeRequestEvents a
                JOIN dbo.ChangeRequestEvents b ON b.ChangeRequestId=a.ChangeRequestId
                    AND b.CorrelationId=a.CorrelationId
                WHERE a.ChangeRequestId=@id AND a.EventType=N'Approved'
                  AND a.ActorUserId=@reviewer AND a.DecisionKey=@key
                  AND b.EventType=N'Applied' AND b.ActorUserId=@reviewer
                  AND b.DecisionKey IS NULL
                """, ct, ("@id", request.ChangeRequestId),
                ("@reviewer", reviewerUserId), ("@key", plan.DecisionKey));
            var allAudit = await ScalarIntAsync(connection, tx,
                "SELECT COUNT(*) FROM dbo.ChangeRequestEvents WHERE ChangeRequestId=@id",
                ct, ("@id", request.ChangeRequestId));
            var proposed = JsonSerializer.Deserialize<V180B3LocationFields>(plan.ProposedJson);
            if (status == "Applied" && reviewer == reviewerUserId
                && reason == plan.ReviewReason && requestVersion is { Length: 8 }
                && locationVersion is { Length: 8 }
                && !requestVersion.SequenceEqual(plan.ExpectedRequestRowVersion)
                && !locationVersion.SequenceEqual(plan.ExpectedLocationRowVersion)
                && fields == proposed && auditPair == 1 && allAudit == 2)
                return V180B3UnknownCommitObservation.CommittedEvidenceComplete;
            if (status == "Pending" && reviewer is null && reason is null
                && requestVersion is { Length: 8 }
                && locationVersion is { Length: 8 }
                && requestVersion.SequenceEqual(plan.ExpectedRequestRowVersion)
                && locationVersion.SequenceEqual(plan.ExpectedLocationRowVersion)
                && allAudit == 0)
                return V180B3UnknownCommitObservation.NoCommitEvidenceComplete;
            return V180B3UnknownCommitObservation.IndeterminateRequiresInvestigation;
        }
        finally
        {
            await tx.RollbackAsync(CancellationToken.None);
        }
    }

    private static async Task<int> ScalarIntAsync(SqlConnection connection,
        SqlTransaction transaction, string sql, CancellationToken ct,
        params (string Name, object Value)[] values)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }
}
