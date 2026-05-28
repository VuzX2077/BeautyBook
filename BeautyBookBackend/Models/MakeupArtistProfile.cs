using System;

namespace BeautyBookBackend.Models
{
    public class MakeupArtistProfile
    {
        public Guid MUAId { get; set; }
        public string? Bio { get; set; }
        public int ExperienceYears { get; set; }
        public decimal RatingAverage { get; set; }
        public int TotalBookings { get; set; }
        public string? PortfolioCoverUrl { get; set; }

        // Navigation
        public User? User { get; set; }
    }
}
