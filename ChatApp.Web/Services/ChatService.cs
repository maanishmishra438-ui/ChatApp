using ChatApp.Web.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;

namespace ChatApp.Web.Services;

public sealed class ChatService(
    IDbContextFactory<ChatDbContext> factory)
{
    // ============================================================
    // ENSURE DELETE SCHEMA
    // ============================================================

    private async Task EnsureDeleteSchemaAsync(
      ChatDbContext db)
    {
        var connection =
            db.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
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
        // UNIQUE INDEX
        // ========================================================

        await db.Database.ExecuteSqlRawAsync(
            """
        CREATE UNIQUE INDEX IF NOT EXISTS
        IX_MessageHiddenForUsers_MessageId_UserName
        ON MessageHiddenForUsers(MessageId, UserName);
        """);

        // ========================================================
        // USER INDEX
        // ========================================================

        await db.Database.ExecuteSqlRawAsync(
            """
        CREATE INDEX IF NOT EXISTS
        IX_MessageHiddenForUsers_UserName
        ON MessageHiddenForUsers(UserName);
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
            .AnyAsync(x => x.Code == code);
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

        // --------------------------------------------------------
        // GET MESSAGE IDS HIDDEN FOR CURRENT USER
        // --------------------------------------------------------

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


        // --------------------------------------------------------
        // GET ROOM MESSAGES
        // --------------------------------------------------------

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


        // --------------------------------------------------------
        // REMOVE MESSAGES DELETED FOR ME
        // --------------------------------------------------------

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


        // --------------------------------------------------------
        // VALIDATION
        // --------------------------------------------------------

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


        // --------------------------------------------------------
        // ROOM VALIDATION
        // --------------------------------------------------------

        var roomExists =
            await db.Rooms.AnyAsync(
                x =>
                    x.Code == code);

        if (!roomExists)
        {
            throw new InvalidOperationException(
                "Room not found.");
        }


        // ========================================================
        // REPLY INFORMATION
        // ========================================================

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


            // ----------------------------------------------------
            // ONLY ALLOW REPLY TO EXISTING NON-DELETED MESSAGE
            // ----------------------------------------------------

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


        // ========================================================
        // CREATE MESSAGE
        // ========================================================

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


        // --------------------------------------------------------
        // DON'T MARK OWN MESSAGE
        // --------------------------------------------------------

        if (message.Sender.Equals(
                readerName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }


        // --------------------------------------------------------
        // ALREADY READ
        // --------------------------------------------------------

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


        // --------------------------------------------------------
        // CHECK MESSAGE
        // --------------------------------------------------------

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


        // --------------------------------------------------------
        // ALREADY HIDDEN
        // --------------------------------------------------------

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


        // --------------------------------------------------------
        // ADD USER-SPECIFIC HIDE RECORD
        // --------------------------------------------------------

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


        // --------------------------------------------------------
        // ONLY ORIGINAL SENDER CAN UNSEND
        // --------------------------------------------------------

        if (!message.Sender.Equals(
                senderName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }


        // --------------------------------------------------------
        // ALREADY DELETED
        // --------------------------------------------------------

        if (message.IsDeleted)
        {
            return true;
        }


        // --------------------------------------------------------
        // MARK AS DELETED
        // --------------------------------------------------------

        message.IsDeleted =
            true;

        message.DeletedAt =
            DateTime.UtcNow;

        message.DeletedBy =
            senderName;


        // --------------------------------------------------------
        // REPLACE MESSAGE CONTENT
        // --------------------------------------------------------

        message.Text =
            "[This message was deleted]";


        // --------------------------------------------------------
        // REMOVE REPLY SNAPSHOT
        // --------------------------------------------------------

        message.ReplyToMessageId =
            null;

        message.ReplyToSender =
            null;

        message.ReplyToText =
            null;


        await db.SaveChangesAsync();


        return true;
    }
}