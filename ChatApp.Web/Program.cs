using ChatApp.Web.Data;
using ChatApp.Web.Hubs;
using ChatApp.Web.Services;
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
        options.UseSqlite(
            builder.Configuration
                .GetConnectionString("ChatDb")
            ?? "Data Source=chatapp.db"));


builder.Services.AddScoped<ChatService>();


var app =
    builder.Build();


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
// DATABASE INITIALIZATION
// ================================================================

await using (
    var scope =
        app.Services.CreateAsyncScope())
{
    var db =
        scope.ServiceProvider
            .GetRequiredService<ChatDbContext>();


    await db.Database.EnsureCreatedAsync();


    // ============================================================
    // READ STATUS COLUMNS
    // ============================================================

    try
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE Messages
            ADD COLUMN IsRead INTEGER NOT NULL DEFAULT 0;
            """);
    }
    catch
    {
        // Column probably already exists.
    }


    try
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE Messages
            ADD COLUMN ReadAt TEXT NULL;
            """);
    }
    catch
    {
        // Column probably already exists.
    }


    // ============================================================
    // REPLY COLUMNS
    // ============================================================

    try
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE Messages
            ADD COLUMN ReplyToMessageId INTEGER NULL;
            """);
    }
    catch
    {
        // Column probably already exists.
    }


    try
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE Messages
            ADD COLUMN ReplyToSender TEXT NULL;
            """);
    }
    catch
    {
        // Column probably already exists.
    }


    try
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE Messages
            ADD COLUMN ReplyToText TEXT NULL;
            """);
    }
    catch
    {
        // Column probably already exists.
    }
}


app.Run();