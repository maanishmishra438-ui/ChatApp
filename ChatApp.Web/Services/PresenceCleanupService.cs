using ChatApp.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Web.Services;

public sealed class PresenceCleanupService(
    IHubContext<ChatHub> hubContext)
    : BackgroundService
{
    private static readonly TimeSpan CleanupInterval =
        TimeSpan.FromSeconds(15);


    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        // Give the application a short moment to start.
        await Task.Delay(
            TimeSpan.FromSeconds(5),
            stoppingToken);


        using var timer =
            new PeriodicTimer(
                CleanupInterval);


        while (
            await timer.WaitForNextTickAsync(
                stoppingToken))
        {
            try
            {
                var offlineUsers =
                    ChatHub.RemoveStaleConnections();


                foreach (var user in offlineUsers)
                {
                    try
                    {
                        await hubContext.Clients
                            .Group(user.RoomCode)
                            .SendAsync(
                                "UserPresenceChanged",
                                user.UserName,
                                false,
                                stoppingToken);
                    }
                    catch
                    {
                        // A notification failure must not
                        // stop the cleanup service.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Keep the background service alive even if
                // one cleanup cycle encounters an unexpected error.
            }
        }
    }
}