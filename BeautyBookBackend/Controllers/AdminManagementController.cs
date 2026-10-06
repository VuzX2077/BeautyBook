using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Controllers;

[ApiController]
[Authorize(Roles = nameof(UserRole.Admin))]
[Route("api/admin/management")]
public sealed class AdminManagementController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> Users(string? search, string? role, bool active = true, int page = 1, int pageSize = 20)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100 || search?.Length > 200) return BadRequest();
        var q = db.Users.AsNoTracking().Where(x => !x.DeletedAt.HasValue && !x.IsDemoAccount && x.Role != UserRole.Admin && x.IsActive == active);
        if (!string.IsNullOrEmpty(role)) {
            if (!Enum.TryParse<UserRole>(role, true, out var r) || r == UserRole.Admin || !Enum.IsDefined(r)) return BadRequest();
            q = q.Where(x => x.Role == r);
        }
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToLower(); if (Guid.TryParse(term, out var id)) q = q.Where(x => x.UserId == id); else q = q.Where(x => (x.FullName != null && x.FullName.ToLower().Contains(term)) || (x.Email != null && x.Email.ToLower().Contains(term))); }
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.UserId).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.UserId, FullName = x.FullName ?? "", Email = x.Email ?? "", Role = x.Role.ToString(), x.AvatarUrl, x.IsActive }).ToListAsync();
        Response.Headers.CacheControl = "no-store";
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("financial-summary")]
    public async Task<IActionResult> FinancialSummary(bool refund = true)
    {
        // Aggregate all persisted statuses, independently of the list filter.
        var normalBookings = db.Bookings.Where(x => !x.IsDemo && x.Customer != null && !x.Customer.IsDemoAccount && x.MakeupArtistProfile != null && x.MakeupArtistProfile.User != null && !x.MakeupArtistProfile.User.IsDemoAccount && !db.BookingPayments.Any(p => p.BookingId == x.BookingId && p.Provider != PaymentProvider.PayOS));
        var payouts = db.Payouts.AsNoTracking().Where(x => x.Mua != null && x.Mua.User != null && !x.Mua.User.IsDemoAccount && (x.Provider == PayoutProvider.Manual || x.Provider == PayoutProvider.PayOS) && x.BankAccount != null && x.BankAccount.UserId == x.MuaId && x.Items.Any() && !x.Items.Any(i => i.MuaReceivable == null || i.MuaReceivable.MuaId != x.MuaId || !normalBookings.Any(b => b.BookingId == i.MuaReceivable.BookingId && b.MUAId == x.MuaId)));
        var refunds = db.Refunds.AsNoTracking().Where(x => normalBookings.Any(b => b.BookingId == x.BookingId) && x.BookingPayment != null && x.BookingPayment.Provider == PaymentProvider.PayOS && x.BookingPayment.BookingId == x.BookingId && x.Booking != null && x.BookingPayment.CustomerId == x.Booking.CustomerId && (x.DestinationBankAccountId == null || (x.DestinationBankAccount != null && x.DestinationBankAccount.UserId == x.Booking.CustomerId)));
        var rows = refund
            ? await refunds
                .GroupBy(x => (int)x.Status).Select(g => new FinancialStatusTotal(g.Key, g.Count(), g.Sum(x => x.Amount))).ToListAsync()
            : await payouts
                .GroupBy(x => (int)x.Status).Select(g => new FinancialStatusTotal(g.Key, g.Count(), g.Sum(x => x.Amount))).ToListAsync();
        Response.Headers.CacheControl = "no-store";
        return Ok(BuildFinancialSummary(rows, refund));
    }

    public static object BuildFinancialSummary(IReadOnlyList<FinancialStatusTotal> rows, bool refund) {
        object Bucket(params int[] statuses) => new { Count = rows.Where(x => statuses.Contains(x.Status)).Sum(x => x.Count), Amount = rows.Where(x => statuses.Contains(x.Status)).Sum(x => x.Amount) };
        return refund
            ? new { Pending = Bucket((int)RefundStatus.Pending, (int)RefundStatus.ManualActionRequired, (int)RefundStatus.AwaitingDestination), Processing = Bucket((int)RefundStatus.Processing), Completed = Bucket((int)RefundStatus.Completed), Failed = Bucket((int)RefundStatus.Failed) }
            : new { Pending = Bucket((int)PayoutStatus.Pending, (int)PayoutStatus.ManualActionRequired), Processing = Bucket((int)PayoutStatus.Processing), Completed = Bucket((int)PayoutStatus.Paid), Failed = Bucket((int)PayoutStatus.Failed) };
    }
}

public sealed record FinancialStatusTotal(int Status, int Count, decimal Amount);
