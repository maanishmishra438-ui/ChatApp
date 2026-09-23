using ChatApp.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Web.Hubs;

public sealed class ChatHub(
    ChatService chatService,
    PushNotificationQueue pushNotificationQueue) : Hub
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

    // ------------------------------------------------------------
    // Last heartbeat/activity received from each connection.
    // This is NOT stored in the database.
    // ------------------------------------------------------------

    private static readonly Dictionary<
        string,
        DateTime>
        ConnectionLastSeen =
            new(StringComparer.OrdinalIgnoreCase);


    // ------------------------------------------------------------
    // If a connection does not send a heartbeat for this long,
    // it is considered stale/offline.
    //
    // Client heartbeat will normally be sent every 10 seconds.
    // 45 seconds gives enough tolerance for browser throttling.
    // ------------------------------------------------------------

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


        // --------------------------------------------------------
        // Add connection to SignalR group
        // --------------------------------------------------------

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            roomCode);


        List<string> onlineUsers;

        bool becameOnline = false;


        lock (PresenceLock)
        {
            // ----------------------------------------------------
            // Get/create room
            // ----------------------------------------------------

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


            // ----------------------------------------------------
            // Get/create user's connections
            // ----------------------------------------------------

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


            // ----------------------------------------------------
            // Add this SignalR connection
            // ----------------------------------------------------

            connections.Add(
                Context.ConnectionId);


            // ----------------------------------------------------
            // Store connection presence
            // ----------------------------------------------------

            ConnectionPresence[
                Context.ConnectionId] =
                new PresenceInfo(
                    roomCode,
                    userName);


            // ----------------------------------------------------
            // Mark connection alive
            // ----------------------------------------------------

            ConnectionLastSeen[
                Context.ConnectionId] =
                DateTime.UtcNow;


            // ----------------------------------------------------
            // Build online user list
            // ----------------------------------------------------

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


        // --------------------------------------------------------
        // Send current online users to caller
        // --------------------------------------------------------

        await Clients.Caller.SendAsync(
            "RoomPresenceSnapshot",
            onlineUsers);


        // --------------------------------------------------------
        // Tell everyone when a user becomes online
        // --------------------------------------------------------

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
    //
    // Client calls this periodically.
    //
    // IMPORTANT:
    // Nothing is written to the database.
    //
    // This only updates in-memory presence.
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


        // --------------------------------------------------------
        // SAVE MESSAGE
        // --------------------------------------------------------

        var message =
            await chatService.SaveMessageAsync(
                roomCode,
                sender,
                text,
                replyToMessageId);


        // --------------------------------------------------------
        // REAL-TIME SIGNALR MESSAGE
        // --------------------------------------------------------

        await Clients.Group(roomCode)
            .SendAsync(
                "ReceiveMessage",
                new
                {
                    id = message.Id,
                    sender = message.Sender,
                    text = message.Text,
                    sentAt =
                        message.SentAt.ToString("O"),
                    isRead = message.IsRead,
                    isDelivered = false,
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


        // --------------------------------------------------------
        // WEB PUSH NOTIFICATION
        // --------------------------------------------------------

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


        if (!await chatService.RoomExistsAsync(roomCode))
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


        if (!await chatService.RoomExistsAsync(roomCode))
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


        if (!await chatService.RoomExistsAsync(roomCode))
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


        if (!await chatService.RoomExistsAsync(roomCode))
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


        if (!await chatService.RoomExistsAsync(roomCode))
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
    //
    // Called by PresenceCleanupService every 15 seconds.
    //
    // If a connection has not sent heartbeat for 45 seconds,
    // we consider that connection dead/stale.
    //
    // Returns users that became completely offline.
    // ============================================================

    public static List<OfflinePresence> RemoveStaleConnections()
    {
        var offlineUsers =
            new List<OfflinePresence>();


        var now =
            DateTime.UtcNow;


        lock (PresenceLock)
        {
            // ----------------------------------------------------
            // Find stale connections
            // ----------------------------------------------------

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


            foreach (var connectionId in staleConnections)
            {
                // ------------------------------------------------
                // Get presence information
                // ------------------------------------------------

                if (!ConnectionPresence.TryGetValue(
                        connectionId,
                        out var presence))
                {
                    ConnectionLastSeen.Remove(
                        connectionId);

                    continue;
                }


                // ------------------------------------------------
                // Remove connection tracking
                // ------------------------------------------------

                ConnectionPresence.Remove(
                    connectionId);

                ConnectionLastSeen.Remove(
                    connectionId);


                // ------------------------------------------------
                // Find room
                // ------------------------------------------------

                if (!RoomConnections.TryGetValue(
                        presence.RoomCode,
                        out var roomUsers))
                {
                    continue;
                }


                // ------------------------------------------------
                // Find user
                // ------------------------------------------------

                if (!roomUsers.TryGetValue(
                        presence.UserName,
                        out var connections))
                {
                    continue;
                }


                // ------------------------------------------------
                // Remove stale connection
                // ------------------------------------------------

                connections.Remove(
                    connectionId);


                // ------------------------------------------------
                // User is offline only when ALL
                // their connections are gone.
                // ------------------------------------------------

                if (connections.Count == 0)
                {
                    roomUsers.Remove(
                        presence.UserName);


                    offlineUsers.Add(
                        new OfflinePresence(
                            presence.RoomCode,
                            presence.UserName));
                }


                // ------------------------------------------------
                // Remove empty room
                // ------------------------------------------------

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
        PresenceInfo? presence = null;

        bool becameOffline = false;


        lock (PresenceLock)
        {
            // ----------------------------------------------------
            // Get presence
            // ----------------------------------------------------

            if (ConnectionPresence.TryGetValue(
                    Context.ConnectionId,
                    out var currentPresence))
            {
                presence =
                    currentPresence;


                // ------------------------------------------------
                // Remove connection tracking
                // ------------------------------------------------

                ConnectionPresence.Remove(
                    Context.ConnectionId);

                ConnectionLastSeen.Remove(
                    Context.ConnectionId);


                // ------------------------------------------------
                // Remove from room
                // ------------------------------------------------

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


                        // ----------------------------------------
                        // No other connection for this user
                        // ----------------------------------------

                        if (connections.Count == 0)
                        {
                            roomUsers.Remove(
                                presence.UserName);

                            becameOffline = true;
                        }
                    }


                    // --------------------------------------------
                    // Remove empty room
                    // --------------------------------------------

                    if (roomUsers.Count == 0)
                    {
                        RoomConnections.Remove(
                            presence.RoomCode);
                    }
                }
            }
        }


        // --------------------------------------------------------
        // Notify room
        // --------------------------------------------------------

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