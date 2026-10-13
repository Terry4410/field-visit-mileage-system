using System.Data;
using System.IO;
using FieldVisit.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Unregistered, isolated-only review orchestration candidate.
/// Derives BOTH live privilege decisions server-side from the same EF context
/// and Serializable transaction as the publication. No route, DI registration,
/// feature activation, or UAT migration. Do not use as production authority.
/// </summary>
internal sealed class V180OptionCAtomicPublicationOrchestrator(
    AppDbContext db, ICurrentUserService current)
{
    internal async Task ExecuteAsync(Guid requestPublicId,
        Guid decisionKey, string reason, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException("OPTIONC_SQLSERVER_REQUIRED");
        if (!connection.Database.StartsWith("OptionCIsolated_", StringComparison.Ordinal))
            throw new InvalidOperationException("OPTIONC_ISOLATED_ONLY");

        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        try
        {
            var (reviewer, admin, _) =
                await V180B3LiveAuthorizationRead.EvaluateActorAsync(
                    db, current.GetRequired(), BusinessTime.Today, ct);
            if (!admin || reviewer.OrganizationId is null or <= 0)
                throw new UnauthorizedAccessException("OPTIONC_ADMIN_REVOKED");

            // Canonical request FIRST. Do not trust a team or requester
            // supplied by the caller. Revalidate requester HR + live role,
            // dated team appointment and active membership within this tx.
            var request = await db.ChangeRequests.FromSqlInterpolated(
                V180B3ApprovalLockSql.RequestByPublicId(
                    requestPublicId, reviewer.OrganizationId.Value))
                .AsNoTracking().SingleOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("OPTIONC_REQUEST_NOT_FOUND");
            if (request.Status != "Pending" || request.TeamId is null or <= 0
                || request.RequestedByUserId == reviewer.UserId
                || request.EntityKind != "Location"
                || request.OperationCode != "UpdatePublishedLocation"
                || request.RiskCode != "High"
                || request.OrganizationId != reviewer.OrganizationId.Value)
                throw new UnauthorizedAccessException("OPTIONC_REQUEST_SCOPE_DENIED");

            var requesterValid =
                await V180B3LiveAuthorizationRead.IsSubmitterTeamStillValidAsync(
                    db, request.RequestedByUserId, request.OrganizationId,
                    request.TeamId.Value, BusinessTime.Today, ct);
            if (!requesterValid)
                throw new UnauthorizedAccessException("OPTIONC_REQUESTER_REVOKED");

            var sqlTx = tx.GetDbTransaction() as SqlTransaction
                ?? throw new InvalidOperationException("OPTIONC_SQL_TRANSACTION_REQUIRED");
            await V180OptionCAtomicPublicationWriter.ExecuteAsync(
                connection, sqlTx, requestPublicId, request.OrganizationId,
                reviewer.UserId, decisionKey, reason, requesterValid, admin, ct);
            // A failed/unknown commit acknowledgement is NEVER retried here.
            // Operator must reconcile on a NEW connection before any action.
            try
            {
                await tx.CommitAsync(ct);
            }
            catch (Exception commitError) when (commitError is SqlException
                or TimeoutException or OperationCanceledException or IOException)
            {
                // Unknown terminal outcome is never a safe reason to retry.
                // Observe on a fresh connection; even complete evidence is
                // reported as UNKNOWN to the caller for operator reconciliation.
                var status = await V180OptionCUnknownCommitReconciler.ObserveAsync(
                    connection.ConnectionString, requestPublicId, decisionKey,
                    reviewer.UserId, CancellationToken.None);
                throw new InvalidOperationException(
                    "OPTIONC_UNKNOWN_COMMIT_" + status, commitError);
            }
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch { /* Preserve original failure; no blind retry. */ }
            throw;
        }
    }
}
