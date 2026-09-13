using System.Data;
using System.Net.Http;
using FieldVisit.Application;
using FieldVisit.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FieldVisit.Infrastructure;

public static class NotificationDeliveryPolicyAuthority
{
    public static NotificationDeliveryPolicyDecision Decide(
        string environmentCode,
        string? emailMode,
        bool isEnabled,
        string? senderIdentityReference,
        bool recipientEmailUsable,
        bool activeAllowlistMatch)
    {
        if (!string.Equals(emailMode, "Disabled", StringComparison.Ordinal)
            && !string.Equals(emailMode, "Test", StringComparison.Ordinal)
            && !string.Equals(emailMode, "Live", StringComparison.Ordinal))
            return NotificationDeliveryPolicyDecision.Cancel(NotificationDeliveryErrorCodes.PolicyInvalid);
        if (string.Equals(environmentCode, "UAT", StringComparison.OrdinalIgnoreCase)
            && string.Equals(emailMode, "Live", StringComparison.Ordinal))
            return NotificationDeliveryPolicyDecision.Cancel(NotificationDeliveryErrorCodes.UatLiveForbidden);
        if (!isEnabled || string.Equals(emailMode, "Disabled", StringComparison.Ordinal))
            return NotificationDeliveryPolicyDecision.Cancel(NotificationDeliveryErrorCodes.PolicyDisabled);
        if (!recipientEmailUsable)
            return NotificationDeliveryPolicyDecision.Cancel(NotificationDeliveryErrorCodes.PolicyInvalid);
        if (string.Equals(emailMode, "Test", StringComparison.Ordinal) && !activeAllowlistMatch)
            return NotificationDeliveryPolicyDecision.Cancel(NotificationDeliveryErrorCodes.TestRecipientNotAllowlisted);
        if (string.IsNullOrWhiteSpace(senderIdentityReference))
            return NotificationDeliveryPolicyDecision.Cancel(NotificationDeliveryErrorCodes.PolicyInvalid);
        return NotificationDeliveryPolicyDecision.Permit(senderIdentityReference?.Trim());
    }
}

public sealed class EfNotificationDeliveryPolicyEvaluator(AppDbContext db) : INotificationDeliveryPolicyEvaluator
{
    public async Task<NotificationDeliveryPolicyDecision> EvaluateAsync(
        NotificationDeliveryClaim claim,
        CancellationToken ct)
    {
        var policy = await db.Set<NotificationEnvironmentPolicy>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.EnvironmentCode == claim.EnvironmentCode, ct);
        if (policy is null)
            return NotificationDeliveryPolicyDecision.Cancel(NotificationDeliveryErrorCodes.PolicyMissing);

        var normalizedEmail = NotificationEmailAuthority.NormalizeUsable(claim.RecipientEmail);
        var allowlisted = false;
        if (string.Equals(policy.EmailMode, "Test", StringComparison.Ordinal) && normalizedEmail is not null)
        {
            allowlisted = await db.Set<NotificationEmailAllowlist>().AsNoTracking()
                .AnyAsync(x => x.EnvironmentCode == claim.EnvironmentCode
                               && x.IsActive
                               && x.NormalizedEmail == normalizedEmail, ct);
        }

        return NotificationDeliveryPolicyAuthority.Decide(
            policy.EnvironmentCode,
            policy.EmailMode,
            policy.IsEnabled,
            policy.SenderIdentityReference,
            normalizedEmail is not null,
            allowlisted);
    }
}

public sealed class UnconfiguredNotificationEmailProvider : INotificationEmailProvider
{
    public string ProviderName => "Unconfigured";

    public Task<NotificationProviderResult> SendAsync(NotificationProviderRequest request, CancellationToken ct)
        => Task.FromResult(NotificationProviderResult.Permanent(
            NotificationDeliveryErrorCodes.ProviderNotConfigured,
            "Notification email provider is not configured."));
}

public sealed class NotificationDeliveryProcessor(
    INotificationDeliveryStore store,
    INotificationDeliveryPolicyEvaluator policyEvaluator,
    INotificationEmailProvider provider,
    TimeProvider timeProvider,
    NotificationDeliveryRuntimeOptions options) : INotificationDeliveryProcessor
{
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        options.Validate();
        var claim = await store.TryClaimNextAsync(UtcNow(), ct);
        if (claim is null) return false;

        var policy = await policyEvaluator.EvaluateAsync(claim, ct);
        if (!policy.IsPermitted)
        {
            await store.TryCancelAsync(
                claim.MailOutboxId,
                claim.ProcessingToken,
                UtcNow(),
                policy.CancellationCode ?? NotificationDeliveryErrorCodes.PolicyInvalid,
                ct);
            return true;
        }

        if (claim.AttemptCount >= NotificationDeliveryAuthority.MaxAttempts)
        {
            await store.TryFailExhaustedAsync(claim.MailOutboxId, claim.ProcessingToken, UtcNow(), ct);
            return true;
        }

        var reservation = await store.TryReserveAttemptAsync(
            claim.MailOutboxId,
            claim.ProcessingToken,
            claim.AttemptCount,
            UtcNow(),
            ct);
        if (reservation is null) return true;

        if (!await store.OwnsReservationAsync(reservation, UtcNow(), ct))
            return true;

        NotificationProviderResult result;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.ProviderTimeout);
        try
        {
            result = await provider.SendAsync(
                new NotificationProviderRequest(
                    claim.MailOutboxId,
                    claim.RecipientEmail!,
                    claim.TemplateCode,
                    claim.TemplateDataJson,
                    policy.SenderIdentityReference,
                    NotificationDeliveryAuthority.ProviderIdempotencyKey(claim.MailOutboxId),
                    reservation.AttemptNumber,
                    claim.CorrelationId),
                timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            result = NotificationProviderResult.Retryable(
                NotificationDeliveryErrorCodes.ProviderTimeout,
                "Notification provider timed out.");
        }
        catch (HttpRequestException)
        {
            result = NotificationProviderResult.Retryable(
                NotificationDeliveryErrorCodes.ProviderNetwork,
                "Notification provider network failure.");
        }
        catch (Exception)
        {
            result = NotificationProviderResult.Retryable(
                NotificationDeliveryErrorCodes.ProviderUnexpected,
                "Notification provider returned an unexpected failure.");
        }

        var sanitized = result.Outcome == NotificationProviderOutcome.Sent
            ? NotificationProviderResult.Success(
                NotificationDeliveryAuthority.SanitizeProviderMessageId(result.ProviderMessageId))
            : new NotificationProviderResult(
                result.Outcome,
                null,
                NotificationDeliveryAuthority.SanitizeErrorCode(
                    result.ErrorCode,
                    NotificationDeliveryErrorCodes.ProviderUnexpected),
                NotificationDeliveryAuthority.SanitizeMessage(result.ErrorMessage));

        await store.TryRecordResultAsync(
            new NotificationDeliveryCompletion(
                claim.MailOutboxId,
                claim.ProcessingToken,
                reservation.AttemptNumber,
                UtcNow(),
                NotificationDeliveryAuthority.SanitizeProviderName(provider.ProviderName),
                sanitized),
            ct);
        return true;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}

public sealed class EfNotificationDeliveryStore(AppDbContext db) : INotificationDeliveryStore
{
    public async Task<NotificationDeliveryClaim?> TryClaimNextAsync(DateTime now, CancellationToken ct)
    {
        var token = Guid.NewGuid();
        var leaseUntil = now.Add(NotificationDeliveryAuthority.LeaseDuration);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var recovered = await ClaimQuery(token).SingleOrDefaultAsync(ct);
            if (recovered is not null) return recovered;

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var command = db.Database.GetDbConnection().CreateCommand();
            await using (command)
            {
                command.Transaction = tx.GetDbTransaction();
                command.CommandText = """
                    SET NOCOUNT ON;
                    DECLARE @MailOutboxId bigint;
                    SELECT TOP (1) @MailOutboxId = MailOutboxId
                    FROM dbo.MailOutbox WITH (UPDLOCK, READPAST, READCOMMITTEDLOCK, ROWLOCK)
                    WHERE (Status = N'Pending' AND AvailableAt <= @Now)
                       OR (Status = N'Processing' AND ProcessingLeaseUntil <= @Now)
                    ORDER BY
                        CASE WHEN Status = N'Processing' THEN 0 ELSE 1 END,
                        CASE WHEN Status = N'Processing' THEN ProcessingLeaseUntil ELSE AvailableAt END,
                        MailOutboxId;

                    IF @MailOutboxId IS NOT NULL
                    BEGIN
                        UPDATE dbo.MailOutbox
                        SET Status = N'Processing', ProcessingToken = @Token,
                            ProcessingLeaseUntil = @LeaseUntil, FinalizedAt = NULL
                        WHERE MailOutboxId = @MailOutboxId;
                    END;
                    SELECT @MailOutboxId;
                    """;
                command.Parameters.Add(DateTimeParameter("@Now", now));
                command.Parameters.Add(new SqlParameter("@Token", SqlDbType.UniqueIdentifier) { Value = token });
                command.Parameters.Add(DateTimeParameter("@LeaseUntil", leaseUntil));
                var scalar = await command.ExecuteScalarAsync(ct);
                if (scalar is null or DBNull)
                {
                    await tx.CommitAsync(ct);
                    return null;
                }
            }

            var claim = await ClaimQuery(token).SingleAsync(ct);
            await tx.CommitAsync(ct);
            return claim;
        });
    }

    public Task<bool> TryCancelAsync(
        long mailOutboxId,
        Guid processingToken,
        DateTime now,
        string reasonCode,
        CancellationToken ct)
    {
        var code = FixedCode(reasonCode);
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            var changed = await db.Set<MailOutbox>()
                .Where(x => x.MailOutboxId == mailOutboxId
                            && x.Status == "Processing"
                            && x.ProcessingToken == processingToken
                            && x.ProcessingLeaseUntil > now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, "Cancelled")
                    .SetProperty(x => x.ProcessingToken, (Guid?)null)
                    .SetProperty(x => x.ProcessingLeaseUntil, (DateTime?)null)
                    .SetProperty(x => x.FinalizedAt, (DateTime?)now)
                    .SetProperty(x => x.LastErrorCode, code)
                    .SetProperty(x => x.LastErrorMessage, (string?)null), ct);
            if (changed == 1) return true;
            return await db.Set<MailOutbox>().AsNoTracking().AnyAsync(
                x => x.MailOutboxId == mailOutboxId
                     && x.Status == "Cancelled"
                     && x.LastErrorCode == code,
                ct);
        });
    }

    public Task<bool> TryFailExhaustedAsync(
        long mailOutboxId,
        Guid processingToken,
        DateTime now,
        CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            var changed = await db.Set<MailOutbox>()
                .Where(x => x.MailOutboxId == mailOutboxId
                            && x.Status == "Processing"
                            && x.ProcessingToken == processingToken
                            && x.ProcessingLeaseUntil > now
                            && x.AttemptCount >= NotificationDeliveryAuthority.MaxAttempts)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, "Failed")
                    .SetProperty(x => x.ProcessingToken, (Guid?)null)
                    .SetProperty(x => x.ProcessingLeaseUntil, (DateTime?)null)
                    .SetProperty(x => x.FinalizedAt, (DateTime?)now)
                    .SetProperty(x => x.LastErrorCode, NotificationDeliveryErrorCodes.AttemptsExhausted)
                    .SetProperty(x => x.LastErrorMessage, (string?)null), ct);
            if (changed == 1) return true;
            return await db.Set<MailOutbox>().AsNoTracking().AnyAsync(
                x => x.MailOutboxId == mailOutboxId
                     && x.Status == "Failed"
                     && x.LastErrorCode == NotificationDeliveryErrorCodes.AttemptsExhausted,
                ct);
        });
    }

    public async Task<NotificationAttemptReservation?> TryReserveAttemptAsync(
        long mailOutboxId,
        Guid processingToken,
        int expectedAttemptCount,
        DateTime now,
        CancellationToken ct)
    {
        if (expectedAttemptCount < 0 || expectedAttemptCount >= NotificationDeliveryAuthority.MaxAttempts)
            return null;
        var attemptNumber = expectedAttemptCount + 1;
        var leaseUntil = now.Add(NotificationDeliveryAuthority.LeaseDuration);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var alreadyReserved = await db.Set<MailOutbox>().AsNoTracking().AnyAsync(
                x => x.MailOutboxId == mailOutboxId
                     && x.Status == "Processing"
                     && x.ProcessingToken == processingToken
                     && x.ProcessingLeaseUntil > now
                     && x.AttemptCount == attemptNumber,
                ct);
            if (alreadyReserved)
                return new NotificationAttemptReservation(mailOutboxId, processingToken, attemptNumber, leaseUntil);

            var connection = db.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose) await db.Database.OpenConnectionAsync(ct);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
                        UPDATE dbo.MailOutbox
                        SET AttemptCount = @AttemptNumber, ProcessingLeaseUntil = @LeaseUntil
                        OUTPUT inserted.AttemptCount
                        WHERE MailOutboxId = @MailOutboxId
                          AND Status = N'Processing'
                          AND ProcessingToken = @Token
                          AND ProcessingLeaseUntil > @Now
                          AND AttemptCount = @ExpectedAttemptCount
                          AND AttemptCount < 3;
                        """;
                command.Parameters.Add(new SqlParameter("@AttemptNumber", SqlDbType.Int) { Value = attemptNumber });
                command.Parameters.Add(DateTimeParameter("@LeaseUntil", leaseUntil));
                command.Parameters.Add(new SqlParameter("@MailOutboxId", SqlDbType.BigInt) { Value = mailOutboxId });
                command.Parameters.Add(new SqlParameter("@Token", SqlDbType.UniqueIdentifier) { Value = processingToken });
                command.Parameters.Add(DateTimeParameter("@Now", now));
                command.Parameters.Add(new SqlParameter("@ExpectedAttemptCount", SqlDbType.Int) { Value = expectedAttemptCount });
                var result = await command.ExecuteScalarAsync(ct);
                return result is null or DBNull
                    ? null
                    : new NotificationAttemptReservation(mailOutboxId, processingToken, Convert.ToInt32(result), leaseUntil);
            }
            finally
            {
                if (shouldClose) await db.Database.CloseConnectionAsync();
            }
        });
    }

    public Task<bool> OwnsReservationAsync(
        NotificationAttemptReservation reservation,
        DateTime now,
        CancellationToken ct)
        => db.Set<MailOutbox>().AsNoTracking().AnyAsync(
            x => x.MailOutboxId == reservation.MailOutboxId
                 && x.Status == "Processing"
                 && x.ProcessingToken == reservation.ProcessingToken
                 && x.ProcessingLeaseUntil > now
                 && x.AttemptCount == reservation.AttemptNumber,
            ct);

    public async Task<bool> TryRecordResultAsync(NotificationDeliveryCompletion completion, CancellationToken ct)
    {
        if (completion.AttemptNumber is < 1 or > NotificationDeliveryAuthority.MaxAttempts)
            throw new ArgumentOutOfRangeException(nameof(completion.AttemptNumber));

        var sent = completion.ProviderResult.Outcome == NotificationProviderOutcome.Sent;
        var retryable = completion.ProviderResult.Outcome == NotificationProviderOutcome.RetryableFailure
                        && completion.AttemptNumber < NotificationDeliveryAuthority.MaxAttempts;
        var status = sent ? "Sent" : retryable ? "Pending" : "Failed";
        var availableAt = retryable
            ? completion.CompletedAt.Add(NotificationDeliveryAuthority.RetryDelay(completion.AttemptNumber))
            : completion.CompletedAt;
        var sentAt = sent ? (DateTime?)completion.CompletedAt : null;
        var finalizedAt = retryable ? null : (DateTime?)completion.CompletedAt;
        var errorCode = sent
            ? null
            : NotificationDeliveryAuthority.SanitizeErrorCode(
                completion.ProviderResult.ErrorCode,
                NotificationDeliveryErrorCodes.ProviderUnexpected);
        var errorMessage = sent
            ? null
            : NotificationDeliveryAuthority.SanitizeMessage(completion.ProviderResult.ErrorMessage);
        var providerMessageId = sent
            ? NotificationDeliveryAuthority.SanitizeProviderMessageId(completion.ProviderResult.ProviderMessageId)
            : null;
        var providerName = NotificationDeliveryAuthority.SanitizeProviderName(completion.Provider);
        var logStatus = sent ? "Sent" : "Failed";
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            if (await db.Set<MailDeliveryLog>().AsNoTracking().AnyAsync(
                    x => x.MailOutboxId == completion.MailOutboxId
                         && x.AttemptNumber == completion.AttemptNumber,
                    ct))
                return true;

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var changed = await db.Set<MailOutbox>()
                    .Where(x => x.MailOutboxId == completion.MailOutboxId
                                && x.Status == "Processing"
                                && x.ProcessingToken == completion.ProcessingToken
                                && x.ProcessingLeaseUntil > completion.CompletedAt
                                && x.AttemptCount == completion.AttemptNumber)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Status, status)
                        .SetProperty(x => x.AvailableAt, availableAt)
                        .SetProperty(x => x.ProcessingToken, (Guid?)null)
                        .SetProperty(x => x.ProcessingLeaseUntil, (DateTime?)null)
                        .SetProperty(x => x.SentAt, sentAt)
                        .SetProperty(x => x.FinalizedAt, finalizedAt)
                        .SetProperty(x => x.LastErrorCode, errorCode)
                        .SetProperty(x => x.LastErrorMessage, errorMessage), ct);
                if (changed != 1)
                {
                    await tx.RollbackAsync(ct);
                    return false;
                }

                db.Set<MailDeliveryLog>().Add(new MailDeliveryLog
                {
                    MailOutboxId = completion.MailOutboxId,
                    AttemptNumber = completion.AttemptNumber,
                    Provider = providerName,
                    Status = logStatus,
                    ProviderMessageId = providerMessageId,
                    AttemptedAt = completion.CompletedAt,
                    ErrorCode = errorCode,
                    ErrorMessage = errorMessage
                });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                throw;
            }
        });
    }

    private IQueryable<NotificationDeliveryClaim> ClaimQuery(Guid token)
        => db.Set<MailOutbox>().AsNoTracking()
            .Where(x => x.Status == "Processing" && x.ProcessingToken == token)
            .Select(x => new NotificationDeliveryClaim(
                x.MailOutboxId,
                x.EnvironmentCode,
                x.EventCode,
                x.BusinessEventKey,
                x.EventOccurredAt,
                x.AggregateType,
                x.AggregateId,
                x.RecipientKey,
                x.RecipientEmail,
                x.TemplateCode,
                x.TemplateDataJson,
                x.CorrelationId,
                x.AttemptCount,
                x.ProcessingToken!.Value,
                x.ProcessingLeaseUntil!.Value));

    private static SqlParameter DateTimeParameter(string name, DateTime value)
        => new(name, SqlDbType.DateTime2) { Scale = 3, Value = value };

    private static string FixedCode(string value)
    {
        var allowed = new[]
        {
            NotificationDeliveryErrorCodes.PolicyMissing,
            NotificationDeliveryErrorCodes.PolicyInvalid,
            NotificationDeliveryErrorCodes.PolicyDisabled,
            NotificationDeliveryErrorCodes.TestRecipientNotAllowlisted,
            NotificationDeliveryErrorCodes.UatLiveForbidden
        };
        return allowed.Contains(value, StringComparer.Ordinal)
            ? value
            : NotificationDeliveryErrorCodes.PolicyInvalid;
    }
}
