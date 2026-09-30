using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs.Chat;
using BeautyBookBackend.Models;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;
using BeautyBookBackend.Models.Enums;
using Microsoft.Extensions.Logging;

namespace BeautyBookBackend.Services
{
    public class ChatService : IChatService
    {
        private readonly IChatRepository _chatRepository;
        private readonly ApplicationDbContext _context;
        private readonly IChatNotificationService _chatNotifications;
        private readonly ILogger<ChatService> _logger;

        public ChatService(IChatRepository chatRepository, ApplicationDbContext context, IChatNotificationService chatNotifications, ILogger<ChatService> logger)
        {
            _chatRepository = chatRepository;
            _context = context;
            _chatNotifications = chatNotifications;
            _logger = logger;
        }

        public async Task<ChatRoomDto> GetOrCreateChatRoomAsync(Guid customerId, Guid muaId)
        {
            if (customerId == muaId) throw new ArgumentException("Không thể tự tạo cuộc trò chuyện với chính mình.");
            var customer = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == customerId && x.IsActive && x.DeletedAt == null);
            if (customer == null || customer.Role == UserRole.Admin) throw new UnauthorizedAccessException("Tài khoản không thể tạo cuộc trò chuyện này.");
            var mua = await _context.MakeupArtistProfiles.AsNoTracking().Include(x => x.User)
                .FirstOrDefaultAsync(x => x.MUAId == muaId && x.User != null && x.User.IsActive && x.User.DeletedAt == null);
            if (mua == null) throw new ArgumentException("Không tìm thấy chuyên gia trang điểm hợp lệ.");
            var room = await _chatRepository.GetOrCreateChatRoomAsync(customerId, muaId);
            return MapToChatRoomDto(room);
        }

        public async Task<IEnumerable<ChatRoomDto>> GetChatRoomsByUserIdAsync(Guid userId)
        {
            var rooms = await _chatRepository.GetChatRoomsByUserIdAsync(userId);
            var dtos = new List<ChatRoomDto>();

            foreach (var room in rooms)
            {
                var dto = MapToChatRoomDto(room);
                // Get last message
                var lastMsg = await _chatRepository.GetLastMessageAsync(room.ChatRoomId);
                if (lastMsg != null)
                {
                    dto.LastMessage = MapToMessageDto(lastMsg, userId);
                }
                dto.UnreadCount = await _chatRepository.GetUnreadCountAsync(room.ChatRoomId, userId);
                dtos.Add(dto);
            }

            return dtos.OrderByDescending(d => d.LastMessage?.SentAt ?? d.CreatedAt);
        }

        public async Task<IEnumerable<MessageDto>> GetMessagesByRoomIdAsync(Guid roomId, Guid userId, DateTime? before = null, int limit = 50)
        {
            var room = await _chatRepository.GetChatRoomByIdAsync(roomId);
            if (room == null)
            {
                throw new Exception("Chat room not found.");
            }

            // Verify user is part of the room
            if (room.CustomerId != userId && room.MUAId != userId)
            {
                throw new UnauthorizedAccessException("You are not authorized to view these messages.");
            }

            var messages = await _chatRepository.GetMessagesByRoomIdAsync(roomId, before, limit);
            return messages.Select(m => MapToMessageDto(m, userId));
        }

        public async Task EnsureRoomAccessAsync(Guid roomId, Guid userId)
        {
            var room = await _chatRepository.GetChatRoomByIdAsync(roomId);
            if (room == null || (room.CustomerId != userId && room.MUAId != userId))
                throw new UnauthorizedAccessException("Bạn không thuộc cuộc trò chuyện này.");
        }

        public async Task<(Guid CustomerId, Guid MuaId)> GetParticipantsAsync(Guid roomId, Guid userId)
        {
            var room = await _chatRepository.GetChatRoomByIdAsync(roomId);
            if (room == null || (room.CustomerId != userId && room.MUAId != userId))
                throw new UnauthorizedAccessException("Bạn không thuộc cuộc trò chuyện này.");
            return (room.CustomerId, room.MUAId);
        }

        public async Task<int> MarkReadAsync(Guid roomId, Guid userId)
        {
            await EnsureRoomAccessAsync(roomId, userId);
            var now = DateTime.UtcNow;
            return await _context.Messages.Where(x => x.ChatRoomId == roomId && x.SenderId != userId && !x.IsRead)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsRead, true).SetProperty(x => x.ReadAt, now));
        }

        public async Task<MessageDto> SendMessageAsync(Guid roomId, Guid senderId, string? content, string? imageUrl, Guid? replyToMessageId)
        {
            var room = await _chatRepository.GetChatRoomByIdAsync(roomId);
            if (room == null)
            {
                throw new Exception("Chat room not found.");
            }

            if (room.CustomerId != senderId && room.MUAId != senderId)
            {
                throw new UnauthorizedAccessException("You are not part of this chat room.");
            }

            if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(imageUrl))
                throw new ArgumentException("Tin nhắn phải có nội dung hoặc hình ảnh.");
            if (content?.Trim().Length > 2000) throw new ArgumentException("Tin nhắn không được vượt quá 2000 ký tự.");
            if (imageUrl?.Length > 1000 || (!string.IsNullOrWhiteSpace(imageUrl) && !Uri.TryCreate(imageUrl, UriKind.Absolute, out _)))
                throw new ArgumentException("Đường dẫn hình ảnh không hợp lệ.");

            Message? replyTo = null;
            if (replyToMessageId.HasValue)
            {
                replyTo = await _context.Messages.FirstOrDefaultAsync(m => m.MessageId == replyToMessageId && m.ChatRoomId == roomId);
                if (replyTo == null) throw new ArgumentException("Tin nhắn phản hồi không hợp lệ.");
            }

            var message = new Message
            {
                MessageId = Guid.NewGuid(),
                ChatRoomId = roomId,
                SenderId = senderId,
                Content = content?.Trim(),
                ImageUrl = imageUrl,
                ReplyToMessageId = replyToMessageId,
                ReplyToMessage = replyTo,
                SentAt = DateTime.UtcNow,
                IsRead = false
            };

            var savedMessage = await _chatRepository.AddMessageAsync(message);
            try
            {
                await _chatNotifications.QueueMessageAsync(room, savedMessage);
            }
            catch (Exception ex)
            {
                // Push is best-effort. A notification/storage outage must never make
                // a successfully persisted chat message look failed to the sender.
                _logger.LogWarning(ex, "Unable to queue push notification for chat message {MessageId}", savedMessage.MessageId);
            }
            return MapToMessageDto(savedMessage, senderId);
        }

        public async Task<MessageDto> ToggleReactionAsync(Guid roomId, Guid messageId, Guid userId, string emoji)
        {
            emoji = emoji.Trim();
            var allowedEmoji = new HashSet<string> { "❤️", "👍", "😀", "😂", "😍", "🔥", "👏", "😢" };
            if (!allowedEmoji.Contains(emoji)) throw new ArgumentException("Cảm xúc không được hỗ trợ.");
            var room = await _chatRepository.GetChatRoomByIdAsync(roomId);
            if (room == null || (room.CustomerId != userId && room.MUAId != userId))
                throw new UnauthorizedAccessException();
            var message = await _context.Messages.Include(m => m.ReplyToMessage).Include(m => m.Reactions)
                .FirstOrDefaultAsync(m => m.MessageId == messageId && m.ChatRoomId == roomId)
                ?? throw new ArgumentException("Không tìm thấy tin nhắn.");
            var existing = message.Reactions.FirstOrDefault(r => r.UserId == userId);
            if (existing != null && existing.Emoji == emoji) _context.MessageReactions.Remove(existing);
            else if (existing != null) existing.Emoji = emoji;
            else _context.MessageReactions.Add(new MessageReaction { MessageId = messageId, UserId = userId, Emoji = emoji });
            await _context.SaveChangesAsync();
            await _context.Entry(message).Collection(m => m.Reactions).LoadAsync();
            return MapToMessageDto(message, userId);
        }

        private ChatRoomDto MapToChatRoomDto(ChatRoom room)
        {
            return new ChatRoomDto
            {
                ChatRoomId = room.ChatRoomId,
                CustomerId = room.CustomerId,
                CustomerName = room.Customer?.FullName ?? "Khách Hàng",
                CustomerAvatar = room.Customer?.AvatarUrl,
                MUAId = room.MUAId,
                MUAName = room.MakeupArtistProfile?.User?.FullName ?? "Chuyên Gia Trang Điểm",
                MUAAvatar = room.MakeupArtistProfile?.User?.AvatarUrl,
                CreatedAt = room.CreatedAt
            };
        }

        private MessageDto MapToMessageDto(Message message, Guid? currentUserId = null)
        {
            return new MessageDto
            {
                MessageId = message.MessageId,
                ChatRoomId = message.ChatRoomId,
                SenderId = message.SenderId,
                Content = message.Content,
                SentAt = message.SentAt,
                IsRead = message.IsRead
                ,ReadAt = message.ReadAt
                ,ImageUrl = message.ImageUrl
                ,ReplyToMessageId = message.ReplyToMessageId
                ,ReplyToContent = message.ReplyToMessage?.Content
                ,ReplyToImageUrl = message.ReplyToMessage?.ImageUrl
                ,Reactions = message.Reactions.GroupBy(r => r.Emoji).Select(g => new MessageReactionDto
                {
                    Emoji = g.Key,
                    Count = g.Count(),
                    ReactedByMe = currentUserId.HasValue && g.Any(r => r.UserId == currentUserId.Value)
                    ,UserIds = g.Select(r => r.UserId).ToList()
                }).ToList()
            };
        }
    }
}
