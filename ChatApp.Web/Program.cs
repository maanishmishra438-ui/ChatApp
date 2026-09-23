using ChatApp.Web.Data;
using ChatApp.Web.Hubs;
using ChatApp.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder =
    WebApplication.CreateBuilder(args);


// ================================================================
// SERVICES
// ================================================================

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();


// ================================================================
// SIGNALR
// ================================================================

builder.Services.AddSignalR();


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
        if (string.IsNullOrWhiteSpace(request.UserName) ||
            string.IsNullOrWhiteSpace(request.RoomCode) ||
            string.IsNullOrWhiteSpace(request.Endpoint) ||
            string.IsNullOrWhiteSpace(request.P256dh) ||
            string.IsNullOrWhiteSpace(request.Auth))
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
        if (string.IsNullOrWhiteSpace(request.RoomCode) ||
            request.MessageId <= 0 ||
            string.IsNullOrWhiteSpace(request.UserName))
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