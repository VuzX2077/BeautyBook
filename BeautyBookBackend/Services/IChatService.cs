using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs.Chat;

namespace BeautyBookBackend.Services
{
    public interface IChatService
    {
        Task<ChatRoomDto> GetOrCreateChatRoomAsync(Guid customerId, Guid muaId);
        Task<IEnumerable<ChatRoomDto>> GetChatRoomsByUserIdAsync(Guid userId);
        Task<IEnumerable<MessageDto>> GetMessagesByRoomIdAsync(Guid roomId, Guid userId, DateTime? before = null, int limit = 50);
        Task<MessageDto> SendMessageAsync(Guid roomId, Guid senderId, string? content, string? imageUrl, Guid? replyToMessageId);
        Task<MessageDto> ToggleReactionAsync(Guid roomId, Guid messageId, Guid userId, string emoji);
        Task EnsureRoomAccessAsync(Guid roomId, Guid userId);
        Task<(Guid CustomerId, Guid MuaId)> GetParticipantsAsync(Guid roomId, Guid userId);
        Task<int> MarkReadAsync(Guid roomId, Guid userId);
    }
}
