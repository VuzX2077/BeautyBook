using System;

using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Models
{
    public class User
    {
        public Guid UserId { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? PasswordHash { get; set; }
        public string? AvatarUrl { get; set; }
        public string? PhoneNumber { get; set; }
        public UserRole Role { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }

        // Navigation
        public MakeupArtistProfile? MakeupArtistProfile { get; set; }
    }
}
