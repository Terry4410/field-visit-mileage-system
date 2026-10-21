using System.Data;
using System.IO;
using FieldVisit.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FieldVisit.Infrastructure;

/// <summary>
/// Isolated candidate only. A fresh live HR/role/team decision is obtained in
/// the SAME Serializable transaction as the canonical publication writer.
/// No UAT/production route is enabled without OptionC:Candidate:Enabled and
/// the isolated loopback SQL execution policy.
/// </summary>
internal sealed class V180OptionCCreateLowRiskPublicationOrchestrator(
    AppDbContext db, ICurrentUserService current)
{
    internal async Task ExecuteAsync(Guid requestPublicId, Guid decisionKey,
        string reason, string requestRowVersion, CancellationToken ct)
    {
        if (requestPublicId == Guid.Empty || decisionKey == Guid.Empty
            || string.IsNullOrWhiteSpace(reason) || reason != reason.Trim()
            || reason.Length > 1000)
            throw new InvalidOperationException("OPTIONC_CREATE_LOW_REVIEW_INPUT_INVALID");
        var expectedRequestVersion = V180B3RowVersionRules.Parse(requestRowVersion);
        var connection = db.Database.GetDbConnection() as SqlConnection
            ?? throw new InvalidOperationException("OPTIONC_SQLSERVER_REQUIRED");
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);
        V180OptionCExecutionPolicy.RequireExecutionTarget(connection);
        await V180OptionCPublicationCatalogRules.RequireAsync(connection, ct);
        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);
        try
        {
            var actorId = current.GetRequired().UserId;
            var auth = new V180OptionCLiveAuthorizationAdapter(db);
            var org = await auth.ResolveLiveOrganizationAsync(actorId, ct);
            var request = await db.ChangeRequests.FromSqlInterpolated(
                V180B3ApprovalLockSql.RequestByPublicId(requestPublicId, org))
                .AsNoTracking().SingleOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("OPTIONC_REQUEST_NOT_FOUND");
            if (!request.RowVersion.SequenceEqual(expectedRequestVersion))
                throw new InvalidOperationException("ROWVERSION_CONFLICT");
            if (request.TeamId is not int team || team <= 0
                || request.RequestedByUserId == actorId
                || request.EntityKind != "Location" || request.Status != "Pending"
                || !((request.OperationCode == "CreateLocation" && request.RiskCode == "High")
                  || (request.OperationCode == "UpdatePublishedLocation"
                      && request.RiskCode == "Low")))
                throw new UnauthorizedAccessException("OPTIONC_CREATE_LOW_SCOPE_DENIED");

            var leader = await db.LeaderReviews.AsNoTracking().SingleOrDefaultAsync(
                x => x.ChangeRequestId == request.ChangeRequestId
                    && x.ReviewStage == "LeaderFirstLevel", ct);
            if (leader?.Status != "Approved" || leader.ReviewerUserId is not int leaderId
                || leaderId == request.RequestedByUserId || leader.DecisionKey is null)
                throw new UnauthorizedAccessException("OPTIONC_CREATE_LOW_LEADER_REQUIRED");

            // Revalidate the original Leader's appointment, role, HR and
            // requester eligibility at the moment of publication, not only
            // when the historical LeaderApproved event was written.
            var liveLeader = await auth.AuthorizeLeaderReviewAsync(
                request.RequestedByUserId, leaderId, org, team,
                BusinessTime.Today, ct);
            if (!liveLeader.RequesterEligible || !liveLeader.ReviewerLeaderEligible
                || !liveLeader.OrganizationMatches || !liveLeader.TeamMatches)
                throw new UnauthorizedAccessException("OPTIONC_CREATE_LOW_LEADER_REVOKED");

            bool adminEligible = false;
            if (request.OperationCode == "CreateLocation")
            {
                if (leaderId == actorId)
                    throw new UnauthorizedAccessException("OPTIONC_CREATE_ADMIN_INDEPENDENCE_DENIED");
                var admin = await auth.AuthorizeDecisionAsync(
                    request.RequestedByUserId, actorId, org, team,
                    BusinessTime.Today, ct);
                adminEligible = admin.RequesterEligible && admin.IndependentAdminEligible
                    && admin.OrganizationMatches && admin.TeamMatches;
                if (!adminEligible)
                    throw new UnauthorizedAccessException("OPTIONC_CREATE_ADMIN_REVOKED");
            }
            else if (leaderId != actorId)
                throw new UnauthorizedAccessException("OPTIONC_LOW_LEADER_ACTOR_MISMATCH");

            var sqlTx = tx.GetDbTransaction() as SqlTransaction
                ?? throw new InvalidOperationException("OPTIONC_SQL_TRANSACTION_REQUIRED");
            await V180OptionCCreateLowRiskPublicationWriter.ExecuteAsync(
                connection, sqlTx, requestPublicId, org, actorId, decisionKey,
                reason, transactionBoundLiveAuthorization: true,
                independentAdminForCreate: adminEligible, ct);
            try { await tx.CommitAsync(ct); }
            catch (Exception commitError) when (commitError is SqlException
                or TimeoutException or OperationCanceledException or IOException)
            {
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
            catch { /* No blind retry after an ambiguous COMMIT. */ }
            throw;
        }
    }
}
