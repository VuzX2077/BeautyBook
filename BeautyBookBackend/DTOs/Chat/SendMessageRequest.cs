using System;
using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs.Chat
{
    public class SendMessageRequest
    {
        [MaxLength(2000)]
        public string? Content { get; set; }
        [MaxLength(1000)]
        public string? ImageUrl { get; set; }
        public Guid? ReplyToMessageId { get; set; }
    }
}
