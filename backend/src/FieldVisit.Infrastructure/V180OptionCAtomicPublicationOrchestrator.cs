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
        V180OptionCExecutionPolicy.RequireIsolatedSqlTest(connection);
        // Database naming is not schema evidence. Verify every required
        // Stage/Ledger object, column, unique index and trusted constraint
        // before opening the write transaction.
        await V180OptionCPublicationCatalogRules.RequireAsync(connection, ct);

        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        try
        {
            var actorId = current.GetRequired().UserId;
            var authorization = new V180OptionCLiveAuthorizationAdapter(db);
            var organizationId = await authorization.ResolveLiveOrganizationAsync(actorId, ct);

            // Canonical request FIRST. Do not trust a team or requester
            // supplied by the caller. Revalidate requester HR + live role,
            // dated team appointment and active membership within this tx.
            var request = await db.ChangeRequests.FromSqlInterpolated(
                V180B3ApprovalLockSql.RequestByPublicId(
                    requestPublicId, organizationId))
                .AsNoTracking().SingleOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("OPTIONC_REQUEST_NOT_FOUND");
            if (request.Status != "Pending" || request.TeamId is null or <= 0
                || request.RequestedByUserId == actorId
                || request.EntityKind != "Location"
                || request.OperationCode != "UpdatePublishedLocation"
                || request.RiskCode != "High"
                || request.OrganizationId != organizationId)
                throw new UnauthorizedAccessException("OPTIONC_REQUEST_SCOPE_DENIED");

            var liveEvidence = await authorization.AuthorizeDecisionAsync(
                request.RequestedByUserId, actorId, request.OrganizationId,
                request.TeamId.Value, BusinessTime.Today, ct);

            var sqlTx = tx.GetDbTransaction() as SqlTransaction
                ?? throw new InvalidOperationException("OPTIONC_SQL_TRANSACTION_REQUIRED");
            var evidence = new V180OptionCLiveApprovalEvidence(
                liveEvidence.RequesterEligible,
                liveEvidence.IndependentAdminEligible,
                liveEvidence.OrganizationMatches,
                liveEvidence.TeamMatches);
            await V180OptionCAtomicPublicationWriter.ExecuteAsync(
                connection, sqlTx, requestPublicId, request.OrganizationId,
                actorId, decisionKey, reason, evidence, ct);
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
                    actorId, CancellationToken.None);
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
