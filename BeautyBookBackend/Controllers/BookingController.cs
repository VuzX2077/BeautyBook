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

                var booking = await _bookingService.CreateBookingAsync(CurrentUserId, createDto);
                if (booking == null)
                {
                    return BadRequest(new { Code = "BOOKING_CREATION_FAILED", Message = "Không thể tạo booking." });
                }

                return Ok(new { Message = "Đã tạo booking. Vui lòng thanh toán tiền cọc 30%.", Booking = booking });
            }
            catch (BookingRuleException ex)
            {
                return StatusCode(ex.StatusCode, new { ex.Code, Message = ex.Message });
            }
            catch (InsufficientBalanceException ex)
            {
                return BadRequest(new
                {
                    Code = "INSUFFICIENT_BALANCE",
                    Message = ex.Message,
                    RequiredAmount = ex.RequiredAmount,
                    CurrentBalance = ex.CurrentBalance,
                    MissingAmount = ex.MissingAmount
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Code = "INVALID_BOOKING_OPERATION", Message = ex.Message });
            }
            catch (BookingConcurrencyException ex)
            {
                return Conflict(new { Code = "BOOKING_CONFLICT", Message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(500, new { Code = "INTERNAL_ERROR", Message = "Không thể tạo booking lúc này. Vui lòng thử lại." });
            }
        }

        [HttpPost("{id}/deposit-payment")]
        public async Task<IActionResult> CreateDepositPayment(Guid id)
        {
            try
            {
                var payment = await _bookingService.CreateDepositPaymentAsync(id, CurrentUserId);
                return payment == null
                    ? NotFound(new { Message = "Không tìm thấy booking của khách hàng." })
                    : Ok(payment);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (BookingConcurrencyException ex)
            {
                return Conflict(new { Message = ex.Message });
            }
        }

        [AllowAnonymous]
        [HttpPost("payos/webhook")]
        public async Task<IActionResult> PayOsWebhook([FromBody] PayOsWebhookDto webhook)
        {
            var handled = await _bookingService.HandlePayOsWebhookAsync(webhook);
            return handled
                ? Ok(new { Message = "Webhook payOS đã được xử lý." })
                : BadRequest(new { Message = "Webhook payOS không hợp lệ hoặc không khớp payment." });
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

            try
            {
                var updated = await _bookingService.UpdateBookingStatusAsync(id, CurrentUserId, updateDto.Status, updateDto.Reason);
                if (updated == null)
                    return BadRequest(new { Message = "Chuyển trạng thái không hợp lệ hoặc không đúng quyền." });
                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (BookingConcurrencyException ex)
            {
                return Conflict(new { Message = ex.Message });
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{id}/resolve-dispute")]
        public async Task<IActionResult> ResolveDispute(Guid id, [FromBody] DisputeResolutionDto dto)
        {
            var booking = await _bookingService.ResolveDisputeAsync(id, dto.RefundCustomer);
            return booking == null
                ? BadRequest(new { Message = "Booking không ở trạng thái tranh chấp." })
                : Ok(booking);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("auto-complete-overdue")]
        public async Task<IActionResult> AutoCompleteOverdue()
        {
            var count = await _bookingService.AutoCompleteOverdueAsync();
            return Ok(new { CompletedBookings = count });
        }
    }
}
