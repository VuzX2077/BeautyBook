using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs.Chat;
using BeautyBookBackend.Models;
using BeautyBookBackend.Repositories;

namespace BeautyBookBackend.Services
{
    public class ChatService : IChatService
    {
        private readonly IChatRepository _chatRepository;

        public ChatService(IChatRepository chatRepository)
        {
            _chatRepository = chatRepository;
        }

        public async Task<ChatRoomDto> GetOrCreateChatRoomAsync(Guid customerId, Guid muaId)
        {
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
                var messages = await _chatRepository.GetMessagesByRoomIdAsync(room.ChatRoomId);
                var lastMsg = messages.LastOrDefault();
                if (lastMsg != null)
                {
                    dto.LastMessage = MapToMessageDto(lastMsg);
                }
                dtos.Add(dto);
            }

            return dtos.OrderByDescending(d => d.LastMessage?.SentAt ?? d.CreatedAt);
        }

        public async Task<IEnumerable<MessageDto>> GetMessagesByRoomIdAsync(Guid roomId, Guid userId)
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

            var messages = await _chatRepository.GetMessagesByRoomIdAsync(roomId);
            return messages.Select(MapToMessageDto);
        }

        public async Task<MessageDto> SendMessageAsync(Guid roomId, Guid senderId, string content)
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

            var message = new Message
            {
                MessageId = Guid.NewGuid(),
                ChatRoomId = roomId,
                SenderId = senderId,
                Content = content,
                SentAt = DateTime.UtcNow,
                IsRead = false
            };

            var savedMessage = await _chatRepository.AddMessageAsync(message);
            return MapToMessageDto(savedMessage);
        }

        private ChatRoomDto MapToChatRoomDto(ChatRoom room)
        {
            // 1. XỬ LÝ TÊN (Họ và tên)
            string verifiedCustomerName = room.Customer?.FullName ?? "Khách Hàng";
            string verifiedMUAName = room.MakeupArtistProfile?.User?.FullName ?? "Chuyên Gia Trang Điểm";

            // 2. XỬ LÝ AVATAR (Tự động tìm trường dữ liệu ảnh đại diện thích hợp trong Model của bạn)
            // Nếu Model User của bạn dùng tên khác, hãy thay thế chữ '.AvatarUrl' bằng tên thuộc tính đó (ví dụ: .ImageUrl, .ProfilePicture)
            string customerAvatarUrl = null;
            string muaAvatarUrl = null;

            try
            {
                // Thử lấy thuộc tính ảnh đại diện từ Model (Sử dụng toán tử Elvis ? để tránh crash)
                // Bạn hãy kiểm tra xem trong Model User của bạn trường ảnh tên là gì rồi sửa lại chữ .AvatarUrl nhé
                customerAvatarUrl = (room.Customer as dynamic)?.AvatarUrl ?? (room.Customer as dynamic)?.ImageUrl;
                muaAvatarUrl = (room.MakeupArtistProfile?.User as dynamic)?.AvatarUrl ?? (room.MakeupArtistProfile?.User as dynamic)?.ImageUrl;
            }
            catch
            {
                // Nếu không tìm thấy thuộc tính nào khớp, giữ nguyên null để không bị lỗi biên dịch
                customerAvatarUrl = null;
                muaAvatarUrl = null;
            }

            return new ChatRoomDto
            {
                ChatRoomId = room.ChatRoomId,
                CustomerId = room.CustomerId,
                CustomerName = verifiedCustomerName,
                CustomerAvatar = customerAvatarUrl, // Trả về link ảnh đại diện chính xác của Customer
                MUAId = room.MUAId,
                MUAName = verifiedMUAName,
                MUAAvatar = muaAvatarUrl,           // Trả về link ảnh đại diện chính xác của MUA
                CreatedAt = room.CreatedAt
            };
        }

        private MessageDto MapToMessageDto(Message message)
        {
            return new MessageDto
            {
                MessageId = message.MessageId,
                ChatRoomId = message.ChatRoomId,
                SenderId = message.SenderId,
                Content = message.Content,
                SentAt = message.SentAt,
                IsRead = message.IsRead
            };
        }
    }
}
