using System.ComponentModel.DataAnnotations;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.DTOs
{
    public class RegisterDto
    {
        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = null!;

        [Required]
        [EmailAddress]
        [MaxLength(255)]
        public string Email { get; set; } = null!;

        [Required]
        [MinLength(6)]
        public string Password { get; set; } = null!;

        [Phone]
        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        [Required]
        [RegularExpression("^[0-9]{6}$")]
        public string Otp { get; set; } = null!;

    }

    public class EmailDto
    {
        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = null!;
    }

    public class ResetPasswordDto : EmailDto
    {
        [Required, RegularExpression("^[0-9]{6}$")]
        public string Otp { get; set; } = null!;

        [Required, MinLength(6), MaxLength(100)]
        public string NewPassword { get; set; } = null!;
    }

    public class ChangePasswordDto
    {
        [Required]
        public string CurrentPassword { get; set; } = null!;

        [Required, MinLength(6), MaxLength(100)]
        public string NewPassword { get; set; } = null!;
    }

    public class LoginDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = null!;

        [Required]
        public string Password { get; set; } = null!;
    }

    public class GoogleLoginDto
    {
        [Required]
        public string IdToken { get; set; } = null!;
    }

    public class TokenDto
    {
        public string Token { get; set; } = null!;
        public DateTime Expiration { get; set; }
        public Guid UserId { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public UserRole Role { get; set; }
        public bool HasMuaProfile { get; set; }
        public bool IsDemoAccount { get; set; }
    }
}
