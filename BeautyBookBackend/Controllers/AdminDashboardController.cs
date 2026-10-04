using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Controllers;

[ApiController]
[Authorize(Roles = nameof(UserRole.Admin))]
[Route("api/admin/dashboard")]
public sealed class AdminDashboardController(ApplicationDbContext db) : ControllerBase
{
    // All date filters are calendar dates in Vietnam (UTC+7), with an exclusive UTC end.
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        if (!DashboardDateRange.TryCreate(from, to, out var start, out var end, out var previousStart))
            return BadRequest(new { Message = "Chọn khoảng ngày hợp lệ, tối đa 366 ngày." });
        var users = db.Users.AsNoTracking().Where(x => !x.IsDemoAccount && x.DeletedAt == null && (x.Role == UserRole.Customer || x.Role == UserRole.MUA));
        var completed = db.Bookings.AsNoTracking().Where(x => !x.IsDemo && x.CompletedAt != null && (x.Status == BookingStatus.Completed || x.Status == BookingStatus.AutoCompleted));
        async Task<object> Period(DateTime a, DateTime b)
        {
            var bookings = completed.Where(x => x.CompletedAt >= a && x.CompletedAt < b);
            var revenue = await bookings.SumAsync(x => (decimal?)x.PlatformFeeAmount, ct) ?? 0;
            var bookingValue = await bookings.SumAsync(x => (decimal?)x.TotalAmount, ct) ?? 0;
            var completedBookings = await bookings.CountAsync(ct);
            var newUsers = await users.CountAsync(x => x.CreatedAt >= a && x.CreatedAt < b, ct);
            // PaidAt remains the evidence of collection after a payment transitions to refunded/forfeited.
            var depositsCollected = await db.BookingPayments.AsNoTracking().Where(x => x.Booking != null && !x.Booking.IsDemo && x.Provider == PaymentProvider.PayOS && x.PaidAt >= a && x.PaidAt < b).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var refundsCompleted = await db.Refunds.AsNoTracking().Where(x => x.Booking != null && !x.Booking.IsDemo && x.Status == RefundStatus.Completed && x.CompletedAt >= a && x.CompletedAt < b).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            return new { revenue, bookingValue, completedBookings, newUsers, depositsCollected, refundsCompleted };
        }
        var current = await Period(start, end);
        var previous = await Period(previousStart, start);
        var customers = await users.CountAsync(x => x.Role == UserRole.Customer, ct);
        var muas = await users.CountAsync(x => x.Role == UserRole.MUA, ct);
        var locked = await users.CountAsync(x => !x.IsActive, ct);
        var revenueDays = await completed.Where(x => x.CompletedAt >= start && x.CompletedAt < end)
            .GroupBy(x => x.CompletedAt!.Value.AddHours(7).Date)
            .Select(g => new { Day = g.Key, Value = g.Sum(x => x.PlatformFeeAmount) }).ToListAsync(ct);
        var userDays = await users.Where(x => !x.IsDemoAccount && x.CreatedAt >= start && x.CreatedAt < end)
            .GroupBy(x => x.CreatedAt.AddHours(7).Date)
            .Select(g => new { Day = g.Key, Value = g.Count() }).ToListAsync(ct);
        var statuses = await db.Bookings.AsNoTracking().Where(x => !x.IsDemo && x.CreatedAt >= start && x.CreatedAt < end)
            .GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var daily = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => {
            var day = from.AddDays(i).ToDateTime(TimeOnly.MinValue);
            return new { date = from.AddDays(i).ToString("yyyy-MM-dd"), revenue = revenueDays.FirstOrDefault(x => x.Day == day)?.Value ?? 0, newUsers = userDays.FirstOrDefault(x => x.Day == day)?.Value ?? 0 };
        });
        return Ok(new { from, to, timezone = "Asia/Ho_Chi_Minh", current, previous,
            users = new { total = customers + muas, customers, muas, locked }, daily,
            bookingStatuses = statuses.Select(x => new { status = x.Status.ToString(), count = x.Count }) });
    }
}
