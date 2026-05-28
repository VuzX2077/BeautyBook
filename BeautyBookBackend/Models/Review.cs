using System;

namespace BeautyBookBackend.Models
{
    public class Review
    {
        public Guid ReviewId { get; set; }
        public Guid BookingId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid MUAId { get; set; }
        public int Rating { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public Booking? Booking { get; set; }
        public User? Customer { get; set; }
        public MakeupArtistProfile? MakeupArtistProfile { get; set; }
    }
}
