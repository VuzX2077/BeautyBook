using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;

        public UserController(IUserService userService)
        {
            _userService = userService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var user = await _userService.GetFullUserProfileAsync(CurrentUserId);
            if (user == null)
            {
                return NotFound(new { Message = "Khong tim thay thong tin nguoi dung." });
            }

            return Ok(user);
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UserUpdateDto updateDto)
        {
            var user = await _userService.UpdateProfileAsync(CurrentUserId, updateDto);
            if (user == null)
            {
                return NotFound(new { Message = "Khong tim thay nguoi dung." });
            }

            return Ok(new { Message = "Cap nhat thong tin thanh cong!", User = user });
        }

        [HttpDelete("me")]
        public async Task<IActionResult> DeleteOwnAccount()
        {
            var result = await _userService.DeleteOwnAccountAsync(CurrentUserId);
            if (result.Deleted) return Ok(new { result.Code, result.Message });
            if (result.Code == "ACCOUNT_NOT_FOUND") return NotFound(new { result.Code, result.Message });
            return Conflict(new { result.Code, result.Message });
        }
    }
}
