using System;

namespace BeautyBookBackend.DTOs.Chat
{
    public class ChatRoomDto
    {
        public Guid ChatRoomId { get; set; }
        public Guid CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerAvatar { get; set; }
        
        public Guid MUAId { get; set; }
        public string? MUAName { get; set; }
        public string? MUAAvatar { get; set; }
        
        public DateTime CreatedAt { get; set; }
        
        public MessageDto? LastMessage { get; set; }
    }
}
