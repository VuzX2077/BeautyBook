using System;

using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Models
{
    public class Booking
    {
        public Guid BookingId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid MUAId { get; set; }
        public Guid ServiceId { get; set; }
        public DateTime BookingDate { get; set; }
        public string? Address { get; set; }
        public string? Note { get; set; }
        public decimal TotalPrice { get; set; }
        public BookingStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public User? Customer { get; set; }
        public MakeupArtistProfile? MakeupArtistProfile { get; set; }
        public Service? Service { get; set; }
    }
}
