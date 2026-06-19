using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ServiceController : ControllerBase
    {
        private readonly IMuaService _muaService;

        public ServiceController(IMuaService muaService)
        {
            _muaService = muaService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());

        [HttpGet("mua/{muaId}")]
        public async Task<IActionResult> GetServicesByMua(Guid muaId)
        {
            var services = await _muaService.GetMuaServicesAsync(muaId);
            return Ok(services);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> AddService([FromBody] ServiceCreateDto serviceDto)
        {
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
            if (roleClaim != UserRole.MUA.ToString())
            {
                return Forbid();
            }

            var service = await _muaService.AddMuaServiceAsync(CurrentUserId, serviceDto);
            if (service == null)
            {
                return BadRequest(new { Message = "Thêm dịch vụ thất bại. Đảm bảo hồ sơ Makeup Artist của bạn đã được thiết lập." });
            }

            return Ok(new { Message = "Đã thêm gói dịch vụ trang điểm mới thành công!", Service = service });
        }

        [Authorize]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateService(Guid id, [FromBody] ServiceCreateDto serviceDto)
        {
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
            if (roleClaim != UserRole.MUA.ToString())
            {
                return Forbid();
            }

            var success = await _muaService.UpdateMuaServiceAsync(CurrentUserId, id, serviceDto);
            if (!success)
            {
                return NotFound(new { Message = "Không tìm thấy gói dịch vụ này hoặc bạn không có quyền chỉnh sửa nó." });
            }

            return Ok(new { Message = "Cập nhật dịch vụ thành công!" });
        }

        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteService(Guid id)
        {
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
            if (roleClaim != UserRole.MUA.ToString())
            {
                return Forbid();
            }

            var success = await _muaService.DeleteMuaServiceAsync(CurrentUserId, id);
            if (!success)
            {
                return NotFound(new { Message = "Không tìm thấy gói dịch vụ này hoặc bạn không có quyền xóa." });
            }

            return Ok(new { Message = "Đã xóa gói dịch vụ trang điểm thành công." });
        }
    }
}
