using ChatApp.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Web.Hubs;

public sealed class ChatHub(
    ChatService chatService,
    PushNotificationQueue pushNotificationQueue,
    SupabaseStorageService supabaseStorageService) : Hub
{
    // ============================================================
    // PRESENCE STATE
    // ============================================================

    private static readonly object PresenceLock = new();

    private static readonly Dictionary<
        string,
        Dictionary<string, HashSet<string>>>
        RoomConnections =
            new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<
        string,
        PresenceInfo>
        ConnectionPresence =
            new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<
        string,
        DateTime>
        ConnectionLastSeen =
            new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan StaleConnectionTimeout =
        TimeSpan.FromSeconds(45);

    private sealed record PresenceInfo(
        string RoomCode,
        string UserName);


    // ============================================================
    // JOIN ROOM
    // ============================================================

    public async Task JoinRoom(
        string roomCode,
        string userName)
    {
        roomCode =
            roomCode
                .Trim()
                .ToLowerInvariant();

        userName =
            userName.Trim();

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new HubException(
                "User name is required.");
        }

        if (userName.Length > 40)
        {
            throw new HubException(
                "User name is too long.");
        }

        if (!await chatService.RoomExistsAsync(roomCode))
        {
            throw new HubException(
                "Room not found.");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            roomCode);

        List<string> onlineUsers;

        bool becameOnline = false;

        lock (PresenceLock)
        {
            if (!RoomConnections.TryGetValue(
                    roomCode,
                    out var roomUsers))
            {
                roomUsers =
                    new Dictionary<
                        string,
                        HashSet<string>>(
                        StringComparer.OrdinalIgnoreCase);

                RoomConnections[roomCode] =
                    roomUsers;
            }

            if (!roomUsers.TryGetValue(
                    userName,
                    out var connections))
            {
                connections =
                    new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);

                roomUsers[userName] =
                    connections;

                becameOnline = true;
            }

            connections.Add(
                Context.ConnectionId);

            ConnectionPresence[
                Context.ConnectionId] =
                new PresenceInfo(
                    roomCode,
                    userName);

            ConnectionLastSeen[
                Context.ConnectionId] =
                DateTime.UtcNow;

            onlineUsers =
                roomUsers
                    .Where(
                        x =>
                            x.Value.Count > 0)
                    .Select(
                        x =>
                            x.Key)
                    .OrderBy(
                        x =>
                            x,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        await Clients.Caller.SendAsync(
            "RoomPresenceSnapshot",
            onlineUsers);

        if (becameOnline)
        {
            await Clients.Group(roomCode)
                .SendAsync(
                    "UserPresenceChanged",
                    userName,
                    true);
        }
    }


    // ============================================================
    // HEARTBEAT
    // ============================================================

    public Task PresenceHeartbeat()
    {
        lock (PresenceLock)
        {
            if (ConnectionPresence.ContainsKey(
                    Context.ConnectionId))
            {
                ConnectionLastSeen[
                    Context.ConnectionId] =
                    DateTime.UtcNow;
            }
        }

        return Task.CompletedTask;
    }


    // ============================================================
    // SEND MESSAGE
    // ============================================================

    public async Task SendMessage(
        string roomCode,
        string sender,
        string text,
        long? replyToMessageId = null)
    {
        roomCode =
            roomCode
                .Trim()
                .ToLowerInvariant();

        sender =
            sender.Trim();

        text =
            text.Trim();

        if (string.IsNullOrWhiteSpace(sender))
        {
            throw new HubException(
                "Sender name is required.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new HubException(
                "Message cannot be empty.");
        }

        if (text.Length > 2000)
        {
            throw new HubException(
                "Message is too long.");
        }

        if (!await chatService.RoomExistsAsync(roomCode))
        {
            throw new HubException(
                "Room not found.");
        }

        var message =
            await chatService.SaveMessageAsync(
                roomCode,
                sender,
                text,
                replyToMessageId);

        await Clients.Group(roomCode)
            .SendAsync(
                "ReceiveMessage",
                new
                {
                    id =
                        message.Id,

                    sender =
                        message.Sender,

                    text =
                        message.Text,

                    sentAt =
                        message.SentAt.ToString("O"),

                    isRead =
                        message.IsRead,

                    isDelivered =
                        false,

                    replyToMessageId =
                        message.ReplyToMessageId,

                    replyToSender =
                        message.ReplyToSender,

                    replyToText =
                        message.ReplyToText,

                    isDeleted =
                        message.IsDeleted,

                    reactions =
                        Array.Empty<object>()
                });

        try
        {
            await pushNotificationQueue.EnqueueAsync(
                new PushNotificationJob(
                    roomCode,
                    sender,
                    message));
        }
        catch
        {
            // Push notification failure must never
            // break normal chat messaging.
        }
    }


    // ============================================================
    // SEND PHOTO
    // ============================================================
    //
    // Supported:
    //
    // Normal
    // ViewOnce
    //
    // Timed is intentionally NOT supported yet.
    // ============================================================

    public async Task SendPhoto(
        string roomCode,
        string sender,
        byte[] fileBytes,
        string fileName,
        string contentType,
        string photoMode = "Normal",
        long? replyToMessageId = null)
    {
        // --------------------------------------------------------
        // NORMALIZE INPUT
        // --------------------------------------------------------

        roomCode =
            roomCode
                .Trim()
                .ToLowerInvariant();

        sender =
            sender.Trim();

        fileName =
            Path.GetFileName(
                fileName.Trim());

        contentType =
            contentType.Trim();

        photoMode =
            photoMode.Trim();


        // --------------------------------------------------------
        // BASIC VALIDATION
        // --------------------------------------------------------

        if (string.IsNullOrWhiteSpace(sender))
        {
            throw new HubException(
                "Sender name is required.");
        }

        if (sender.Length > 40)
        {
            throw new HubException(
                "Sender name is too long.");
        }


        // --------------------------------------------------------
        // ROOM CHECK
        // --------------------------------------------------------

        try
        {
            if (!await chatService.RoomExistsAsync(
                    roomCode))
            {
                throw new HubException(
                    "Room not found.");
            }
        }
        catch (HubException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new HubException(
                $"Room check failed: {ex.Message}");
        }


        // --------------------------------------------------------
        // PHOTO MODE
        // --------------------------------------------------------

        if (string.IsNullOrWhiteSpace(photoMode))
        {
            photoMode =
                "Normal";
        }

        if (!string.Equals(
                photoMode,
                "Normal",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                photoMode,
                "ViewOnce",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new HubException(
                $"Unsupported photo mode: {photoMode}");
        }

        photoMode =
            string.Equals(
                photoMode,
                "ViewOnce",
                StringComparison.OrdinalIgnoreCase)
                ? "ViewOnce"
                : "Normal";


        // --------------------------------------------------------
        // FILE VALIDATION
        // --------------------------------------------------------

        if (fileBytes is null ||
            fileBytes.Length == 0)
        {
            throw new HubException(
                "Photo file is empty.");
        }

        const long MaxPhotoSize =
            10 * 1024 * 1024;

        if (fileBytes.Length > MaxPhotoSize)
        {
            throw new HubException(
                $"Photo size cannot exceed 10 MB. " +
                $"Current size: " +
                $"{fileBytes.Length / 1024d / 1024d:0.##} MB");
        }


        // --------------------------------------------------------
        // CONTENT TYPE VALIDATION
        // --------------------------------------------------------

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new HubException(
                "Photo content type is empty.");
        }

        if (!contentType.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new HubException(
                $"Only image files are allowed. " +
                $"Received: {contentType}");
        }


        // --------------------------------------------------------
        // FILE NAME VALIDATION
        // --------------------------------------------------------

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new HubException(
                "Photo file name is required.");
        }

        if (fileName.Length > 200)
        {
            throw new HubException(
                "Photo file name is too long.");
        }


        // --------------------------------------------------------
        // GET EXTENSION
        // --------------------------------------------------------

        var extension =
            Path.GetExtension(fileName)
                .ToLowerInvariant();

        var allowedExtensions =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ".jpg",
                ".jpeg",
                ".png",
                ".gif",
                ".webp"
            };

        if (!allowedExtensions.Contains(
                extension))
        {
            throw new HubException(
                $"Unsupported image format: {extension}");
        }


        // --------------------------------------------------------
        // SAFE STORAGE FILE NAME
        // --------------------------------------------------------

        var storageFileName =
            $"{Guid.NewGuid():N}{extension}";

        var storagePath =
            $"rooms/{roomCode}/{storageFileName}";


        // ========================================================
        // SUPABASE UPLOAD
        // ========================================================

        string mediaPath;

        try
        {
            mediaPath =
                await supabaseStorageService
                    .UploadPhotoAsync(
                        fileBytes,
                        storagePath,
                        contentType);
        }
        catch (Exception ex)
        {
            throw new HubException(
                $"Supabase photo upload failed: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(
                mediaPath))
        {
            throw new HubException(
                "Supabase upload completed but " +
                "returned an empty media path.");
        }


        // ========================================================
        // SAVE PHOTO METADATA
        // ========================================================

        ChatApp.Web.Data.ChatMessage message;

        try
        {
            message =
                await chatService.SavePhotoAsync(
                    roomCode,
                    sender,
                    mediaPath,
                    contentType,
                    fileName,
                    fileBytes.LongLength,
                    photoMode,
                    replyToMessageId);
        }
        catch (Exception ex)
        {
            throw new HubException(
                $"Photo database save failed: {ex.Message}");
        }

        if (message is null)
        {
            throw new HubException(
                "Photo was saved but no message was returned.");
        }


        // ========================================================
        // REAL-TIME SIGNALR MESSAGE
        // ========================================================

        try
        {
            await Clients.Group(roomCode)
                .SendAsync(
                    "ReceiveMessage",
                    new
                    {
                        id =
                            message.Id,

                        sender =
                            message.Sender,

                        text =
                            message.Text,

                        sentAt =
                            message.SentAt.ToString("O"),

                        isRead =
                            message.IsRead,

                        isDelivered =
                            false,

                        // PHOTO DATA
                        messageType =
                            message.MessageType,

                        mediaPath =
                            message.MediaPath,

                        mediaContentType =
                            message.MediaContentType,

                        mediaFileName =
                            message.MediaFileName,

                        mediaSize =
                            message.MediaSize,

                        photoMode =
                            message.PhotoMode,

                        expiresAt =
                            message.ExpiresAt,

                        viewedAt =
                            message.ViewedAt,

                        // REPLY
                        replyToMessageId =
                            message.ReplyToMessageId,

                        replyToSender =
                            message.ReplyToSender,

                        replyToText =
                            message.ReplyToText,

                        // DELETE
                        isDeleted =
                            message.IsDeleted,

                        // REACTIONS
                        reactions =
                            Array.Empty<object>()
                    });
        }
        catch (Exception ex)
        {
            throw new HubException(
                $"Photo was saved, but SignalR broadcast failed: {ex.Message}");
        }


        // ========================================================
        // WEB PUSH NOTIFICATION
        // ========================================================
        //
        // Push failure MUST NOT make photo sending fail.
        // ========================================================

        try
        {
            await pushNotificationQueue.EnqueueAsync(
                new PushNotificationJob(
                    roomCode,
                    sender,
                    message));
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Push notification queue failed for photo: " +
                $"{ex.Message}");
        }
    }


    // ============================================================
    // MESSAGE DELIVERED
    // ============================================================

    public async Task ConfirmMessageDelivered(
        string roomCode,
        long messageId,
        string receiverName)
    {
        roomCode =
            roomCode
                .Trim()
                .ToLowerInvariant();

        receiverName =
            receiverName.Trim();

        if (string.IsNullOrWhiteSpace(receiverName))
        {
            return;
        }

        if (!await chatService.RoomExistsAsync(
                roomCode))
        {
            return;
        }

        await Clients.Group(roomCode)
            .SendAsync(
                "MessageDelivered",
                messageId);
    }


    // ============================================================
    // USER TYPING
    // ============================================================

    public Task UserTyping(
        string roomCode,
        string sender,
        bool isTyping)
    {
        roomCode =
            roomCode
                .Trim()
                .ToLowerInvariant();

        return Clients
            .OthersInGroup(roomCode)
            .SendAsync(
                "UserTyping",
                sender,
                isTyping);
    }


    // ============================================================
    // MARK MESSAGE AS READ
    // ============================================================

    public async Task MarkMessageAsRead(
        string roomCode,
        long messageId,
        string readerName)
    {
        roomCode =
            roomCode
                .Trim()
                .ToLowerInvariant();

        readerName =
            readerName.Trim();

        if (!await chatService.RoomExistsAsync(
                roomCode))
        {
            throw new HubException(
                "Room not found.");
        }

        var marked =
            await chatService.MarkMessageAsReadAsync(
                roomCode,
                messageId,
                readerName);

        if (!marked)
        {
            return;
        }

        await Clients.Group(roomCode)
            .SendAsync(
                "MessageRead",
                messageId);
    }


    // ============================================================
    // DELETE FOR ME
    // ============================================================

    public async Task DeleteMessageForMe(
        string roomCode,
        long messageId,
        string userName)
    {
        roomCode =
            roomCode
                .Trim()
                .ToLowerInvariant();

        userName =
            userName.Trim();

        if (!await chatService.RoomExistsAsync(
                roomCode))
        {
            throw new HubException(
                "Room not found.");
        }

        var deleted =
            await chatService.DeleteForMeAsync(
                roomCode,
                messageId,
                userName);

        if (!deleted)
        {
            throw new HubException(
                "Unable to delete this message.");
        }

        await Clients.Caller.SendAsync(
            "MessageDeletedForMe",
            messageId);
    }


    // ============================================================
    // UNSEND
    // ============================================================

    public async Task UnsendMessage(
        string roomCode,
        long messageId,
        string senderName)
    {
        roomCode =
            roomCode
                .Trim()
                .ToLowerInvariant();

        senderName =
            senderName.Trim();

        if (!await chatService.RoomExistsAsync(
                roomCode))
        {
            throw new HubException(
                "Room not found.");
        }

        var deleted =
            await chatService.DeleteForEveryoneAsync(
                roomCode,
                messageId,
                senderName);

        if (!deleted)
        {
            throw new HubException(
                "You can only unsend your own message.");
        }

        await Clients.Group(roomCode)
            .SendAsync(
                "MessageUnsent",
                messageId);
    }


    // ============================================================
    // TOGGLE REACTION
    // ============================================================

    public async Task ToggleReaction(
        string roomCode,
        long messageId,
        string userName,
        string reaction)
    {
        roomCode =
            roomCode
                .Trim()
                .ToLowerInvariant();

        userName =
            userName.Trim();

        reaction =
            reaction.Trim();

        if (!await chatService.RoomExistsAsync(
                roomCode))
        {
            throw new HubException(
                "Room not found.");
        }

        var result =
            await chatService.ToggleReactionAsync(
                roomCode,
                messageId,
                userName,
                reaction);

        await Clients.Group(roomCode)
            .SendAsync(
                "MessageReactionChanged",
                messageId,
                result.Reactions);
    }


    // ============================================================
    // REMOVE STALE CONNECTIONS
    // ============================================================

    public static List<OfflinePresence>
        RemoveStaleConnections()
    {
        var offlineUsers =
            new List<OfflinePresence>();

        var now =
            DateTime.UtcNow;

        lock (PresenceLock)
        {
            var staleConnections =
                ConnectionLastSeen
                    .Where(
                        x =>
                            now - x.Value >
                            StaleConnectionTimeout)
                    .Select(
                        x =>
                            x.Key)
                    .ToList();

            foreach (var connectionId
                in staleConnections)
            {
                if (!ConnectionPresence.TryGetValue(
                        connectionId,
                        out var presence))
                {
                    ConnectionLastSeen.Remove(
                        connectionId);

                    continue;
                }

                ConnectionPresence.Remove(
                    connectionId);

                ConnectionLastSeen.Remove(
                    connectionId);

                if (!RoomConnections.TryGetValue(
                        presence.RoomCode,
                        out var roomUsers))
                {
                    continue;
                }

                if (!roomUsers.TryGetValue(
                        presence.UserName,
                        out var connections))
                {
                    continue;
                }

                connections.Remove(
                    connectionId);

                if (connections.Count == 0)
                {
                    roomUsers.Remove(
                        presence.UserName);

                    offlineUsers.Add(
                        new OfflinePresence(
                            presence.RoomCode,
                            presence.UserName));
                }

                if (roomUsers.Count == 0)
                {
                    RoomConnections.Remove(
                        presence.RoomCode);
                }
            }
        }

        return offlineUsers;
    }


    // ============================================================
    // DISCONNECTED
    // ============================================================

    public override async Task OnDisconnectedAsync(
        Exception? exception)
    {
        PresenceInfo? presence =
            null;

        bool becameOffline =
            false;

        lock (PresenceLock)
        {
            if (ConnectionPresence.TryGetValue(
                    Context.ConnectionId,
                    out var currentPresence))
            {
                presence =
                    currentPresence;

                ConnectionPresence.Remove(
                    Context.ConnectionId);

                ConnectionLastSeen.Remove(
                    Context.ConnectionId);

                if (RoomConnections.TryGetValue(
                        presence.RoomCode,
                        out var roomUsers))
                {
                    if (roomUsers.TryGetValue(
                            presence.UserName,
                            out var connections))
                    {
                        connections.Remove(
                            Context.ConnectionId);

                        if (connections.Count == 0)
                        {
                            roomUsers.Remove(
                                presence.UserName);

                            becameOffline =
                                true;
                        }
                    }

                    if (roomUsers.Count == 0)
                    {
                        RoomConnections.Remove(
                            presence.RoomCode);
                    }
                }
            }
        }

        if (becameOffline &&
            presence is not null)
        {
            await Clients.Group(
                    presence.RoomCode)
                .SendAsync(
                    "UserPresenceChanged",
                    presence.UserName,
                    false);
        }

        await base.OnDisconnectedAsync(
            exception);
    }


    // ============================================================
    // OFFLINE PRESENCE RESULT
    // ============================================================

    public sealed record OfflinePresence(
        string RoomCode,
        string UserName);
}