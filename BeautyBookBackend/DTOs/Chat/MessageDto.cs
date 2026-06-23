using System;

namespace BeautyBookBackend.DTOs.Chat
{
    public class MessageDto
    {
        public Guid MessageId { get; set; }
        public Guid ChatRoomId { get; set; }
        public Guid SenderId { get; set; }
        public string? Content { get; set; }
        public DateTime SentAt { get; set; }
        public bool IsRead { get; set; }
    }
}
