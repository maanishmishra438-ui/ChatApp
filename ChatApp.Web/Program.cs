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


builder.Services.AddSignalR();


builder.Services.AddDbContextFactory<ChatDbContext>(
    options =>
        options.UseNpgsql(
            builder.Configuration
                .GetConnectionString("ChatDb")
            ?? throw new InvalidOperationException(
                "Connection string 'ChatDb' was not found.")));


builder.Services.AddScoped<ChatService>();


// ================================================================
// FORWARDED HEADERS
// IMPORTANT FOR NGROK / REVERSE PROXY
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