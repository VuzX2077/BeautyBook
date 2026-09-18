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
        private readonly IBookingService _bookingService;

        public MuaController(IMuaService muaService, IBookingService bookingService)
        {
            _muaService = muaService;
            _bookingService = bookingService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());

        [HttpGet("{id}/availability")]
        public async Task<IActionResult> GetAvailability(Guid id, [FromQuery] DateTime date, [FromQuery] int duration)
        {
            if (date == default) date = DateTime.UtcNow.Date;
            if (duration <= 0) duration = 60; // Default 1 hour if not specified

            var slots = await _bookingService.GetAvailableSlotsAsync(id, date, duration);
            return Ok(slots);
        }

        [HttpGet]
        public async Task<IActionResult> GetMuas([FromQuery] int page = 1)
        {
            var muas = await _muaService.GetMuasAsync(page);
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
            // Đảm bảo người dùng có profile MUA
            if (!await _muaService.HasMuaProfileAsync(CurrentUserId))
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
            Guid? currentUserId = User.Identity?.IsAuthenticated == true ? CurrentUserId : null;
            var portfolio = await _muaService.GetMuaPortfolioAsync(id, currentUserId);
            return Ok(portfolio);
        }

        [Authorize]
        [HttpPost("portfolio/{portfolioId:guid}/like")]
        public async Task<IActionResult> TogglePortfolioLike(Guid portfolioId)
        {
            return await _muaService.TogglePortfolioLikeAsync(CurrentUserId, portfolioId)
                ? Ok() : NotFound();
        }

        [Authorize]
        [HttpPost("portfolio/{portfolioId:guid}/save")]
        public async Task<IActionResult> TogglePortfolioSave(Guid portfolioId)
        {
            return await _muaService.TogglePortfolioSaveAsync(CurrentUserId, portfolioId)
                ? Ok() : NotFound();
        }

        [HttpGet("portfolio/{portfolioId:guid}/comments")]
        public async Task<IActionResult> GetPortfolioComments(Guid portfolioId) =>
            Ok(await _muaService.GetPortfolioCommentsAsync(portfolioId));

        [Authorize]
        [HttpPost("portfolio/{portfolioId:guid}/comments")]
        public async Task<IActionResult> AddPortfolioComment(Guid portfolioId, [FromBody] ContentRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Content)) return BadRequest(new { Message = "Nội dung không được để trống." });
            var comment = await _muaService.AddPortfolioCommentAsync(CurrentUserId, portfolioId, request.Content.Trim());
            return comment == null ? NotFound() : Ok(comment);
        }

        [Authorize]
        [HttpPost("portfolio")]
        public async Task<IActionResult> AddPortfolioImage([FromBody] PortfolioCreateRequest request)
        {
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
            if (roleClaim != UserRole.MUA.ToString() && roleClaim != UserRole.Admin.ToString()) return Forbid();

            var success = await _muaService.AddPortfolioImageAsync(CurrentUserId, request);
            if (!success) return BadRequest(new { Message = "Không thể thêm ảnh vào Portfolio." });

            return Ok(new { Message = "Đã thêm tác phẩm vào bộ sưu tập Portfolio thành công!" });
        }

        [Authorize]
        [HttpPut("portfolio/{portfolioId:guid}")]
        public async Task<IActionResult> UpdatePortfolioImage(Guid portfolioId, [FromBody] PortfolioCreateRequest request)
        {
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
            if (roleClaim != UserRole.MUA.ToString() && roleClaim != UserRole.Admin.ToString()) return Forbid();

            var success = await _muaService.UpdatePortfolioImageAsync(CurrentUserId, portfolioId, request);
            if (!success) return BadRequest(new { Message = "Không thể cập nhật tác phẩm này." });

            return Ok(new { Message = "Đã cập nhật tác phẩm thành công!" });
        }

        [Authorize]
        [HttpDelete("portfolio/{portfolioId}")]
        public async Task<IActionResult> DeletePortfolioImage(Guid portfolioId)
        {
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
            if (roleClaim != UserRole.MUA.ToString() && roleClaim != UserRole.Admin.ToString()) return Forbid();

            var success = await _muaService.DeletePortfolioAsync(CurrentUserId, portfolioId);
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
            if (roleClaim != UserRole.MUA.ToString() && roleClaim != UserRole.Admin.ToString()) return Forbid();

            var success = await _muaService.UpdateStylesAsync(CurrentUserId, styleIds);
            if (!success) return BadRequest(new { Message = "Không thể cập nhật danh sách phong cách trang điểm." });

            return Ok(new { Message = "Cập nhật danh sách phong cách thế mạnh thành công!" });
        }
    }
}
