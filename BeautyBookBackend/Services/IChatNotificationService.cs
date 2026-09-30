using BeautyBookBackend.Models;

namespace BeautyBookBackend.Services;

public interface IChatNotificationService
{
    Task QueueMessageAsync(ChatRoom room, Message message, CancellationToken cancellationToken = default);
}
