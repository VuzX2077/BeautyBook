using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeautyBookBackend.Models;

namespace BeautyBookBackend.Repositories
{
    public interface IChatRepository
    {
        Task<ChatRoom> GetOrCreateChatRoomAsync(Guid customerId, Guid muaId);
        Task<IEnumerable<ChatRoom>> GetChatRoomsByUserIdAsync(Guid userId);
        Task<IEnumerable<Message>> GetMessagesByRoomIdAsync(Guid roomId, DateTime? before = null, int limit = 50);
        Task<Message?> GetLastMessageAsync(Guid roomId);
        Task<int> GetUnreadCountAsync(Guid roomId, Guid userId);
        Task<Message> AddMessageAsync(Message message);
        Task<ChatRoom?> GetChatRoomByIdAsync(Guid roomId);
        Task SaveChangesAsync();
    }
}
