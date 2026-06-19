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
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class BookingController : ControllerBase
    {
        private readonly IBookingService _bookingService;

        public BookingController(IBookingService bookingService)
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

        [HttpPost]
        public async Task<IActionResult> CreateBooking([FromBody] BookingCreateDto createDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // Chỉ khách hàng mới được đặt lịch
            if (CurrentUserRole != UserRole.Customer)
            {
                return BadRequest(new { Message = "Chỉ tài khoản khách hàng mới được thực hiện đặt lịch hẹn." });
            }

            var booking = await _bookingService.CreateBookingAsync(CurrentUserId, createDto);
            if (booking == null)
            {
                return BadRequest(new { Message = "Đặt lịch thất bại. Gói dịch vụ không tồn tại hoặc không thuộc Makeup Artist đã chọn." });
            }

            return Ok(new { Message = "Đặt lịch hẹn thành công! Booking đang ở trạng thái Pending.", Booking = booking });
        }
        [HttpGet]
        public async Task<IActionResult> GetBookings()
        {
            var bookings = await _bookingService.GetBookingsAsync(CurrentUserId, CurrentUserRole);
            return Ok(bookings);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetBookingById(Guid id)
        {
            var booking = await _bookingService.GetBookingByIdAsync(id, CurrentUserId);
            if (booking == null)
            {
                return NotFound(new { Message = "Không tìm thấy thông tin đơn đặt lịch này." });
            }
            return Ok(booking);
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateBookingStatus(Guid id, [FromBody] BookingStatusUpdateDto updateDto)
        {
            if (CurrentUserRole != UserRole.MUA)
            {
                return Forbid();
            }

            // Kiểm tra tính hợp lệ của trạng thái đổi
            var booking = await _bookingService.GetBookingByIdAsync(id, CurrentUserId);
            if (booking == null)
            {
                return NotFound(new { Message = "Không tìm thấy đơn đặt lịch." });
            }

            var success = await _bookingService.UpdateBookingStatusAsync(id, CurrentUserId, CurrentUserRole, updateDto.Status);
            if (!success)
            {
                return BadRequest(new { Message = "Cập nhật trạng thái lịch hẹn thất bại. Makeup Artist chỉ có thể duyệt/từ chối lịch Pending hoặc hoàn thành lịch Approved của mình." });
            }

            string statusMsg = updateDto.Status switch
            {
                BookingStatus.Approved => "đã duyệt lịch hẹn và cam kết thực hiện",
                BookingStatus.Completed => "đã hoàn thành. Tiền cọc (trừ phí dịch vụ) đã giải ngân sang ví Makeup Artist",
                BookingStatus.Cancelled => "đã bị hủy bỏ. Tiền cọc đã tự động hoàn trả đầy đủ vào ví khách hàng",
                _ => "đã được cập nhật"
            };

            return Ok(new { Message = $"Đơn đặt lịch #{id.ToString().Substring(0, 8)} {statusMsg}!" });
        }
    }
}
