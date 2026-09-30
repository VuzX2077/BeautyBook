using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using BeautyBookBackend.Services;

namespace BeautyBookBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FeedController : ControllerBase
    {
        private readonly IFeedService _feedService;

        public FeedController(IFeedService feedService)
        {
            _feedService = feedService;
        }

        private Guid? CurrentUserId
        {
            get
            {
                var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (Guid.TryParse(idClaim, out var userId))
                {
                    return userId;
                }
                return null;
            }
        }

        [HttpGet("scroll")]
        public async Task<IActionResult> Scroll([FromQuery] int limit = 20, [FromQuery] string? cursor = null)
        {
            if (limit < 1 || limit > 50 || cursor?.Length > 64) return BadRequest();
            try { return Ok(await _feedService.GetFeedPageAsync(limit, CurrentUserId, cursor)); }
            catch (ArgumentException) { return BadRequest(new { message = "Cursor không hợp lệ." }); }
            catch (FeedSnapshotExpiredException) { return StatusCode(410, new { message = "Phiên bảng tin đã hết hạn. Vui lòng làm mới." }); }
        }

        [HttpGet]
        public async Task<IActionResult> GetFeed([FromQuery] int page = 1, [FromQuery] int limit = 20)
        {
            var feed = await _feedService.GetFeedAsync(page, limit, CurrentUserId);
            return Ok(feed);
        }
    }
}
