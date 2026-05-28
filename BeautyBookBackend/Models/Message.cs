using System;

namespace BeautyBookBackend.Models
{
    public class Message
    {
        public Guid MessageId { get; set; }
        public Guid ChatRoomId { get; set; }
        public Guid SenderId { get; set; }
        public string? Content { get; set; }
        public DateTime SentAt { get; set; }
        public bool IsRead { get; set; }

        // Navigation
        public ChatRoom? ChatRoom { get; set; }
        public User? Sender { get; set; }
    }
}
