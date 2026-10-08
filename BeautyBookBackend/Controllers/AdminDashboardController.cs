using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Controllers;

[ApiController]
[Authorize(Roles = nameof(UserRole.Admin))]
[Route("api/admin/dashboard")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
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
            var newMuas = await users.CountAsync(x => x.Role == UserRole.MUA && x.CreatedAt >= a && x.CreatedAt < b, ct);
            var totalBookings = await db.Bookings.CountAsync(x => !x.IsDemo && x.CreatedAt >= a && x.CreatedAt < b, ct);
            // PaidAt remains the evidence of collection after a payment transitions to refunded/forfeited.
            var depositsCollected = await db.BookingPayments.AsNoTracking().Where(x => x.Booking != null && !x.Booking.IsDemo && x.Provider == PaymentProvider.PayOS && x.PaidAt >= a && x.PaidAt < b).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var refundsCompleted = await db.Refunds.AsNoTracking().Where(x => x.Booking != null && !x.Booking.IsDemo && x.Status == RefundStatus.Completed && x.CompletedAt >= a && x.CompletedAt < b).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var successfulTransactions = await db.BookingPayments.AsNoTracking().CountAsync(x => x.Booking != null && !x.Booking.IsDemo && x.Provider == PaymentProvider.PayOS && x.PaidAt >= a && x.PaidAt < b, ct);
            return new { revenue, bookingValue, completedBookings, newUsers, newMuas, totalBookings, depositsCollected, refundsCompleted, successfulTransactions };
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
            .Select(g => new { Day = g.Key, Value = g.Count(), Muas = g.Count(x => x.Role == UserRole.MUA) }).ToListAsync(ct);
        var bookingDays = await db.Bookings.AsNoTracking().Where(x => !x.IsDemo && x.CreatedAt >= start && x.CreatedAt < end)
            .GroupBy(x => x.CreatedAt.AddHours(7).Date).Select(g => new { Day = g.Key, Value = g.Count() }).ToListAsync(ct);
        var statuses = await db.Bookings.AsNoTracking().Where(x => !x.IsDemo && x.CreatedAt >= start && x.CreatedAt < end)
            .GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        // Service reviews are independent of app feedback. Exclude demo/inconsistent links
        // and reviews removed through moderation, matching marketplace visibility.
        var reviews = VisibleReviews();
        var periodReviews = reviews.Where(x => x.CreatedAt >= start && x.CreatedAt < end);
        var ratingCounts = await periodReviews.GroupBy(x => x.Rating)
            .Select(g => new { rating = g.Key, count = g.Count() }).ToListAsync(ct);
        var reviewTotal = ratingCounts.Sum(x => x.count);
        var reviewedCompletedBookings = await completed.Where(x => x.CompletedAt >= start && x.CompletedAt < end)
            .CountAsync(x => reviews.Any(r => r.BookingId == x.BookingId), ct);
        var eligibleCompletedBookings = await completed.CountAsync(x => x.CompletedAt >= start && x.CompletedAt < end, ct);
        var recentReviews = await periodReviews.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.ReviewId).Take(4)
            .Select(x => new { x.ReviewId, x.BookingId, x.Rating, x.Comment, x.CreatedAt,
                customerName = db.Users.Where(u => u.UserId == x.CustomerId).Select(u => u.DeletedAt == null ? u.FullName : "Người dùng đã xóa").FirstOrDefault(),
                muaName = db.Users.Where(u => u.UserId == x.MUAId).Select(u => u.DeletedAt == null ? u.FullName : "Người dùng đã xóa").FirstOrDefault() }).ToListAsync(ct);
        var daily = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => {
            var day = from.AddDays(i).ToDateTime(TimeOnly.MinValue);
            return new { date = from.AddDays(i).ToString("yyyy-MM-dd"), revenue = revenueDays.FirstOrDefault(x => x.Day == day)?.Value ?? 0, newUsers = userDays.FirstOrDefault(x => x.Day == day)?.Value ?? 0,
                newMuas = userDays.FirstOrDefault(x => x.Day == day)?.Muas ?? 0, bookings = bookingDays.FirstOrDefault(x => x.Day == day)?.Value ?? 0 };
        });
        return Ok(new { from, to, timezone = "Asia/Ho_Chi_Minh", current, previous,
            users = new { total = customers + muas, customers, muas, locked }, daily,
            bookingStatuses = statuses.OrderBy(x => x.Status).Select(x => new { status = x.Status.ToString(), count = x.Count }),
            serviceReviews = new {
                total = reviewTotal,
                averageRating = reviewTotal == 0 ? (double?)null : ratingCounts.Sum(x => x.rating * (double)x.count) / reviewTotal,
                lowRatingCount = ratingCounts.Where(x => x.rating <= 2).Sum(x => x.count),
                distribution = Enumerable.Range(1, 5).Reverse().Select(rating => new { rating, count = ratingCounts.FirstOrDefault(x => x.rating == rating)?.count ?? 0 }),
                reviewedCompletedBookings, eligibleCompletedBookings, recent = recentReviews
            } });
    }

    private IQueryable<Review> VisibleReviews() => db.Reviews.AsNoTracking().Where(x => x.Booking != null && !x.Booking.IsDemo
        && x.CustomerId == x.Booking.CustomerId && x.MUAId == x.Booking.MUAId
        && db.Users.Any(u => u.UserId == x.CustomerId && !u.IsDemoAccount)
        && db.Users.Any(u => u.UserId == x.MUAId && !u.IsDemoAccount)
        && x.Rating >= 1 && x.Rating <= 5
        && !db.ContentReports.Any(r => r.TargetType == "Review" && r.TargetId == x.ReviewId && r.Status == "Removed"));

    [HttpGet("reviews")]
    public async Task<IActionResult> Reviews([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int? rating,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!DashboardDateRange.TryCreate(from, to, out var start, out var end, out _) || rating is < 1 or > 5 || page < 1 || page > 100000 || pageSize < 1 || pageSize > 50)
            return BadRequest(new { Message = "Khoảng ngày, số sao hoặc phân trang không hợp lệ." });
        var query = VisibleReviews().Where(x => x.CreatedAt >= start && x.CreatedAt < end);
        if (rating.HasValue) query = query.Where(x => x.Rating == rating.Value);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.ReviewId).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.ReviewId, x.BookingId, x.Rating, x.Comment, x.CreatedAt, hasImage = x.ImageUrl != null && x.ImageUrl != "",
                customerName = db.Users.Where(u => u.UserId == x.CustomerId).Select(u => u.DeletedAt == null ? u.FullName : "Người dùng đã xóa").FirstOrDefault(),
                muaName = db.Users.Where(u => u.UserId == x.MUAId).Select(u => u.DeletedAt == null ? u.FullName : "Người dùng đã xóa").FirstOrDefault() }).ToListAsync(ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("reviews/{id:guid}")]
    public async Task<IActionResult> ReviewDetail(Guid id, CancellationToken ct)
    {
        var item = await VisibleReviews().Where(x => x.ReviewId == id).Select(x => new {
            x.ReviewId, x.BookingId, x.Rating, x.Comment, x.ImageUrl, x.CreatedAt, x.MuaReply, x.MuaReplyAt, x.CustomerId, x.MUAId,
            customerName = db.Users.Where(u => u.UserId == x.CustomerId).Select(u => u.DeletedAt == null ? u.FullName : "Người dùng đã xóa").FirstOrDefault(),
            muaName = db.Users.Where(u => u.UserId == x.MUAId).Select(u => u.DeletedAt == null ? u.FullName : "Người dùng đã xóa").FirstOrDefault(),
            booking = new { x.Booking!.BookingDate, x.Booking.StartTime, status = x.Booking.Status.ToString(), x.Booking.TotalAmount,
                services = x.Booking.BookingServices.Select(s => new { s.ServiceName, s.ParticipantsCount }) }
        }).FirstOrDefaultAsync(ct);
        return item == null ? NotFound(new { Message = "Không tìm thấy đánh giá." }) : Ok(item);
    }
}
