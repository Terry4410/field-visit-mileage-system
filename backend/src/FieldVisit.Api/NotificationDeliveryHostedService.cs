using FieldVisit.Application;

namespace FieldVisit.Api;

public sealed class NotificationDeliveryHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<NotificationDeliveryHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Notifications:Delivery:WorkerEnabled", false))
        {
            logger.LogInformation("Notification delivery worker is disabled.");
            return;
        }

        var pollSeconds = Math.Clamp(
            configuration.GetValue("Notifications:Delivery:PollIntervalSeconds", 10),
            1,
            300);
        var batchSize = Math.Clamp(
            configuration.GetValue("Notifications:Delivery:BatchSize", 10),
            1,
            100);
        var pollInterval = TimeSpan.FromSeconds(pollSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = 0;
                while (processed < batchSize && !stoppingToken.IsCancellationRequested)
                {
                    using var scope = scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<INotificationDeliveryProcessor>();
                    if (!await processor.ProcessNextAsync(stoppingToken)) break;
                    processed++;
                }

                if (processed == 0)
                    await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification delivery worker cycle failed; durable leases will permit safe recovery.");
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
    }
}
