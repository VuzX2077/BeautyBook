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
        public bool PhoneVerified { get; set; }
        public UserRole Role { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public bool IsDemoAccount { get; set; }
        public DateTime? DeletedAt { get; set; }
        // New registrations use the owner-tracked uploader. Existing rows default false
        // in the additive migration: old unreferenced uploads cannot be inferred from DB.
        public bool MediaOwnershipTracked { get; set; } = true;

        // Navigation
        public MakeupArtistProfile? MakeupArtistProfile { get; set; }
        public ICollection<BankAccount> BankAccounts { get; set; } = new List<BankAccount>();
    }
}
