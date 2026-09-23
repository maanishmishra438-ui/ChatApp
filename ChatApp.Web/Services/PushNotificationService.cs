using System.Text.Json;
using ChatApp.Web.Data;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;

namespace ChatApp.Web.Services;

public sealed class PushNotificationService(
    IConfiguration configuration,
    ChatService chatService,
    ILogger<PushNotificationService> logger)
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
    // ============================================================

    public string PublicKey => publicKey;


    // ============================================================
    // SEND NEW MESSAGE NOTIFICATION
    // ============================================================

    public async Task SendNewMessageNotificationAsync(
        string roomCode,
        string sender,
        ChatMessage message)
    {
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            logger.LogWarning(
                "Push notification skipped: room code is empty.");

            return;
        }

        if (string.IsNullOrWhiteSpace(sender))
        {
            logger.LogWarning(
                "Push notification skipped: sender is empty.");

            return;
        }


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
            logger.LogDebug(
                "No push subscriptions found for room {RoomCode}.",
                roomCode);

            return;
        }


        // --------------------------------------------------------
        // CREATE PAYLOAD ONCE
        // --------------------------------------------------------

        var payload =
            JsonSerializer.Serialize(
                new
                {
                    title =
                        $"💬 {sender}",

                    body =
                        string.IsNullOrWhiteSpace(message.Text)
                            ? "New message"
                            : message.Text,

                    data =
                        new
                        {
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
                                action = "open",
                                title = "Open chat"
                            },

                            new
                            {
                                action = "like",
                                title = "❤️ Like"
                            }
                        }
                });


        // --------------------------------------------------------
        // CREATE PUSH MESSAGE ONCE
        // --------------------------------------------------------

        var pushMessage =
            new PushMessage(payload)
            {
                // Message remains valid for 5 minutes.
                // This is NOT a delivery delay.
                TimeToLive = 300
            };


        // --------------------------------------------------------
        // CREATE VAPID AUTHENTICATION ONCE
        // --------------------------------------------------------

        using var vapidAuthentication =
            new VapidAuthentication(
                publicKey,
                privateKey)
            {
                Subject = subject
            };


        // --------------------------------------------------------
        // SEND TO ALL DEVICES IN PARALLEL
        // --------------------------------------------------------

        var pushClient =
            new PushServiceClient();


        var pushTasks =
            subscriptions.Select(
                subscription =>
                    SendToSubscriptionAsync(
                        pushClient,
                        subscription,
                        pushMessage,
                        vapidAuthentication,
                        roomCode))
            .ToArray();


        await Task.WhenAll(pushTasks);


        logger.LogDebug(
            "Push notification processing completed for room {RoomCode}. Devices: {Count}",
            roomCode,
            subscriptions.Count);
    }


    // ============================================================
    // SEND TO ONE SUBSCRIPTION
    // ============================================================

    private async Task SendToSubscriptionAsync(
        PushServiceClient pushClient,
        dynamic subscription,
        PushMessage pushMessage,
        VapidAuthentication vapidAuthentication,
        string roomCode)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(
                    subscription.Endpoint))
            {
                logger.LogWarning(
                    "Skipping push subscription with empty endpoint for room {RoomCode}.",
                    roomCode);

                return;
            }


            // ----------------------------------------------------
            // CREATE BROWSER PUSH SUBSCRIPTION
            // ----------------------------------------------------

            var webPushSubscription =
                new Lib.Net.Http.WebPush.PushSubscription
                {
                    Endpoint =
                        subscription.Endpoint
                };


            // ----------------------------------------------------
            // BROWSER ENCRYPTION KEYS
            // ----------------------------------------------------

            webPushSubscription.Keys =
                new Dictionary<string, string>
                {
                    ["p256dh"] =
                        subscription.P256dh,

                    ["auth"] =
                        subscription.Auth
                };


            // ----------------------------------------------------
            // DELIVER PUSH
            // ----------------------------------------------------

            await pushClient
                .RequestPushMessageDeliveryAsync(
                    webPushSubscription,
                    pushMessage,
                    vapidAuthentication);


            logger.LogDebug(
                "Push notification sent successfully for room {RoomCode}.",
                roomCode);
        }
        catch (PushServiceClientException ex)
        {
            logger.LogWarning(
                ex,
                "Push provider rejected delivery for room {RoomCode}.",
                roomCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Unexpected push notification error for room {RoomCode}.",
                roomCode);
        }
    }
}