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
        public DateTime CreatedAt { get; set; }
    }

    public class ReviewCreateDto
    {
        [Required]
        [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5 stars.")]
        public int Rating { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }
    }

    public class ReviewCreateWithBookingDto : ReviewCreateDto
    {
        [Required]
        public Guid BookingId { get; set; }
    }
}
