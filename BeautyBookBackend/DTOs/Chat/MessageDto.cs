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
        public string? ImageUrl { get; set; }
        public Guid? ReplyToMessageId { get; set; }
        public string? ReplyToContent { get; set; }
        public string? ReplyToImageUrl { get; set; }
        public List<MessageReactionDto> Reactions { get; set; } = new();
    }

    public class MessageReactionDto
    {
        public string Emoji { get; set; } = string.Empty;
        public int Count { get; set; }
        public bool ReactedByMe { get; set; }
    }
}
