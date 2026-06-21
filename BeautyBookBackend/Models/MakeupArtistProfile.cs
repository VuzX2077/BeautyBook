using System;

namespace BeautyBookBackend.Models
{
    public class MakeupArtistProfile
    {
        public Guid MUAId { get; set; }
        public string? Bio { get; set; }
        public int ExperienceYears { get; set; }
        public decimal AverageRating { get; set; }
        public int TotalBookings { get; set; }
        public string? PortfolioCoverUrl { get; set; }
        
        public string? City { get; set; }
        public string? Specialization { get; set; }
        public string? SocialLinks { get; set; }

        public Models.Enums.MuaStatus Status { get; set; } = Models.Enums.MuaStatus.Draft;
        public int RankScore { get; set; } = 0;
        public int ProfileQualityScore { get; set; } = 0;
        public DateTime? ListedAt { get; set; }
        public DateTime? LastActiveAt { get; set; }

        // Navigation
        public User? User { get; set; }
        public System.Collections.Generic.ICollection<Portfolio> Portfolios { get; set; } = new System.Collections.Generic.List<Portfolio>();
        public System.Collections.Generic.ICollection<Service> Services { get; set; } = new System.Collections.Generic.List<Service>();
    }
}
