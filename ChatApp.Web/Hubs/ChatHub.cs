using ChatApp.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Web.Hubs;

public sealed class ChatHub(
    ChatService chatService)
    : Hub
{
    // ============================================================
    // JOIN ROOM
    // ============================================================

    public async Task JoinRoom(
        string roomCode)
    {
        roomCode =
            roomCode.Trim()
                .ToLowerInvariant();

        if (!await chatService.RoomExistsAsync(
                roomCode))
        {
            throw new HubException(
                "Room not found.");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            roomCode);
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
            roomCode.Trim()
                .ToLowerInvariant();

        var message =
            await chatService.SaveMessageAsync(
                roomCode,
                sender,
                text,
                replyToMessageId);

        await Clients
            .Group(roomCode)
            .SendAsync(
                "ReceiveMessage",
                new
                {
                    id = message.Id,

                    sender = message.Sender,

                    text = message.Text,

                    sentAt =
                        message.SentAt.ToString("O"),

                    isRead =
                        message.IsRead,

                    replyToMessageId =
                        message.ReplyToMessageId,

                    replyToSender =
                        message.ReplyToSender,

                    replyToText =
                        message.ReplyToText,

                    isDeleted =
                        message.IsDeleted
                });
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
            roomCode.Trim()
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
            roomCode.Trim()
                .ToLowerInvariant();

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

        await Clients
            .Group(roomCode)
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
            roomCode.Trim()
                .ToLowerInvariant();

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

        // Notify only the current connection.
        await Clients
            .Caller
            .SendAsync(
                "MessageDeletedForMe",
                messageId);
    }


    // ============================================================
    // DELETE FOR EVERYONE / UNSEND
    // ============================================================

    public async Task UnsendMessage(
        string roomCode,
        long messageId,
        string senderName)
    {
        roomCode =
            roomCode.Trim()
                .ToLowerInvariant();

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

        await Clients
            .Group(roomCode)
            .SendAsync(
                "MessageUnsent",
                messageId);
    }
}