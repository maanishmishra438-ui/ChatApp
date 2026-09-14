using Microsoft.EntityFrameworkCore;

namespace ChatApp.Web.Data;

public sealed class ChatDbContext(
    DbContextOptions<ChatDbContext> options)
    : DbContext(options)
{
    public DbSet<ChatRoom> Rooms =>
        Set<ChatRoom>();

    public DbSet<ChatMessage> Messages =>
        Set<ChatMessage>();

    public DbSet<MessageHiddenForUser> MessageHiddenForUsers =>
        Set<MessageHiddenForUser>();

    public DbSet<MessageReaction> MessageReactions =>
        Set<MessageReaction>();

    public DbSet<PushSubscription> PushSubscriptions =>
        Set<PushSubscription>();


    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);


        // ========================================================
        // CHAT ROOM
        // ========================================================

        modelBuilder.Entity<ChatRoom>()
            .HasKey(x => x.Id);

        modelBuilder.Entity<ChatRoom>()
            .HasIndex(x => x.Code)
            .IsUnique();


        // ========================================================
        // CHAT MESSAGE
        // ========================================================

        modelBuilder.Entity<ChatMessage>()
            .HasKey(x => x.Id);

        modelBuilder.Entity<ChatMessage>()
            .HasIndex(x => new
            {
                x.RoomCode,
                x.SentAt
            });

        modelBuilder.Entity<ChatMessage>()
            .HasIndex(x => x.ReplyToMessageId);


        // ========================================================
        // DELETE FOR ME
        // ========================================================

        modelBuilder.Entity<MessageHiddenForUser>()
            .HasKey(x => x.Id);

        modelBuilder.Entity<MessageHiddenForUser>()
            .HasIndex(x => new
            {
                x.MessageId,
                x.UserName
            })
            .IsUnique();

        modelBuilder.Entity<MessageHiddenForUser>()
            .HasIndex(x => x.UserName);


        // ========================================================
        // MESSAGE REACTIONS
        // ========================================================

        modelBuilder.Entity<MessageReaction>()
            .HasKey(x => x.Id);

        modelBuilder.Entity<MessageReaction>()
            .HasIndex(x => new
            {
                x.MessageId,
                x.UserName
            })
            .IsUnique();

        modelBuilder.Entity<MessageReaction>()
            .HasIndex(x => x.MessageId);

        modelBuilder.Entity<MessageReaction>()
            .HasIndex(x => x.UserName);


        // ========================================================
        // PUSH SUBSCRIPTIONS
        // ========================================================

        modelBuilder.Entity<PushSubscription>()
            .HasKey(x => x.Id);

        modelBuilder.Entity<PushSubscription>()
            .HasIndex(x => new
            {
                x.UserName,
                x.RoomCode,
                x.Endpoint
            })
            .IsUnique();

        modelBuilder.Entity<PushSubscription>()
            .HasIndex(x => x.UserName);

        modelBuilder.Entity<PushSubscription>()
            .HasIndex(x => x.RoomCode);
    }
}


// =================================================================
// CHAT ROOM
// =================================================================

public sealed class ChatRoom
{
    public int Id { get; set; }

    public string Code { get; set; } =
        string.Empty;

    public DateTime CreatedAt { get; set; } =
        DateTime.UtcNow;
}


// =================================================================
// CHAT MESSAGE
// =================================================================

public sealed class ChatMessage
{
    public long Id { get; set; }

    public string RoomCode { get; set; } =
        string.Empty;

    public string Sender { get; set; } =
        string.Empty;

    public string Text { get; set; } =
        string.Empty;

    public DateTime SentAt { get; set; } =
        DateTime.UtcNow;


    // ============================================================
    // READ STATUS
    // ============================================================

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }


    // ============================================================
    // REPLY
    // ============================================================

    public long? ReplyToMessageId { get; set; }

    public string? ReplyToSender { get; set; }

    public string? ReplyToText { get; set; }


    // ============================================================
    // DELETE / UNSEND
    // ============================================================

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }
}


// =================================================================
// MESSAGE HIDDEN FOR USER
// =================================================================

public sealed class MessageHiddenForUser
{
    public long Id { get; set; }

    public long MessageId { get; set; }

    public string UserName { get; set; } =
        string.Empty;

    public DateTime HiddenAt { get; set; } =
        DateTime.UtcNow;
}


// =================================================================
// MESSAGE REACTION
// =================================================================

public sealed class MessageReaction
{
    public long Id { get; set; }

    public long MessageId { get; set; }

    public string UserName { get; set; } =
        string.Empty;

    public string Reaction { get; set; } =
        string.Empty;

    public DateTime CreatedAt { get; set; } =
        DateTime.UtcNow;
}


// =================================================================
// PUSH SUBSCRIPTION
// =================================================================

public sealed class PushSubscription
{
    public long Id { get; set; }

    // User who owns this browser/device subscription.
    public string UserName { get; set; } =
        string.Empty;

    // Room for which notifications are enabled.
    public string RoomCode { get; set; } =
        string.Empty;

    // Browser push endpoint.
    public string Endpoint { get; set; } =
        string.Empty;

    // Public encryption key generated by the browser.
    public string P256dh { get; set; } =
        string.Empty;

    // Authentication secret generated by the browser.
    public string Auth { get; set; } =
        string.Empty;

    public DateTime CreatedAt { get; set; } =
        DateTime.UtcNow;
}