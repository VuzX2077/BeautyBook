using System.Threading.Tasks;
using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services
{
    public interface IAuthService
    {
        Task<TokenDto?> LoginAsync(LoginDto loginDto);
        Task<UserDto?> RegisterAsync(RegisterDto registerDto);
    }
}
