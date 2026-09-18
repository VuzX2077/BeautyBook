using System;
using System.Collections.Generic;

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
        public string? ImageUrl { get; set; }
        public Guid? ReplyToMessageId { get; set; }

        // Navigation
        public ChatRoom? ChatRoom { get; set; }
        public User? Sender { get; set; }
        public Message? ReplyToMessage { get; set; }
        public ICollection<MessageReaction> Reactions { get; set; } = new List<MessageReaction>();
    }
}
