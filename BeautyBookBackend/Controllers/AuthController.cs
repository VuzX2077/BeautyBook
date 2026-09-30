using System.Threading.Tasks;
using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
                return BadRequest(new { Message = "Mã OTP không hợp lệ, đã hết hạn hoặc email đã được đăng ký." });
            }

            return Ok(new { Message = "Đăng ký tài khoản thành công!", User = user });
        }

        [HttpPost("register/request-otp")]
        public async Task<IActionResult> RequestRegistrationOtp([FromBody] EmailDto request)
        {
            try
            {
                var error = await _authService.SendRegistrationOtpAsync(request.Email);
                return error == "EMAIL_EXISTS"
                    ? BadRequest(new { Code = error, Message = "Email này đã được đăng ký sử dụng trong hệ thống." })
                    : Ok(new { Message = "Mã OTP đã được gửi đến email." });
            }
            catch (InvalidOperationException ex) { return StatusCode(429, new { Message = ex.Message }); }
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] EmailDto request)
        {
            try { await _authService.SendPasswordResetOtpAsync(request.Email); }
            catch (InvalidOperationException) { }
            return Ok(new { Message = "Nếu email tồn tại, mã OTP đã được gửi." });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto request)
            => await _authService.ResetPasswordAsync(request)
                ? Ok(new { Message = "Đặt lại mật khẩu thành công." })
                : BadRequest(new { Message = "Mã OTP không hợp lệ, đã hết hạn hoặc tài khoản không hỗ trợ mật khẩu." });

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto request)
        {
            var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(id, out var userId)) return Unauthorized();
            return await _authService.ChangePasswordAsync(userId, request)
                ? Ok(new { Message = "Đổi mật khẩu thành công." })
                : BadRequest(new { Message = "Mật khẩu hiện tại không chính xác hoặc tài khoản không hỗ trợ mật khẩu." });
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

        [HttpPost("google")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginDto googleLoginDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var token = await _authService.GoogleLoginAsync(googleLoginDto);
            if (token == null)
            {
                return Unauthorized(new { Message = "Google token khong hop le hoac Google OAuth chua duoc cau hinh." });
            }

            return Ok(token);
        }

        [Authorize]
        [HttpPost("become-mua")]
        public async Task<IActionResult> BecomeMua([FromBody] MuaApplicationRequestDto request)
        {
            var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdValue, out var userId))
            {
                return Unauthorized(new { Message = "Token khong hop le." });
            }

            var token = await _authService.BecomeMuaAsync(userId, request);
            if (token == null)
            {
                return BadRequest(new { Code = "MUA_ONBOARDING_FAILED", Message = "Không thể tạo hồ sơ MUA. Vui lòng kiểm tra chuyên môn đã chọn." });
            }

            return Ok(token);
        }
    }
}
