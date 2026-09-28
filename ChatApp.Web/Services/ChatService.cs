using ChatApp.Web.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace ChatApp.Web.Services;

public sealed class ChatService(
    IDbContextFactory<ChatDbContext> factory)
{
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
            text =
                text[..2000];
        }

        await using var db =
            await factory.CreateDbContextAsync();

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
                    x.MessageId ==
                    messageId)
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

        // Reactions are Unicode emoji, not a fixed six-item whitelist.
        // Keep the server-side validation so arbitrary text cannot be stored
        // as a reaction, while allowing the full Unicode emoji range.
        if (!IsValidEmojiReaction(reaction))
        {
            throw new ArgumentException(
                "Invalid reaction.");
        }

        await using var db =
            await factory.CreateDbContextAsync();

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

            removed =
                true;
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
    // SAVE PUSH SUBSCRIPTION
    // ============================================================

    public async Task SavePushSubscriptionAsync(
        string userName,
        string roomCode,
        string endpoint,
        string p256dh,
        string auth)
    {
        userName =
            userName.Trim();

        roomCode =
            roomCode.Trim()
                .ToLowerInvariant();

        endpoint =
            endpoint.Trim();

        p256dh =
            p256dh.Trim();

        auth =
            auth.Trim();


        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException(
                "User name is required.");
        }

        if (string.IsNullOrWhiteSpace(roomCode))
        {
            throw new ArgumentException(
                "Room code is required.");
        }

        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(p256dh) ||
            string.IsNullOrWhiteSpace(auth))
        {
            throw new ArgumentException(
                "Invalid push subscription.");
        }


        await using var db =
            await factory.CreateDbContextAsync();


        var roomExists =
            await db.Rooms.AnyAsync(
                x =>
                    x.Code == roomCode);

        if (!roomExists)
        {
            throw new InvalidOperationException(
                "Room not found.");
        }


        var existing =
            await db.PushSubscriptions
                .FirstOrDefaultAsync(
                    x =>
                        x.UserName == userName &&
                        x.RoomCode == roomCode &&
                        x.Endpoint == endpoint);


        if (existing is null)
        {
            db.PushSubscriptions.Add(
                new PushSubscription
                {
                    UserName =
                        userName,

                    RoomCode =
                        roomCode,

                    Endpoint =
                        endpoint,

                    P256dh =
                        p256dh,

                    Auth =
                        auth,

                    CreatedAt =
                        DateTime.UtcNow
                });
        }
        else
        {
            existing.P256dh =
                p256dh;

            existing.Auth =
                auth;

            existing.CreatedAt =
                DateTime.UtcNow;
        }


        await db.SaveChangesAsync();
    }


    // ============================================================
    // GET PUSH SUBSCRIPTIONS
    // ============================================================

    public async Task<List<PushSubscription>>
        GetPushSubscriptionsAsync(
            string roomCode,
            string senderName)
    {
        roomCode =
            roomCode.Trim()
                .ToLowerInvariant();

        senderName =
            senderName.Trim();


        await using var db =
            await factory.CreateDbContextAsync();


        var subscriptions =
            await db.PushSubscriptions
                .AsNoTracking()
                .Where(
                    x =>
                        x.RoomCode == roomCode)
                .ToListAsync();


        return subscriptions
            .Where(
                x =>
                    !x.UserName.Equals(
                        senderName,
                        StringComparison.OrdinalIgnoreCase))
            .ToList();
    }


    // ============================================================
    // EMOJI REACTION VALIDATION
    // ============================================================

    private static bool IsValidEmojiReaction(
        string reaction)
    {
        if (string.IsNullOrWhiteSpace(reaction))
        {
            return false;
        }

        reaction = reaction.Trim();

        // A reaction should be short enough to represent one emoji or
        // one normal emoji sequence (skin tone, ZWJ, variation selector,
        // flags, etc.).
        if (reaction.Length > 32)
        {
            return false;
        }

        var hasEmojiBase = false;

        foreach (var rune in reaction.EnumerateRunes())
        {
            var value = rune.Value;

            // Unicode emoji blocks commonly used by modern emoji pickers.
            var isEmojiBase =
                value is >= 0x1F000 and <= 0x1FAFF ||
                value is >= 0x2600 and <= 0x27BF ||
                value is >= 0x2300 and <= 0x23FF;

            // Allow emoji modifiers, variation selectors, ZWJ and regional
            // indicator symbols used to build valid emoji sequences.
            var isEmojiComponent =
                value is >= 0x1F3FB and <= 0x1F3FF ||
                value is 0x200D ||
                value is 0xFE0E ||
                value is 0xFE0F ||
                value is >= 0x1F1E6 and <= 0x1F1FF;

            if (isEmojiBase)
            {
                hasEmojiBase = true;
                continue;
            }

            if (!isEmojiComponent)
            {
                return false;
            }
        }

        return hasEmojiBase;
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