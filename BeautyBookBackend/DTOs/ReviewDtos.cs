using System;
using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs
{
    public class ReviewDto
    {
        public Guid ReviewId { get; set; }
        public Guid BookingId { get; set; }
        public Guid CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public Guid MUAId { get; set; }
        public int Rating { get; set; }
        public string? Comment { get; set; }
        public string? ImageUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? MuaReply { get; set; }
        public DateTime? MuaReplyAt { get; set; }
    }

    public class ReviewCreateDto
    {
        [Required]
        [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5 stars.")]
        public int Rating { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }

        [MaxLength(2048)]
        public string? ImageUrl { get; set; }
    }

    public class ReviewCreateWithBookingDto : ReviewCreateDto
    {
        [Required]
        public Guid BookingId { get; set; }
    }

    public class ReviewReplyDto
    {
        [Required]
        [MaxLength(2000)]
        public string ReplyContent { get; set; } = string.Empty;
    }
}
