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

        [HttpPost("create")]
        public async Task<IActionResult> CreateBooking([FromBody] BookingCreateDto createDto)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                if (CurrentUserId == createDto.MUAId)
                {
                    return BadRequest(new { Message = "Không thể tự đặt lịch cho chính mình." });
                }

                var booking = await _bookingService.CreateBookingAsync(CurrentUserId, createDto);
                if (booking == null)
                {
                    return BadRequest(new { Message = "Đặt lịch thất bại. Gói dịch vụ không tồn tại hoặc không thuộc Makeup Artist đã chọn." });
                }

                return Ok(new { Message = "Đặt lịch hẹn thành công! Booking đang ở trạng thái Pending.", Booking = booking });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Message = ex.Message,
                    Inner = ex.InnerException?.Message,
                    StackTrace = ex.StackTrace
                });
            }
        }
        
        [HttpGet]
        public async Task<IActionResult> GetBookings([FromQuery] string viewAs = "customer")
        {
            var bookings = await _bookingService.GetBookingsAsync(CurrentUserId, viewAs);
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
            var booking = await _bookingService.GetBookingByIdAsync(id, CurrentUserId);
            if (booking == null)
            {
                return NotFound(new { Message = "Không tìm thấy đơn đặt lịch." });
            }

            var success = await _bookingService.UpdateBookingStatusAsync(id, CurrentUserId, updateDto.Status);
            if (!success)
            {
                return BadRequest(new { Message = "Cập nhật trạng thái lịch hẹn thất bại. Makeup Artist chỉ có thể duyệt/từ chối lịch Pending hoặc hoàn thành lịch Approved của mình." });
            }

            string statusMsg = updateDto.Status switch
            {
                BookingStatus.Approved => "đã duyệt lịch hẹn và cam kết thực hiện",
                BookingStatus.WaitingCustomer => "đã tải lên bằng chứng. Chờ khách hàng xác nhận",
                BookingStatus.Completed => "đã hoàn thành. Tiền cọc (trừ phí dịch vụ) đã giải ngân sang ví Makeup Artist",
                BookingStatus.Cancelled => "đã bị hủy bỏ. Tiền cọc đã tự động hoàn trả đầy đủ vào ví khách hàng",
                _ => "đã được cập nhật"
            };

            return Ok(new { Message = $"Đơn đặt lịch #{id.ToString().Substring(0, 8)} {statusMsg}!" });
        }
    }
}
