using ChatApp.Web.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;

namespace ChatApp.Web.Services;

public sealed class ChatService(
    IDbContextFactory<ChatDbContext> factory)
{
    // ============================================================
    // ENSURE DATABASE SCHEMA
    // ============================================================

    private async Task EnsureDeleteSchemaAsync(
        ChatDbContext db)
    {
        var connection =
            db.Database.GetDbConnection();

        if (connection.State !=
            ConnectionState.Open)
        {
            await connection.OpenAsync();
        }


        // ========================================================
        // IsDeleted
        // ========================================================

        if (!await ColumnExistsAsync(
                connection,
                "Messages",
                "IsDeleted"))
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE Messages
                ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;
                """);
        }


        // ========================================================
        // DeletedAt
        // ========================================================

        if (!await ColumnExistsAsync(
                connection,
                "Messages",
                "DeletedAt"))
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE Messages
                ADD COLUMN DeletedAt TEXT NULL;
                """);
        }


        // ========================================================
        // DeletedBy
        // ========================================================

        if (!await ColumnExistsAsync(
                connection,
                "Messages",
                "DeletedBy"))
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE Messages
                ADD COLUMN DeletedBy TEXT NULL;
                """);
        }


        // ========================================================
        // DELETE FOR ME TABLE
        // ========================================================

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS MessageHiddenForUsers
            (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                MessageId INTEGER NOT NULL,
                UserName TEXT NOT NULL,
                HiddenAt TEXT NOT NULL
            );
            """);


        // ========================================================
        // DELETE UNIQUE INDEX
        // ========================================================

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
            IX_MessageHiddenForUsers_MessageId_UserName
            ON MessageHiddenForUsers(MessageId, UserName);
            """);


        // ========================================================
        // DELETE USER INDEX
        // ========================================================

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS
            IX_MessageHiddenForUsers_UserName
            ON MessageHiddenForUsers(UserName);
            """);


        // ========================================================
        // REACTION TABLE
        // ========================================================

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS MessageReactions
            (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                MessageId INTEGER NOT NULL,
                UserName TEXT NOT NULL,
                Reaction TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            """);


        // ========================================================
        // REACTION UNIQUE INDEX
        // ========================================================

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS
            IX_MessageReactions_MessageId_UserName
            ON MessageReactions(MessageId, UserName);
            """);


        // ========================================================
        // REACTION MESSAGE INDEX
        // ========================================================

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS
            IX_MessageReactions_MessageId
            ON MessageReactions(MessageId);
            """);


        // ========================================================
        // REACTION USER INDEX
        // ========================================================

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS
            IX_MessageReactions_UserName
            ON MessageReactions(UserName);
            """);
    }


    // ============================================================
    // CHECK COLUMN EXISTS
    // ============================================================

    private static async Task<bool> ColumnExistsAsync(
        System.Data.Common.DbConnection connection,
        string tableName,
        string columnName)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT COUNT(*)
            FROM pragma_table_info(@tableName)
            WHERE name = @columnName;
            """;


        var tableParameter =
            command.CreateParameter();

        tableParameter.ParameterName =
            "@tableName";

        tableParameter.Value =
            tableName;

        command.Parameters.Add(
            tableParameter);


        var columnParameter =
            command.CreateParameter();

        columnParameter.ParameterName =
            "@columnName";

        columnParameter.Value =
            columnName;

        command.Parameters.Add(
            columnParameter);


        var result =
            await command.ExecuteScalarAsync();

        return Convert.ToInt32(result) > 0;
    }


    // ============================================================
    // CREATE ROOM
    // ============================================================

    public async Task<string> CreateRoomAsync()
    {
        await using var db =
            await factory.CreateDbContextAsync();


        string code;


        do
        {
            code =
                Convert.ToHexString(
                    RandomNumberGenerator.GetBytes(6))
                .ToLowerInvariant();
        }
        while (
            await db.Rooms.AnyAsync(
                x => x.Code == code));


        db.Rooms.Add(
            new ChatRoom
            {
                Code = code
            });


        await db.SaveChangesAsync();


        return code;
    }


    // ============================================================
    // ROOM EXISTS
    // ============================================================

    public async Task<bool> RoomExistsAsync(
        string code)
    {
        code =
            code.Trim()
                .ToLowerInvariant();


        await using var db =
            await factory.CreateDbContextAsync();


        return await db.Rooms
            .AnyAsync(
                x => x.Code == code);
    }


    // ============================================================
    // GET MESSAGES
    // ============================================================

    public async Task<List<ChatMessage>> GetMessagesAsync(
        string code,
        string viewerName = "")
    {
        code =
            code.Trim()
                .ToLowerInvariant();


        viewerName =
            viewerName.Trim();


        await using var db =
            await factory.CreateDbContextAsync();


        await EnsureDeleteSchemaAsync(db);


        var hiddenIds =
            string.IsNullOrWhiteSpace(viewerName)
                ? new HashSet<long>()
                : (
                    await db.MessageHiddenForUsers
                        .AsNoTracking()
                        .Where(
                            x =>
                                x.UserName == viewerName)
                        .Select(
                            x =>
                                x.MessageId)
                        .ToListAsync()
                ).ToHashSet();


        var result =
            await db.Messages
                .Where(
                    x =>
                        x.RoomCode == code)
                .OrderBy(
                    x =>
                        x.SentAt)
                .Take(500)
                .ToListAsync();


        if (hiddenIds.Count == 0)
        {
            return result;
        }


        return result
            .Where(
                x =>
                    !hiddenIds.Contains(x.Id))
            .ToList();
    }


    // ============================================================
    // GET REACTIONS
    // ============================================================

    public async Task<
        Dictionary<long, List<ReactionInfo>>
    > GetReactionsAsync(
        IEnumerable<long> messageIds)
    {
        var ids =
            messageIds
                .Distinct()
                .ToList();


        if (ids.Count == 0)
        {
            return new Dictionary<
                long,
                List<ReactionInfo>>();
        }


        await using var db =
            await factory.CreateDbContextAsync();


        await EnsureDeleteSchemaAsync(db);


        var rows =
            await db.MessageReactions
                .AsNoTracking()
                .Where(
                    x =>
                        ids.Contains(x.MessageId))
                .OrderBy(
                    x =>
                        x.CreatedAt)
                .ToListAsync();


        return rows
            .GroupBy(
                x =>
                    x.MessageId)
            .ToDictionary(
                g =>
                    g.Key,
                g =>
                    g.Select(
                        x =>
                            new ReactionInfo(
                                x.UserName,
                                x.Reaction))
                    .ToList());
    }


    // ============================================================
    // SAVE MESSAGE
    // ============================================================

    public async Task<ChatMessage> SaveMessageAsync(
        string code,
        string sender,
        string text,
        long? replyToMessageId = null)
    {
        code =
            code.Trim()
                .ToLowerInvariant();


        sender =
            sender.Trim();


        text =
            text.Trim();


        if (string.IsNullOrWhiteSpace(sender))
        {
            throw new ArgumentException(
                "Name is required.");
        }


        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                "Message cannot be empty.");
        }


        if (sender.Length > 40)
        {
            sender =
                sender[..40];
        }


        if (text.Length > 2000)
        {
            throw new ArgumentException(
                "Message is too long.");
        }


        await using var db =
            await factory.CreateDbContextAsync();


        await EnsureDeleteSchemaAsync(db);


        var roomExists =
            await db.Rooms.AnyAsync(
                x =>
                    x.Code == code);


        if (!roomExists)
        {
            throw new InvalidOperationException(
                "Room not found.");
        }


        string? replySender =
            null;


        string? replyText =
            null;


        if (replyToMessageId.HasValue)
        {
            var repliedMessage =
                await db.Messages
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.Id ==
                            replyToMessageId.Value &&
                            x.RoomCode ==
                            code);


            if (repliedMessage is not null &&
                !repliedMessage.IsDeleted)
            {
                replySender =
                    repliedMessage.Sender;


                replyText =
                    repliedMessage.Text;


                if (replySender.Length > 40)
                {
                    replySender =
                        replySender[..40];
                }


                if (replyText.Length > 500)
                {
                    replyText =
                        replyText[..500] +
                        "…";
                }
            }
            else
            {
                replyToMessageId =
                    null;
            }
        }


        var message =
            new ChatMessage
            {
                RoomCode =
                    code,

                Sender =
                    sender,

                Text =
                    text,

                SentAt =
                    DateTime.UtcNow,

                IsRead =
                    false,

                ReadAt =
                    null,

                ReplyToMessageId =
                    replyToMessageId,

                ReplyToSender =
                    replySender,

                ReplyToText =
                    replyText,

                IsDeleted =
                    false,

                DeletedAt =
                    null,

                DeletedBy =
                    null
            };


        db.Messages.Add(
            message);


        await db.SaveChangesAsync();


        return message;
    }


    // ============================================================
    // MARK MESSAGE AS READ
    // ============================================================

    public async Task<bool> MarkMessageAsReadAsync(
        string roomCode,
        long messageId,
        string readerName)
    {
        roomCode =
            roomCode.Trim()
                .ToLowerInvariant();


        readerName =
            readerName.Trim();


        if (string.IsNullOrWhiteSpace(
                readerName))
        {
            return false;
        }


        await using var db =
            await factory.CreateDbContextAsync();


        await EnsureDeleteSchemaAsync(db);


        var message =
            await db.Messages
                .FirstOrDefaultAsync(
                    x =>
                        x.Id ==
                        messageId &&
                        x.RoomCode ==
                        roomCode);


        if (message is null ||
            message.IsDeleted)
        {
            return false;
        }


        if (message.Sender.Equals(
                readerName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }


        if (message.IsRead)
        {
            return false;
        }


        message.IsRead =
            true;


        message.ReadAt =
            DateTime.UtcNow;


        await db.SaveChangesAsync();


        return true;
    }


    // ============================================================
    // DELETE FOR ME
    // ============================================================

    public async Task<bool> DeleteForMeAsync(
        string roomCode,
        long messageId,
        string userName)
    {
        roomCode =
            roomCode.Trim()
                .ToLowerInvariant();


        userName =
            userName.Trim();


        if (string.IsNullOrWhiteSpace(
                userName))
        {
            return false;
        }


        await using var db =
            await factory.CreateDbContextAsync();


        await EnsureDeleteSchemaAsync(db);


        var message =
            await db.Messages
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.Id ==
                        messageId &&
                        x.RoomCode ==
                        roomCode);


        if (message is null)
        {
            return false;
        }


        var alreadyHidden =
            await db.MessageHiddenForUsers
                .AnyAsync(
                    x =>
                        x.MessageId ==
                        messageId &&
                        x.UserName ==
                        userName);


        if (alreadyHidden)
        {
            return true;
        }


        db.MessageHiddenForUsers.Add(
            new MessageHiddenForUser
            {
                MessageId =
                    messageId,

                UserName =
                    userName,

                HiddenAt =
                    DateTime.UtcNow
            });


        await db.SaveChangesAsync();


        return true;
    }


    // ============================================================
    // DELETE FOR EVERYONE / UNSEND
    // ============================================================

    public async Task<bool> DeleteForEveryoneAsync(
        string roomCode,
        long messageId,
        string senderName)
    {
        roomCode =
            roomCode.Trim()
                .ToLowerInvariant();


        senderName =
            senderName.Trim();


        if (string.IsNullOrWhiteSpace(
                senderName))
        {
            return false;
        }


        await using var db =
            await factory.CreateDbContextAsync();


        await EnsureDeleteSchemaAsync(db);


        var message =
            await db.Messages
                .FirstOrDefaultAsync(
                    x =>
                        x.Id ==
                        messageId &&
                        x.RoomCode ==
                        roomCode);


        if (message is null)
        {
            return false;
        }


        if (!message.Sender.Equals(
                senderName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }


        if (message.IsDeleted)
        {
            return true;
        }


        message.IsDeleted =
            true;


        message.DeletedAt =
            DateTime.UtcNow;


        message.DeletedBy =
            senderName;


        message.Text =
            "[This message was deleted]";


        message.ReplyToMessageId =
            null;


        message.ReplyToSender =
            null;


        message.ReplyToText =
            null;


        // Remove reactions when message is unsent.
        await db.MessageReactions
            .Where(
                x =>
                    x.MessageId == messageId)
            .ExecuteDeleteAsync();


        await db.SaveChangesAsync();


        return true;
    }


    // ============================================================
    // TOGGLE REACTION
    // ============================================================

    public async Task<ReactionResult> ToggleReactionAsync(
        string roomCode,
        long messageId,
        string userName,
        string reaction)
    {
        roomCode =
            roomCode.Trim()
                .ToLowerInvariant();


        userName =
            userName.Trim();


        reaction =
            reaction.Trim();


        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException(
                "User name is required.");
        }


        if (string.IsNullOrWhiteSpace(reaction))
        {
            throw new ArgumentException(
                "Reaction is required.");
        }


        var allowedReactions =
            new HashSet<string>
            {
                "👍",
                "❤️",
                "😂",
                "😮",
                "😢",
                "👎"
            };


        if (!allowedReactions.Contains(
                reaction))
        {
            throw new ArgumentException(
                "Invalid reaction.");
        }


        await using var db =
            await factory.CreateDbContextAsync();


        await EnsureDeleteSchemaAsync(db);


        var message =
            await db.Messages
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.Id ==
                        messageId &&
                        x.RoomCode ==
                        roomCode);


        if (message is null)
        {
            throw new InvalidOperationException(
                "Message not found.");
        }


        if (message.IsDeleted)
        {
            throw new InvalidOperationException(
                "Deleted messages cannot be reacted to.");
        }


        var existingReaction =
            await db.MessageReactions
                .FirstOrDefaultAsync(
                    x =>
                        x.MessageId ==
                        messageId &&
                        x.UserName ==
                        userName);


        var removed =
            false;


        if (existingReaction is null)
        {
            db.MessageReactions.Add(
                new MessageReaction
                {
                    MessageId =
                        messageId,

                    UserName =
                        userName,

                    Reaction =
                        reaction,

                    CreatedAt =
                        DateTime.UtcNow
                });
        }
        else if (existingReaction.Reaction ==
                 reaction)
        {
            db.MessageReactions.Remove(
                existingReaction);

            removed = true;
        }
        else
        {
            existingReaction.Reaction =
                reaction;

            existingReaction.CreatedAt =
                DateTime.UtcNow;
        }


        await db.SaveChangesAsync();


        var reactions =
            await db.MessageReactions
                .AsNoTracking()
                .Where(
                    x =>
                        x.MessageId ==
                        messageId)
                .OrderBy(
                    x =>
                        x.CreatedAt)
                .Select(
                    x =>
                        new ReactionInfo(
                            x.UserName,
                            x.Reaction))
                .ToListAsync();


        return new ReactionResult(
            removed,
            reactions);
    }


    // ============================================================
    // REACTION TYPES
    // ============================================================

    public sealed record ReactionInfo(
        string UserName,
        string Reaction);


    public sealed record ReactionResult(
        bool Removed,
        List<ReactionInfo> Reactions);
}