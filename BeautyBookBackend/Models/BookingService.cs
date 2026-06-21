using System;

namespace BeautyBookBackend.Models
{
    public class BookingService
    {
        public Guid Id { get; set; }
        public Guid BookingId { get; set; }
        public Guid ServiceId { get; set; }
        
        // Snapshots (in case the service is deleted or changed later)
        public string ServiceName { get; set; } = string.Empty;
        public decimal PriceSnapshot { get; set; }
        public int DurationMinutesSnapshot { get; set; }
        
        public int ParticipantsCount { get; set; } = 1;

        // Navigation
        public Booking? Booking { get; set; }
        public Service? Service { get; set; }
    }
}
