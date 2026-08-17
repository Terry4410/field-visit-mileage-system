using FieldVisit.Application;

namespace FieldVisit.Api;

public sealed class BackgroundJobHostedService(
    IServiceScopeFactory scopeFactory,
    IBackgroundJobSignal signal,
    IConfiguration configuration,
    ILogger<BackgroundJobHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var enabled =
            configuration.GetValue(
                "BackgroundJobs:WorkerEnabled",
                true);

        if (!enabled)
        {
            logger.LogInformation(
                "Background job worker is disabled.");

            return;
        }

        var recoverOnStartup =
            configuration.GetValue(
                "BackgroundJobs:RecoverOnStartup",
                true);

        if (recoverOnStartup)
        {
            await DrainWaitingJobsAsync(
                "startup-recovery",
                stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await signal.WaitAsync(
                    stoppingToken);

                await DrainWaitingJobsAsync(
                    "job-signal",
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task DrainWaitingJobsAsync(
        string reason,
        CancellationToken ct)
    {
        var processedCount = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope =
                    scopeFactory.CreateScope();

                var jobs =
                    scope.ServiceProvider
                        .GetRequiredService<
                            IBackgroundJobService>();

                var processed =
                    await jobs.ProcessNextAsync(ct);

                if (!processed)
                {
                    if (processedCount > 0)
                    {
                        logger.LogInformation(
                            "Background job worker drained {Count} job(s). Reason={Reason}",
                            processedCount,
                            reason);
                    }

                    return;
                }

                processedCount++;
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Background job worker failed during {Reason}. Worker will wait for the next signal instead of polling.",
                    reason);

                return;
            }
        }
    }
}
