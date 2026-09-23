using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatApp.Web.Services;

public sealed class PushNotificationWorker(
    PushNotificationQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PushNotificationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await foreach (
            var job in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope =
                    scopeFactory.CreateScope();

                var pushService =
                    scope.ServiceProvider
                        .GetRequiredService<PushNotificationService>();

                await pushService
                    .SendNewMessageNotificationAsync(
                        job.RoomCode,
                        job.Sender,
                        job.Message);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Background push notification failed for room {RoomCode}.",
                    job.RoomCode);
            }
        }
    }
}