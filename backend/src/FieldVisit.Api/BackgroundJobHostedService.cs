using FieldVisit.Application;

namespace FieldVisit.Api;

public sealed class BackgroundJobHostedService(IServiceScopeFactory scopeFactory, ILogger<BackgroundJobHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobService>();
                var processed = await jobs.ProcessNextAsync(stoppingToken);
                await Task.Delay(processed ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background job worker failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
