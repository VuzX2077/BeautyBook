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
    public class ReviewController : ControllerBase
    {
        private readonly IBookingService _bookingService;

        public ReviewController(IBookingService bookingService)
        {
            _bookingService = bookingService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());
        private UserRole CurrentUserRole
        {
            get
            {
                var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
                return Enum.TryParse<UserRole>(roleClaim, true, out var role) ? role : UserRole.Customer;
            }
        }

        [HttpGet("mua/{muaId}")]
        public async Task<IActionResult> GetReviewsByMua(Guid muaId)
        {
            var reviews = await _bookingService.GetMuaReviewsAsync(muaId);
            return Ok(reviews);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> AddReview([FromBody] ReviewCreateWithBookingDto reviewDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            return await AddReviewForBooking(reviewDto.BookingId, reviewDto);
        }

        [Authorize]
        [HttpPost("booking/{bookingId}")]
        public async Task<IActionResult> AddReview(Guid bookingId, [FromBody] ReviewCreateDto reviewDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            return await AddReviewForBooking(bookingId, reviewDto);
        }

        private async Task<IActionResult> AddReviewForBooking(Guid bookingId, ReviewCreateDto reviewDto)
        {
            if (CurrentUserRole != UserRole.Customer)
            {
                return BadRequest(new { Message = "Chỉ tài khoản khách hàng mới có thể gửi đánh giá dịch vụ." });
            }

            var success = await _bookingService.AddReviewAsync(bookingId, CurrentUserId, reviewDto);
            if (!success)
            {
                return BadRequest(new { Message = "Gửi đánh giá thất bại. Bạn chỉ có thể đánh giá các lịch hẹn của mình đã hoàn thành (Completed) và chưa được đánh giá trước đó." });
            }

            return Ok(new { Message = "Cảm ơn bạn đã gửi đánh giá! Phản hồi của bạn đã được ghi nhận." });
        }

        [Authorize]
        [HttpPost("{reviewId}/reply")]
        public async Task<IActionResult> AddReply(Guid reviewId, [FromBody] ReviewReplyDto replyDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (CurrentUserRole != UserRole.MUA && CurrentUserRole != UserRole.Admin)
            {
                return BadRequest(new { Message = "Chỉ Make Up Artist hoặc Admin mới có quyền phản hồi đánh giá." });
            }

            bool isAdmin = CurrentUserRole == UserRole.Admin;
            var success = await _bookingService.ReplyReviewAsync(reviewId, CurrentUserId, replyDto.ReplyContent, isAdmin);
            if (!success)
            {
                return BadRequest(new { Message = "Phản hồi đánh giá thất bại. Đánh giá không tồn tại hoặc không thuộc về bạn." });
            }

            return Ok(new { Message = "Phản hồi thành công!" });
        }
    }
}
