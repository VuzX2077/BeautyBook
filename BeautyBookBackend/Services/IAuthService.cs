using System;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services
{
    public interface IAuthService
    {
        Task<TokenDto?> BecomeMuaAsync(Guid userId, MuaApplicationRequestDto request);
        Task<TokenDto?> GoogleLoginAsync(GoogleLoginDto googleLoginDto);
        Task<TokenDto?> LoginAsync(LoginDto loginDto);
        Task<string?> SendRegistrationOtpAsync(string email);
        Task<UserDto?> RegisterAsync(RegisterDto registerDto);
        Task SendPasswordResetOtpAsync(string email);
        Task<bool> ResetPasswordAsync(ResetPasswordDto request);
        Task<string?> VerifyPasswordResetOtpAsync(VerifyPasswordResetOtpDto request);
        Task<bool> CompletePasswordResetAsync(CompletePasswordResetDto request);
        Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordDto request);
        Task<bool> VerifyPasswordAsync(Guid userId, string password);
    }
}
