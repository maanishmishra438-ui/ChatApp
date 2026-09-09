using Microsoft.EntityFrameworkCore;

namespace ChatApp.Web.Data;

public sealed class ChatDbContext(
    DbContextOptions<ChatDbContext> options)
    : DbContext(options)
{
    public DbSet<ChatRoom> Rooms => Set<ChatRoom>();

    public DbSet<ChatMessage> Messages => Set<ChatMessage>();

    public DbSet<MessageHiddenForUser> MessageHiddenForUsers =>
        Set<MessageHiddenForUser>();

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
        // HIDDEN MESSAGE FOR USER
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