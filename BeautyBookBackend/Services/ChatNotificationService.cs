using System.Text.Json;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed class ChatNotificationService : IChatNotificationService
{
    private readonly ApplicationDbContext _db;

    public ChatNotificationService(ApplicationDbContext db) => _db = db;

    public async Task QueueMessageAsync(ChatRoom room, Message message, CancellationToken cancellationToken = default)
    {
        var recipientId = message.SenderId == room.CustomerId ? room.MUAId : room.CustomerId;
        var senderName = await _db.Users.AsNoTracking()
            .Where(user => user.UserId == message.SenderId)
            .Select(user => user.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? "BBook";

        var body = !string.IsNullOrWhiteSpace(message.Content)
            ? message.Content.Trim()
            : "Đã gửi cho bạn một hình ảnh.";
        if (body.Length > 160) body = $"{body[..157]}...";

        var notificationId = Guid.NewGuid();
        _db.AppNotifications.Add(new AppNotification
        {
            Id = notificationId,
            UserId = recipientId,
            MessageId = message.MessageId,
            Type = "CHAT_MESSAGE",
            Title = senderName,
            Body = body,
            DataJson = JsonSerializer.Serialize(new
            {
                url = $"/chat/{room.ChatRoomId}",
                chatRoomId = room.ChatRoomId,
                messageId = message.MessageId,
                senderId = message.SenderId,
                notificationId
            }),
            ScheduledAt = DateTime.UtcNow,
            Status = await new PlayReviewPolicy(_db).ExternalDeliveryAllowedAsync(new AppNotification { UserId = recipientId, MessageId = message.MessageId }) ? "Pending" : "Skipped",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
