using System.Text.Json;
using ChatApp.Web.Data;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;

namespace ChatApp.Web.Services;

public sealed class PushNotificationService(
    IConfiguration configuration,
    ChatService chatService)
{
    // ============================================================
    // VAPID CONFIGURATION
    // ============================================================

    private readonly string publicKey =
        configuration["PushServiceClient:PublicKey"]
        ?? throw new InvalidOperationException(
            "PushServiceClient:PublicKey is missing.");

    private readonly string privateKey =
        configuration["PushServiceClient:PrivateKey"]
        ?? throw new InvalidOperationException(
            "PushServiceClient:PrivateKey is missing.");

    private readonly string subject =
        configuration["PushServiceClient:Subject"]
        ?? throw new InvalidOperationException(
            "PushServiceClient:Subject is missing.");


    // ============================================================
    // PUBLIC VAPID KEY
    // USED BY BROWSER
    // ============================================================

    public string PublicKey =>
        publicKey;


    // ============================================================
    // SEND NEW MESSAGE NOTIFICATION
    // ============================================================

    public async Task SendNewMessageNotificationAsync(
        string roomCode,
        string sender,
        ChatMessage message)
    {
        // --------------------------------------------------------
        // GET SUBSCRIBED DEVICES
        // EXCLUDE THE SENDER
        // --------------------------------------------------------

        var subscriptions =
            await chatService.GetPushSubscriptionsAsync(
                roomCode,
                sender);


        if (subscriptions.Count == 0)
        {
            return;
        }


        // --------------------------------------------------------
        // CREATE PUSH CLIENT
        // --------------------------------------------------------

        var pushClient =
            new PushServiceClient();


        // --------------------------------------------------------
        // CREATE VAPID AUTHENTICATION
        // --------------------------------------------------------

        using var vapidAuthentication =
            new VapidAuthentication(
                publicKey,
                privateKey)
            {
                Subject = subject
            };


        // --------------------------------------------------------
        // SEND TO EVERY SUBSCRIBED DEVICE
        // --------------------------------------------------------

        foreach (var subscription in subscriptions)
        {
            try
            {
                // =================================================
                // NOTIFICATION PAYLOAD
                // =================================================

                var payload =
                    JsonSerializer.Serialize(
                        new
                        {
                            title =
                                $"💬 {sender}",

                            body =
                                string.IsNullOrWhiteSpace(
                                    message.Text)
                                    ? "New message"
                                    : message.Text,

                            data =
                                new
                                {
                                    roomCode =
                                        roomCode,

                                    messageId =
                                        message.Id,

                                    userName =
                                        sender
                                },

                            actions =
                                new[]
                                {
                                    new
                                    {
                                        action =
                                            "open",

                                        title =
                                            "Open chat"
                                    },

                                    new
                                    {
                                        action =
                                            "like",

                                        title =
                                            "❤️ Like"
                                    }
                                }
                        });


                // =================================================
                // PUSH MESSAGE
                // =================================================

                var pushMessage =
                    new PushMessage(payload)
                    {
                        TimeToLive = 300
                    };


                // =================================================
                // BROWSER PUSH SUBSCRIPTION
                // =================================================

                var webPushSubscription =
                    new Lib.Net.Http.WebPush.PushSubscription
                    {
                        Endpoint =
                            subscription.Endpoint
                    };


                // Browser encryption keys.
                webPushSubscription.Keys =
                    new Dictionary<string, string>
                    {
                        ["p256dh"] =
                            subscription.P256dh,

                        ["auth"] =
                            subscription.Auth
                    };


                // =================================================
                // DELIVER PUSH
                // =================================================

                await pushClient
                    .RequestPushMessageDeliveryAsync(
                        webPushSubscription,
                        pushMessage,
                        vapidAuthentication);
            }
            catch (PushServiceClientException)
            {
                // Individual push failure should not
                // break normal chat messaging.
            }
            catch
            {
                // Keep chat working even if one
                // push subscription is invalid.
            }
        }
    }
}