using System;
using System.Collections.Generic;
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
    public class MuaController : ControllerBase
    {
        private readonly IMuaService _muaService;

        public MuaController(IMuaService muaService)
        {
            _muaService = muaService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());

        [HttpGet]
        public async Task<IActionResult> GetMuas([FromQuery] MuaFilterDto filter)
        {
            var muas = await _muaService.GetMuasAsync(filter);
            return Ok(muas);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetMuaById(Guid id)
        {
            var mua = await _muaService.GetMuaByIdAsync(id);
            if (mua == null)
            {
                return NotFound(new { Message = "Không tìm thấy thông tin Make Up Artist này." });
            }
            return Ok(mua);
        }

        [Authorize]
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] MuaUpdateDto updateDto)
        {
            // Đảm bảo người dùng có vai trò là MUA
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
            if (roleClaim != UserRole.MUA.ToString())
            {
                return Forbid();
            }

            var success = await _muaService.UpdateMuaProfileAsync(CurrentUserId, updateDto);
            if (!success)
            {
                return BadRequest(new { Message = "Cập nhật hồ sơ thất bại." });
            }

            return Ok(new { Message = "Cập nhật hồ sơ Makeup Artist thành công!" });
        }

        // ================= PORTFOLIO MANAGEMENT =================
        [HttpGet("{id}/portfolio")]
        public async Task<IActionResult> GetPortfolio(Guid id)
        {
            var portfolio = await _muaService.GetMuaPortfolioAsync(id);
            return Ok(portfolio);
        }

        [Authorize]
        [HttpPost("portfolio")]
        public async Task<IActionResult> AddPortfolioImage([FromBody] PortfolioCreateRequest request)
        {
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
            if (roleClaim != UserRole.MUA.ToString()) return Forbid();

            var success = await _muaService.AddPortfolioImageAsync(CurrentUserId, request.ImageUrl, request.Description ?? "");
            if (!success) return BadRequest(new { Message = "Không thể thêm ảnh vào Portfolio." });

            return Ok(new { Message = "Đã thêm tác phẩm vào bộ sưu tập Portfolio thành công!" });
        }

        [Authorize]
        [HttpDelete("portfolio/{portfolioId}")]
        public async Task<IActionResult> DeletePortfolioImage(Guid portfolioId)
        {
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
            if (roleClaim != UserRole.MUA.ToString()) return Forbid();

            var success = await _muaService.DeletePortfolioImageAsync(CurrentUserId, portfolioId);
            if (!success) return BadRequest(new { Message = "Không thể xóa tác phẩm này." });

            return Ok(new { Message = "Đã xóa ảnh tác phẩm khỏi Portfolio." });
        }

        // ================= STYLES =================
        [HttpGet("styles")]
        public async Task<IActionResult> GetStyles()
        {
            var styles = await _muaService.GetAllStylesAsync();
            return Ok(styles);
        }

        [Authorize]
        [HttpPut("styles")]
        public async Task<IActionResult> UpdateStyles([FromBody] List<int> styleIds)
        {
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
            if (roleClaim != UserRole.MUA.ToString()) return Forbid();

            var success = await _muaService.UpdateStylesAsync(CurrentUserId, styleIds);
            if (!success) return BadRequest(new { Message = "Không thể cập nhật danh sách phong cách trang điểm." });

            return Ok(new { Message = "Cập nhật danh sách phong cách thế mạnh thành công!" });
        }
    }

    public class PortfolioCreateRequest
    {
        public string ImageUrl { get; set; } = null!;
        public string? Description { get; set; }
    }
}
