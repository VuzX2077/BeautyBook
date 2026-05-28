using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public UserController(ApplicationDbContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == CurrentUserId);
            if (user == null)
            {
                return NotFound(new { Message = "Không tìm thấy thông tin người dùng." });
            }

            return Ok(new UserDto
            {
                UserId = user.UserId,
                FullName = user.FullName,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                PhoneNumber = user.PhoneNumber,
                Role = user.Role,
                CreatedAt = user.CreatedAt,
                IsActive = user.IsActive
            });
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UserUpdateDto updateDto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == CurrentUserId);
            if (user == null)
            {
                return NotFound(new { Message = "Không tìm thấy người dùng." });
            }

            if (!string.IsNullOrEmpty(updateDto.FullName)) user.FullName = updateDto.FullName;
            if (!string.IsNullOrEmpty(updateDto.AvatarUrl)) user.AvatarUrl = updateDto.AvatarUrl;
            if (!string.IsNullOrEmpty(updateDto.PhoneNumber)) user.PhoneNumber = updateDto.PhoneNumber;

            await _context.SaveChangesAsync();

            return Ok(new { Message = "Cập nhật thông tin thành công!", User = user });
        }
    }
}
