using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Repositories
{
    public class ChatRepository : IChatRepository
    {
        private readonly ApplicationDbContext _context;

        public ChatRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<ChatRoom> GetOrCreateChatRoomAsync(Guid customerId, Guid muaId)
        {
            var room = await _context.ChatRooms
                .Include(r => r.Customer)
                .Include(r => r.MakeupArtistProfile)
                .ThenInclude(m => m!.User)
                .FirstOrDefaultAsync(r => r.CustomerId == customerId && r.MUAId == muaId);

            if (room == null)
            {
                room = new ChatRoom
                {
                    ChatRoomId = Guid.NewGuid(),
                    CustomerId = customerId,
                    MUAId = muaId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.ChatRooms.Add(room);
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    _context.Entry(room).State = EntityState.Detached;
                    room = await _context.ChatRooms
                        .Include(r => r.Customer).Include(r => r.MakeupArtistProfile).ThenInclude(m => m!.User)
                        .FirstOrDefaultAsync(r => r.CustomerId == customerId && r.MUAId == muaId);
                    if (room == null) throw;
                    return room;
                }
                
                // Reload with includes
                room = await _context.ChatRooms
                    .Include(r => r.Customer)
                    .Include(r => r.MakeupArtistProfile)
                    .ThenInclude(m => m!.User)
                    .FirstAsync(r => r.ChatRoomId == room.ChatRoomId);
            }

            return room!;
        }

        public async Task<IEnumerable<ChatRoom>> GetChatRoomsByUserIdAsync(Guid userId)
        {
            return await _context.ChatRooms
                .Include(r => r.Customer)
                .Include(r => r.MakeupArtistProfile)
                .ThenInclude(m => m!.User)
                .Where(r => r.CustomerId == userId || r.MUAId == userId)
                .OrderByDescending(r => r.CreatedAt) // Ideally sort by last message sentAt
                .ToListAsync();
        }

        public async Task<IEnumerable<Message>> GetMessagesByRoomIdAsync(Guid roomId, DateTime? before = null, int limit = 50)
        {
            var query = _context.Messages
                .Include(m => m.ReplyToMessage)
                .Include(m => m.Reactions)
                .Where(m => m.ChatRoomId == roomId);
            if (before.HasValue) query = query.Where(m => m.SentAt < before.Value);
            var messages = await query.OrderByDescending(m => m.SentAt).ThenByDescending(m => m.MessageId)
                .Take(Math.Clamp(limit, 1, 100)).ToListAsync();
            messages.Reverse();
            return messages;
        }

        public Task<Message?> GetLastMessageAsync(Guid roomId) => _context.Messages
            .Include(m => m.ReplyToMessage).Include(m => m.Reactions)
            .Where(m => m.ChatRoomId == roomId).OrderByDescending(m => m.SentAt).ThenByDescending(m => m.MessageId)
            .FirstOrDefaultAsync();

        public Task<int> GetUnreadCountAsync(Guid roomId, Guid userId) => _context.Messages
            .CountAsync(m => m.ChatRoomId == roomId && m.SenderId != userId && !m.IsRead);

        public async Task<Message> AddMessageAsync(Message message)
        {
            _context.Messages.Add(message);
            await _context.SaveChangesAsync();
            await _context.Entry(message).Collection(m => m.Reactions).LoadAsync();
            return message;
        }

        public async Task<ChatRoom?> GetChatRoomByIdAsync(Guid roomId)
        {
            return await _context.ChatRooms
                .Include(r => r.Customer)
                .Include(r => r.MakeupArtistProfile)
                .ThenInclude(m => m!.User)
                .FirstOrDefaultAsync(r => r.ChatRoomId == roomId);
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
