using System;

namespace BeautyBookBackend.DTOs.Chat
{
    public class SendMessageRequest
    {
        public string? Content { get; set; }
        public string? ImageUrl { get; set; }
        public Guid? ReplyToMessageId { get; set; }
    }
}
