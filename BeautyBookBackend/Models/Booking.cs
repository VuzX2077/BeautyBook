using System;
using System.Collections.Generic;

using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Models
{
    public class Booking
    {
        public Guid BookingId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid MUAId { get; set; }
        
        public decimal TotalAmount { get; set; }
        public int TotalDurationMinutes { get; set; }
        public DateTime BookingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        
        public string? Address { get; set; }
        public string? Notes { get; set; }

        public BookingStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public User? Customer { get; set; }
        public MakeupArtistProfile? MakeupArtistProfile { get; set; }
        public ICollection<BookingService> BookingServices { get; set; } = new List<BookingService>();
    }
}
