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
        private readonly VerificationMediaService? _media;

        public ChatService(IChatRepository chatRepository, ApplicationDbContext context, IChatNotificationService chatNotifications, ILogger<ChatService> logger, VerificationMediaService? media = null)
        {
            _chatRepository = chatRepository;
            _context = context;
            _chatNotifications = chatNotifications;
            _logger = logger;
            _media = media;
        }

        public async Task<ChatRoomDto> GetOrCreateChatRoomAsync(Guid customerId, Guid muaId)
        {
            await using var scope = await ModerationWriteScope.Start(_context, customerId, muaId);
        await new PlayReviewPolicy(_context).EnsureSameDomainAsync(customerId, muaId);
            await new ModerationService(_context).EnsureInteraction(customerId, muaId);
            if (customerId == muaId) throw new ArgumentException("Không thể tự tạo cuộc trò chuyện với chính mình.");
            var customer = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == customerId && x.IsActive && x.DeletedAt == null);
            if (customer == null || customer.Role == UserRole.Admin) throw new UnauthorizedAccessException("Tài khoản không thể tạo cuộc trò chuyện này.");
            var mua = await _context.MakeupArtistProfiles.AsNoTracking().Include(x => x.User)
                .FirstOrDefaultAsync(x => x.MUAId == muaId && x.User != null && x.User.IsActive && x.User.DeletedAt == null);
            if (mua == null) throw new ArgumentException("Không tìm thấy chuyên gia trang điểm hợp lệ.");
            var room = await _chatRepository.GetOrCreateChatRoomAsync(customerId, muaId);
            var dto = MapToChatRoomDto(room, customerId);
            dto.CustomerName = DisplayName(customer.FullName);
            dto.CustomerAvatar = customer.AvatarUrl;
            dto.MUAName = DisplayName(mua.User?.FullName);
            dto.MUAAvatar = mua.User?.AvatarUrl;
            SetOtherParticipant(dto, customerId);
            await scope.Commit();
            return dto;
        }

        public async Task<IEnumerable<ChatRoomDto>> GetChatRoomsByUserIdAsync(Guid userId)
        {
            var rooms = (await _chatRepository.GetChatRoomsByUserIdAsync(userId)).ToList();
            var participantIds = rooms.SelectMany(r => new[] { r.CustomerId, r.MUAId }).Distinct().ToArray();
            // Authoritative user identity, independent of role or optional profile navigation.
            var identities = await _context.Users.AsNoTracking().Where(u => participantIds.Contains(u.UserId) && u.DeletedAt == null)
                .Select(u => new { u.UserId, u.FullName, u.AvatarUrl }).ToDictionaryAsync(u => u.UserId);
            var dtos = new List<ChatRoomDto>();

            foreach (var room in rooms)
            {
                var dto = MapToChatRoomDto(room, userId);
                dto.CustomerName = identities.TryGetValue(room.CustomerId, out var customer) ? DisplayName(customer.FullName) : "Người dùng B-Book";
                dto.CustomerAvatar = customer?.AvatarUrl;
                dto.MUAName = identities.TryGetValue(room.MUAId, out var artist) ? DisplayName(artist.FullName) : "Người dùng B-Book";
                dto.MUAAvatar = artist?.AvatarUrl;
                SetOtherParticipant(dto, userId);
                // Get last message
                var lastMsg = await _chatRepository.GetLastMessageAsync(room.ChatRoomId);
                if (lastMsg != null)
                {
                    dto.LastMessage = await MapToMessageDtoAsync(lastMsg, userId);
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
            var result = new List<MessageDto>();
            foreach (var message in messages) result.Add(await MapToMessageDtoAsync(message, userId));
            return result;
        }

        public async Task EnsureRoomAccessAsync(Guid roomId, Guid userId)
        {
        await new PlayReviewPolicy(_context).EnsureChatDomainAsync(roomId, userId);
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

        public async Task EnsureRoomInteractionAsync(Guid roomId, Guid userId)
        {
            await EnsureRoomAccessAsync(roomId, userId);
            var participants = await GetParticipantsAsync(roomId, userId);
            await new ModerationService(_context).EnsureInteraction(userId, participants.CustomerId == userId ? participants.MuaId : participants.CustomerId);
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
            await using var scope = await ModerationWriteScope.Start(_context, senderId);

            var room = await _chatRepository.GetChatRoomByIdAsync(roomId);
            if (room == null)
            {
                throw new Exception("Chat room not found.");
            }

            if (room.CustomerId != senderId && room.MUAId != senderId)
            {
                throw new UnauthorizedAccessException("You are not part of this chat room.");
            }

            await new PlayReviewPolicy(_context).EnsureChatDomainAsync(roomId, senderId);
            await new ModerationService(_context).EnsureInteraction(senderId, room.CustomerId == senderId ? room.MUAId : room.CustomerId);
            if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(imageUrl))
                throw new ArgumentException("Tin nhắn phải có nội dung hoặc hình ảnh.");
            if (content?.Trim().Length > 2000) throw new ArgumentException("Tin nhắn không được vượt quá 2000 ký tự.");
            VerificationMedia? attachment = null;
            await using var imageTransaction = !string.IsNullOrWhiteSpace(imageUrl) && _context.Database.IsNpgsql() && _context.Database.CurrentTransaction == null ? await _context.Database.BeginTransactionAsync() : null;
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                if (_media == null || !VerificationMediaService.TryId(imageUrl, out var imageId)) throw new ArgumentException("Vui lòng tải ảnh qua kho ảnh chat riêng tư.");
                if (_context.Database.IsNpgsql())
                {
                    var available = await _context.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock_shared(724266524669002) AS \"Value\"").SingleAsync();
                    if (!available) throw new ArgumentException("Kho ảnh đang được bảo trì. Vui lòng thử lại sau.");
                    await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"VerificationMedia\" WHERE \"Id\" = {imageId} FOR UPDATE");
                }
                attachment = await _media.FindOwnedAsync(imageId, senderId, "chat");
                if (attachment == null || attachment.ContextId != roomId) throw new ArgumentException("Ảnh không thuộc tài khoản hoặc cuộc trò chuyện này.");
                // Durable reference + attachment are saved together by the repository's SaveChanges.
                attachment.AttachedAt = DateTime.UtcNow;
            }

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
            if (imageTransaction != null) await imageTransaction.CommitAsync();
            await scope.Commit();
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
            return await MapToMessageDtoAsync(savedMessage, senderId);
        }

        public async Task<MessageDto> ToggleReactionAsync(Guid roomId, Guid messageId, Guid userId, string emoji)
        {
            await using var scope = await ModerationWriteScope.Start(_context, userId);
        await new PlayReviewPolicy(_context).EnsureChatDomainAsync(roomId, userId);
            emoji = emoji.Trim();
            var allowedEmoji = new HashSet<string> { "❤️", "👍", "😀", "😂", "😍", "🔥", "👏", "😢" };
            if (!allowedEmoji.Contains(emoji)) throw new ArgumentException("Cảm xúc không được hỗ trợ.");
            var room = await _chatRepository.GetChatRoomByIdAsync(roomId);
            if (room == null || (room.CustomerId != userId && room.MUAId != userId))
                throw new UnauthorizedAccessException();
            var message = await _context.Messages.Include(m => m.ReplyToMessage).Include(m => m.Reactions)
                .FirstOrDefaultAsync(m => m.MessageId == messageId && m.ChatRoomId == roomId)
                ?? throw new ArgumentException("Không tìm thấy tin nhắn.");
            await EnsureRoomInteractionAsync(roomId, userId);
            await new ModerationService(_context).EnsureContent("Message", messageId);
            var existing = message.Reactions.FirstOrDefault(r => r.UserId == userId);
            if (existing != null && existing.Emoji == emoji) _context.MessageReactions.Remove(existing);
            else if (existing != null) existing.Emoji = emoji;
            else _context.MessageReactions.Add(new MessageReaction { MessageId = messageId, UserId = userId, Emoji = emoji });
            await _context.SaveChangesAsync();
            await _context.Entry(message).Collection(m => m.Reactions).LoadAsync();
            await scope.Commit();
            return await MapToMessageDtoAsync(message, userId);
        }

        private static string DisplayName(string? name) => string.IsNullOrWhiteSpace(name) ? "Người dùng B-Book" : name.Trim();

        private static void SetOtherParticipant(ChatRoomDto dto, Guid viewerId)
        {
            var isCustomer = dto.CustomerId == viewerId;
            dto.OtherUserId = isCustomer ? dto.MUAId : dto.CustomerId;
            dto.OtherUserName = isCustomer ? dto.MUAName : dto.CustomerName;
            dto.OtherUserAvatar = isCustomer ? dto.MUAAvatar : dto.CustomerAvatar;
        }

        private ChatRoomDto MapToChatRoomDto(ChatRoom room, Guid viewerId)
        {
            var dto = new ChatRoomDto
            {
                ChatRoomId = room.ChatRoomId,
                CustomerId = room.CustomerId,
                CustomerName = DisplayName(room.Customer?.FullName),
                CustomerAvatar = room.Customer?.AvatarUrl,
                MUAId = room.MUAId,
                MUAName = DisplayName(room.MakeupArtistProfile?.User?.FullName),
                MUAAvatar = room.MakeupArtistProfile?.User?.AvatarUrl,
                CreatedAt = room.CreatedAt
            };
            SetOtherParticipant(dto, viewerId);
            return dto;
        }

        private async Task<MessageDto> MapToMessageDtoAsync(Message message, Guid? currentUserId = null)
        {
            var removed = await _context.ContentReports.AnyAsync(x => x.TargetType == "Message" && x.TargetId == message.MessageId && x.Status == "Removed");
            var replyRemoved = message.ReplyToMessageId.HasValue && await _context.ContentReports.AnyAsync(x => x.TargetType == "Message" && x.TargetId == message.ReplyToMessageId && x.Status == "Removed");
            async Task<string?> Preview(string? reference)
            {
                try { return _media == null ? null : await _media.ResolveChatAsync(reference, message.ChatRoomId); }
                catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException)
                { _logger.LogWarning("Private chat preview unavailable for message {MessageId}.", message.MessageId); return null; }
            }
            return new MessageDto
            {
                MessageId = message.MessageId,
                ChatRoomId = message.ChatRoomId,
                SenderId = message.SenderId,
                Content = removed ? "Nội dung đã được ẩn bởi kiểm duyệt." : message.Content,
                SentAt = message.SentAt,
                IsRead = message.IsRead
                ,ReadAt = message.ReadAt
                ,ImageUrl = removed ? null : await Preview(message.ImageUrl)
                ,ImageMediaId = !removed && VerificationMediaService.TryId(message.ImageUrl, out var imageId) ? imageId : null
                ,ReplyToMessageId = message.ReplyToMessageId
                ,ReplyToContent = replyRemoved ? "Nội dung đã được ẩn bởi kiểm duyệt." : message.ReplyToMessage?.Content
                ,ReplyToImageUrl = replyRemoved ? null : await Preview(message.ReplyToMessage?.ImageUrl)
                ,ReplyToImageMediaId = !replyRemoved && VerificationMediaService.TryId(message.ReplyToMessage?.ImageUrl, out var replyImageId) ? replyImageId : null
                ,Reactions = message.Reactions.Where(r => !removed).GroupBy(r => r.Emoji).Select(g => new MessageReactionDto
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
