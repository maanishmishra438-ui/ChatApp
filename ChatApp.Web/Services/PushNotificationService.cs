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
        try
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
            // CREATE PAYLOAD
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
            // CREATE PUSH MESSAGE
            // --------------------------------------------------------

            var pushMessage =
                new PushMessage(payload)
                {
                    // Push remains valid for 5 minutes.
                    // This does NOT delay delivery.
                    TimeToLive = 300
                };


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
            // CREATE PUSH CLIENT
            // --------------------------------------------------------

            var pushClient =
                new PushServiceClient();


            // --------------------------------------------------------
            // SEND TO ALL DEVICES
            // --------------------------------------------------------

            var pushTasks =
                subscriptions
                    .Select(
                        subscription =>
                            SendToSubscriptionAsync(
                                pushClient,
                                subscription,
                                pushMessage,
                                vapidAuthentication,
                                roomCode))
                    .ToArray();


            try
            {
                await Task.WhenAll(pushTasks);
            }
            catch (OperationCanceledException ex)
            {
                // A cancelled push request must NEVER
                // affect normal chat/photo functionality.

                logger.LogWarning(
                    ex,
                    "Push notification request was cancelled for room {RoomCode}.",
                    roomCode);
            }
            catch (Exception ex)
            {
                // Push failure must NEVER break chat/photo sending.

                logger.LogWarning(
                    ex,
                    "Push notification processing failed for room {RoomCode}.",
                    roomCode);
            }


            logger.LogDebug(
                "Push notification processing completed for room {RoomCode}. Devices: {Count}",
                roomCode,
                subscriptions.Count);
        }
        catch (OperationCanceledException ex)
        {
            // --------------------------------------------------------
            // IMPORTANT:
            // Push cancellation must never escape this service.
            // --------------------------------------------------------

            logger.LogWarning(
                ex,
                "Push notification operation was cancelled for room {RoomCode}.",
                roomCode);
        }
        catch (Exception ex)
        {
            // --------------------------------------------------------
            // IMPORTANT:
            // Push failure must never break normal chat.
            // --------------------------------------------------------

            logger.LogWarning(
                ex,
                "Push notification service failed for room {RoomCode}.",
                roomCode);
        }
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
            if (subscription is null)
            {
                logger.LogWarning(
                    "Skipping null push subscription for room {RoomCode}.",
                    roomCode);

                return;
            }


            // --------------------------------------------------------
            // VALIDATE ENDPOINT
            // --------------------------------------------------------

            if (string.IsNullOrWhiteSpace(
                    subscription.Endpoint))
            {
                logger.LogWarning(
                    "Skipping push subscription with empty endpoint for room {RoomCode}.",
                    roomCode);

                return;
            }


            // --------------------------------------------------------
            // VALIDATE ENCRYPTION KEYS
            // --------------------------------------------------------

            if (string.IsNullOrWhiteSpace(
                    subscription.P256dh))
            {
                logger.LogWarning(
                    "Skipping push subscription with empty P256dh key for room {RoomCode}.",
                    roomCode);

                return;
            }

            if (string.IsNullOrWhiteSpace(
                    subscription.Auth))
            {
                logger.LogWarning(
                    "Skipping push subscription with empty Auth key for room {RoomCode}.",
                    roomCode);

                return;
            }


            // --------------------------------------------------------
            // CREATE BROWSER PUSH SUBSCRIPTION
            // --------------------------------------------------------

            var webPushSubscription =
                new Lib.Net.Http.WebPush.PushSubscription
                {
                    Endpoint =
                        subscription.Endpoint
                };


            // --------------------------------------------------------
            // BROWSER ENCRYPTION KEYS
            // --------------------------------------------------------

            webPushSubscription.Keys =
                new Dictionary<string, string>
                {
                    ["p256dh"] =
                        subscription.P256dh,

                    ["auth"] =
                        subscription.Auth
                };


            // --------------------------------------------------------
            // DELIVER PUSH
            // --------------------------------------------------------

            try
            {
                await pushClient
                    .RequestPushMessageDeliveryAsync(
                        webPushSubscription,
                        pushMessage,
                        vapidAuthentication);
            }
            catch (OperationCanceledException ex)
            {
                // ----------------------------------------------------
                // IMPORTANT:
                // A cancelled/timeout push request is isolated.
                // It must NOT stop other devices.
                // ----------------------------------------------------

                logger.LogWarning(
                    ex,
                    "Push delivery was cancelled for room {RoomCode}.",
                    roomCode);

                return;
            }
            catch (PushServiceClientException ex)
            {
                logger.LogWarning(
                    ex,
                    "Push provider rejected delivery for room {RoomCode}.",
                    roomCode);

                return;
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(
                    ex,
                    "HTTP error while sending push notification for room {RoomCode}.",
                    roomCode);

                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Unexpected push notification error for room {RoomCode}.",
                    roomCode);

                return;
            }


            // --------------------------------------------------------
            // SUCCESS
            // --------------------------------------------------------

            logger.LogDebug(
                "Push notification sent successfully for room {RoomCode}.",
                roomCode);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(
                ex,
                "Push notification operation cancelled for room {RoomCode}.",
                roomCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Unexpected push notification failure for room {RoomCode}.",
                roomCode);
        }
    }
}