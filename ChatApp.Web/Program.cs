using ChatApp.Web.Data;
using ChatApp.Web.Hubs;
using ChatApp.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder =
    WebApplication.CreateBuilder(args);


// ================================================================
// BACKGROUND SERVICE SAFETY
// ================================================================
//
// A failure inside a BackgroundService (for example push
// notification delivery) must NEVER bring down the whole ChatApp.
// The chat UI/SignalR circuit must stay alive even if a background
// notification attempt fails.
//
// The worker itself is still expected to catch and recover from
// individual notification errors. This setting is the final safety
// net so one unexpected worker exception cannot terminate the host.
builder.Services.Configure<HostOptions>(
    options =>
    {
        options.BackgroundServiceExceptionBehavior =
            BackgroundServiceExceptionBehavior.Ignore;
    });


// ================================================================
// SERVICES
// ================================================================

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();


// ================================================================
// SIGNALR
// ================================================================

// Photo upload ke liye SignalR ke through byte[] transfer hota hai.
// Maximum photo size ChatHub mein 10 MB hai.
//
// JSON/SignalR overhead ki wajah se 10 MB file ko transfer karne
// ke liye thoda extra limit rakhi gayi hai.
//
// 15 MB = 15 * 1024 * 1024 bytes.
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize =
        15 * 1024 * 1024;
});


// ================================================================
// DATABASE
// ================================================================

builder.Services.AddDbContextFactory<ChatDbContext>(
    options =>
        options.UseNpgsql(
            builder.Configuration
                .GetConnectionString("ChatDb")
            ?? throw new InvalidOperationException(
                "Connection string 'ChatDb' was not found.")));

builder.Services.AddScoped<ChatService>();


// ================================================================
// SUPABASE STORAGE
// ================================================================

// Handles photo/media uploads to Supabase Storage.
builder.Services.AddScoped<
    SupabaseStorageService>();


// ================================================================
// PUSH NOTIFICATION
// ================================================================

// Service that actually sends Web Push notifications.
builder.Services.AddScoped<
    PushNotificationService>();


// Queue must be Singleton because it is shared between
// SignalR requests and the background worker.
builder.Services.AddSingleton<
    PushNotificationQueue>();


// Background worker continuously reads the queue and
// sends notifications without blocking ChatHub.
builder.Services.AddHostedService<
    PushNotificationWorker>();


// ================================================================
// PRESENCE CLEANUP
// ================================================================

// Removes users who stopped sending presence heartbeats.
// Presence is kept in server memory only.
// Nothing is added to the database.
builder.Services.AddHostedService<
    PresenceCleanupService>();


// ================================================================
// FORWARDED HEADERS
// IMPORTANT FOR RENDER / REVERSE PROXY / HTTPS
// ================================================================

builder.Services.Configure<ForwardedHeadersOptions>(
    options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto |
            ForwardedHeaders.XForwardedHost;

        // Render works behind a reverse proxy.
        // Clear these so forwarded headers are accepted.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });


// ================================================================
// BUILD APP
// ================================================================

var app =
    builder.Build();


// ================================================================
// DATABASE MIGRATIONS
// ================================================================

using (var scope = app.Services.CreateScope())
{
    var dbFactory =
        scope.ServiceProvider
            .GetRequiredService<
                IDbContextFactory<ChatDbContext>>();

    await using var db =
        await dbFactory.CreateDbContextAsync();

    await db.Database.MigrateAsync();
}


// ================================================================
// FORWARDED HEADERS
// MUST RUN EARLY
// ================================================================

app.UseForwardedHeaders();


// ================================================================
// MIDDLEWARE
// ================================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");

    app.UseHsts();
}


app.MapStaticAssets();

app.UseAntiforgery();


// ================================================================
// PUSH NOTIFICATION - SAVE SUBSCRIPTION
// ================================================================

app.MapPost(
    "/api/notifications/subscribe",
    async (
        PushSubscriptionRequest request,
        ChatService chatService) =>
    {
        if (string.IsNullOrWhiteSpace(
                request.UserName) ||
            string.IsNullOrWhiteSpace(
                request.RoomCode) ||
            string.IsNullOrWhiteSpace(
                request.Endpoint) ||
            string.IsNullOrWhiteSpace(
                request.P256dh) ||
            string.IsNullOrWhiteSpace(
                request.Auth))
        {
            return Results.BadRequest(
                new
                {
                    message =
                        "Invalid push subscription data."
                });
        }

        try
        {
            await chatService.SavePushSubscriptionAsync(
                request.UserName,
                request.RoomCode,
                request.Endpoint,
                request.P256dh,
                request.Auth);

            return Results.Ok(
                new
                {
                    message =
                        "Push subscription saved."
                });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message);
        }
    });


// ================================================================
// PUSH NOTIFICATION - GET VAPID PUBLIC KEY
// ================================================================

app.MapGet(
    "/api/notifications/public-key",
    (PushNotificationService service) =>
    {
        return Results.Ok(
            new
            {
                publicKey =
                    service.PublicKey
            });
    });


// ================================================================
// PUSH NOTIFICATION - LIKE
// ================================================================

app.MapPost(
    "/api/notifications/like",
    async (
        LikeNotificationRequest request,
        ChatService chatService) =>
    {
        if (string.IsNullOrWhiteSpace(
                request.RoomCode) ||
            request.MessageId <= 0 ||
            string.IsNullOrWhiteSpace(
                request.UserName))
        {
            return Results.BadRequest(
                new
                {
                    message =
                        "Invalid like request."
                });
        }

        try
        {
            await chatService.ToggleReactionAsync(
                request.RoomCode,
                request.MessageId,
                request.UserName,
                "❤️");

            return Results.Ok(
                new
                {
                    message =
                        "Message liked."
                });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message);
        }
    });


// ================================================================
// PHOTO / MEDIA - NORMAL PHOTO
// ================================================================
//
// Normal photo can be opened/downloaded normally.
//
// ViewOnce photos are intentionally rejected here.
// They must use the dedicated ViewOnce endpoint below.
//

app.MapGet(
    "/api/media/{messageId:long}",
    async (
        long messageId,
        string roomCode,
        string userName,
        ChatService chatService,
        SupabaseStorageService storageService) =>
    {
        if (messageId <= 0 ||
            string.IsNullOrWhiteSpace(roomCode) ||
            string.IsNullOrWhiteSpace(userName))
        {
            return Results.BadRequest(
                new
                {
                    message =
                        "Invalid photo request."
                });
        }

        try
        {
            roomCode =
                roomCode.Trim()
                    .ToLowerInvariant();

            userName =
                userName.Trim();

            // Get the message visible to this user.
            var messages =
                await chatService.GetMessagesAsync(
                    roomCode,
                    userName);

            var message =
                messages.FirstOrDefault(
                    x => x.Id == messageId);

            if (message is null)
            {
                return Results.NotFound(
                    new
                    {
                        message =
                            "Photo message was not found."
                    });
            }

            if (message.IsDeleted)
            {
                return Results.NotFound(
                    new
                    {
                        message =
                            "This message has been deleted."
                    });
            }

            if (!string.Equals(
                    message.MessageType,
                    "Photo",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(
                    new
                    {
                        message =
                            "This message is not a photo."
                    });
            }

            // IMPORTANT:
            // ViewOnce photos can NEVER be accessed through
            // the normal media endpoint.
            if (string.Equals(
                    message.PhotoMode,
                    "ViewOnce",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(
                    new
                    {
                        message =
                            "This is a View Once photo. Use the View Once endpoint."
                    });
            }

            if (string.IsNullOrWhiteSpace(
                    message.MediaPath))
            {
                return Results.NotFound(
                    new
                    {
                        message =
                            "Photo file was not found."
                    });
            }

            var bytes =
                await storageService.DownloadPhotoAsync(
                    message.MediaPath);

            if (bytes is null ||
                bytes.Length == 0)
            {
                return Results.NotFound(
                    new
                    {
                        message =
                            "Photo file is empty or unavailable."
                    });
            }

            var contentType =
                string.IsNullOrWhiteSpace(
                    message.MediaContentType)
                    ? "application/octet-stream"
                    : message.MediaContentType;

            var fileName =
                string.IsNullOrWhiteSpace(
                    message.MediaFileName)
                    ? "photo"
                    : Path.GetFileName(
                        message.MediaFileName);

            return Results.File(
                bytes,
                contentType,
                fileName);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail:
                    $"Unable to load photo: {ex.Message}");
        }
    });


// ================================================================
// PHOTO / MEDIA - VIEW ONCE PHOTO
// ================================================================
//
// ViewOnce photo is protected by the database.
//
// The important operation is:
//
//     ClaimViewOncePhotoAsync()
//
// That method atomically changes:
//
//     ViewedAt = null
//
// to:
//
//     ViewedAt = current UTC time
//
// Only one request can successfully perform that operation.
//
// Therefore refreshing/opening again cannot return the photo.
//

app.MapPost(
    "/api/media/view-once/{messageId:long}",
    async (
        long messageId,
        ViewOncePhotoRequest request,
        ChatService chatService,
        SupabaseStorageService storageService) =>
    {
        if (messageId <= 0 ||
            string.IsNullOrWhiteSpace(
                request.RoomCode) ||
            string.IsNullOrWhiteSpace(
                request.UserName))
        {
            return Results.BadRequest(
                new
                {
                    message =
                        "Invalid View Once photo request."
                });
        }

        try
        {
            var roomCode =
                request.RoomCode.Trim()
                    .ToLowerInvariant();

            var userName =
                request.UserName.Trim();

            // ----------------------------------------------------
            // FIND MESSAGE
            // ----------------------------------------------------

            var messages =
                await chatService.GetMessagesAsync(
                    roomCode,
                    userName);

            var message =
                messages.FirstOrDefault(
                    x => x.Id == messageId);

            if (message is null)
            {
                return Results.NotFound(
                    new
                    {
                        message =
                            "Photo message was not found."
                    });
            }

            // ----------------------------------------------------
            // DELETED CHECK
            // ----------------------------------------------------

            if (message.IsDeleted)
            {
                return Results.StatusCode(
                    StatusCodes.Status410Gone);
            }

            // ----------------------------------------------------
            // MESSAGE TYPE CHECK
            // ----------------------------------------------------

            if (!string.Equals(
                    message.MessageType,
                    "Photo",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(
                    new
                    {
                        message =
                            "This message is not a photo."
                    });
            }

            // ----------------------------------------------------
            // PHOTO MODE CHECK
            // ----------------------------------------------------

            if (!string.Equals(
                    message.PhotoMode,
                    "ViewOnce",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(
                    new
                    {
                        message =
                            "This photo is not a View Once photo."
                    });
            }

            // ----------------------------------------------------
            // MEDIA PATH CHECK
            // ----------------------------------------------------

            if (string.IsNullOrWhiteSpace(
                    message.MediaPath))
            {
                return Results.NotFound(
                    new
                    {
                        message =
                            "Photo file was not found."
                    });
            }

            // ----------------------------------------------------
            // SENDER CANNOT OPEN OWN VIEW ONCE PHOTO
            // ----------------------------------------------------

            if (string.Equals(
                    message.Sender,
                    userName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Results.Forbid();
            }

            // ----------------------------------------------------
            // DOWNLOAD PHOTO
            // ----------------------------------------------------
            //
            // We download before claiming the photo.
            //
            // If Supabase download fails, ViewedAt is NOT changed.
            //

            var bytes =
                await storageService.DownloadPhotoAsync(
                    message.MediaPath);

            if (bytes is null ||
                bytes.Length == 0)
            {
                return Results.NotFound(
                    new
                    {
                        message =
                            "Photo file is empty or unavailable."
                    });
            }

            var contentType =
                string.IsNullOrWhiteSpace(
                    message.MediaContentType)
                    ? "application/octet-stream"
                    : message.MediaContentType;

            var fileName =
                string.IsNullOrWhiteSpace(
                    message.MediaFileName)
                    ? "photo"
                    : Path.GetFileName(
                        message.MediaFileName);

            // ----------------------------------------------------
            // ATOMIC VIEW ONCE CLAIM
            // ----------------------------------------------------
            //
            // This is the REAL ViewOnce protection.
            //
            // If another request already changed ViewedAt,
            // this returns null.
            //

            var claimedMessage =
                await chatService.ClaimViewOncePhotoAsync(
                    roomCode,
                    messageId,
                    userName);

            if (claimedMessage is null)
            {
                // 410 Gone means:
                //
                // "This ViewOnce photo has already been opened
                //  or is no longer available."
                //
                return Results.StatusCode(
                    StatusCodes.Status410Gone);
            }

            // ----------------------------------------------------
            // SUCCESS
            // ----------------------------------------------------

            return Results.File(
                bytes,
                contentType,
                fileName);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail:
                    $"Unable to open View Once photo: {ex.Message}");
        }
    });


// ================================================================
// SIGNALR HUB
// ================================================================

app.MapHub<ChatHub>("/chathub");


// ================================================================
// RAZOR COMPONENTS
// ================================================================

app.MapRazorComponents<
        ChatApp.Web.Components.App>()
    .AddInteractiveServerRenderMode();


// ================================================================
// RUN
// ================================================================

app.Run();


// ================================================================
// REQUEST MODELS
// ================================================================

public sealed record PushSubscriptionRequest(
    string UserName,
    string RoomCode,
    string Endpoint,
    string P256dh,
    string Auth);


public sealed record LikeNotificationRequest(
    string RoomCode,
    long MessageId,
    string UserName);


// ================================================================
// VIEW ONCE PHOTO REQUEST
// ================================================================

public sealed record ViewOncePhotoRequest(
    string RoomCode,
    string UserName);