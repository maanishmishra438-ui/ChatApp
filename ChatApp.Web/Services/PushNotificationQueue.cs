using System.Threading.Channels;
using ChatApp.Web.Data;

namespace ChatApp.Web.Services;

public sealed record PushNotificationJob(
    string RoomCode,
    string Sender,
    ChatMessage Message);

public sealed class PushNotificationQueue
{
    private readonly Channel<PushNotificationJob> queue =
        Channel.CreateUnbounded<PushNotificationJob>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

    public ValueTask EnqueueAsync(
        PushNotificationJob job)
    {
        return queue.Writer.WriteAsync(job);
    }

    public IAsyncEnumerable<PushNotificationJob> ReadAllAsync(
        CancellationToken cancellationToken)
    {
        return queue.Reader.ReadAllAsync(cancellationToken);
    }
}