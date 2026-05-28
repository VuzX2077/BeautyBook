using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;

namespace BeautyBookBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = await _authService.RegisterAsync(registerDto);
            if (user == null)
            {
                return BadRequest(new { Message = "Email này đã được đăng ký sử dụng trong hệ thống." });
            }

            return Ok(new { Message = "Đăng ký tài khoản thành công!", User = user });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var token = await _authService.LoginAsync(loginDto);
            if (token == null)
            {
                return Unauthorized(new { Message = "Email hoặc mật khẩu không chính xác." });
            }

            return Ok(token);
        }
    }
}
