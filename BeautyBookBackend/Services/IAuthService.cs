using System.Threading.Tasks;
using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services
{
    public interface IAuthService
    {
        Task<TokenDto?> GoogleLoginAsync(GoogleLoginDto googleLoginDto);
        Task<TokenDto?> LoginAsync(LoginDto loginDto);
        Task<UserDto?> RegisterAsync(RegisterDto registerDto);
    }
}
