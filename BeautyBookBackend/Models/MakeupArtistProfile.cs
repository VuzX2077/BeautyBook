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
        public string? InstagramUrl { get; set; }
        public string? FacebookUrl { get; set; }

        public Models.Enums.MuaStatus Status { get; set; } = Models.Enums.MuaStatus.Draft;
        public Models.Enums.MuaVerificationStatus VerificationStatus { get; set; } = Models.Enums.MuaVerificationStatus.Draft;
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public Guid? ReviewedByAdminId { get; set; }
        public string? RejectionReason { get; set; }
        public int RankScore { get; set; } = 0;
        public int ProfileQualityScore { get; set; } = 0;
        public DateTime? ListedAt { get; set; }
        public DateTime? LastActiveAt { get; set; }

        // Navigation
        public User? User { get; set; }
        public System.Collections.Generic.ICollection<Portfolio> Portfolios { get; set; } = new System.Collections.Generic.List<Portfolio>();
        public System.Collections.Generic.ICollection<Service> Services { get; set; } = new System.Collections.Generic.List<Service>();
        public System.Collections.Generic.ICollection<MuaWorkingSchedule> WorkingSchedules { get; set; } = new System.Collections.Generic.List<MuaWorkingSchedule>();
        public System.Collections.Generic.ICollection<MuaTimeOff> TimeOffs { get; set; } = new System.Collections.Generic.List<MuaTimeOff>();
    }
}
