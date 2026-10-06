using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Controllers;

[ApiController, Authorize(Roles = nameof(UserRole.Admin))]
[Route("api/admin/work-summary")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminWorkSummaryController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        // Count the complete queues, never the current page. DbContext queries run sequentially.
        var verification = await db.MakeupArtistProfiles.CountAsync(x => x.VerificationStatus == MuaVerificationStatus.PendingReview && x.User != null && !x.User.IsDemoAccount && x.User.DeletedAt == null, ct);
        var banks = await db.BankAccounts.CountAsync(x => x.IsActive && x.VerificationStatus == "PENDING_ADMIN" && x.User != null && !x.User.IsDemoAccount && x.User.DeletedAt == null, ct);
        var complaints = await db.BookingComplaints.CountAsync(x => x.IsOpen && x.Booking != null && !x.Booking.IsDemo, ct);
        var reports = await db.ContentReports.CountAsync(x => x.Status == "Pending" && db.Users.Any(u => u.UserId == x.ReporterId && !u.IsDemoAccount) && db.Users.Any(u => u.UserId == x.TargetOwnerId && !u.IsDemoAccount), ct);
        // Match the financial queues' persisted domain checks, including linked records.
        var normalBookings = db.Bookings.Where(x => !x.IsDemo && x.Customer != null && !x.Customer.IsDemoAccount && x.MakeupArtistProfile != null && x.MakeupArtistProfile.User != null && !x.MakeupArtistProfile.User.IsDemoAccount && !db.BookingPayments.Any(p => p.BookingId == x.BookingId && p.Provider != PaymentProvider.PayOS));
        var payouts = db.Payouts.Where(x => x.Mua != null && x.Mua.User != null && !x.Mua.User.IsDemoAccount && (x.Provider == PayoutProvider.Manual || x.Provider == PayoutProvider.PayOS) && x.BankAccount != null && x.BankAccount.UserId == x.MuaId && x.Items.Any() && !x.Items.Any(i => i.MuaReceivable == null || i.MuaReceivable.MuaId != x.MuaId || !normalBookings.Any(b => b.BookingId == i.MuaReceivable.BookingId && b.MUAId == x.MuaId)));
        var refunds = db.Refunds.Where(x => normalBookings.Any(b => b.BookingId == x.BookingId) && x.BookingPayment != null && x.BookingPayment.Provider == PaymentProvider.PayOS && x.BookingPayment.BookingId == x.BookingId && x.Booking != null && x.BookingPayment.CustomerId == x.Booking.CustomerId && (x.DestinationBankAccountId == null || (x.DestinationBankAccount != null && x.DestinationBankAccount.UserId == x.Booking.CustomerId)));
        var payoutCount = await payouts.CountAsync(x => x.Status == PayoutStatus.Pending || x.Status == PayoutStatus.ManualActionRequired || (x.Status == PayoutStatus.Failed && x.ReconciledAt == null), ct);
        var refundCount = await refunds.CountAsync(x => x.Status == RefundStatus.Pending || x.Status == RefundStatus.ManualActionRequired || x.Status == RefundStatus.Failed, ct);
        var processingPayouts = await payouts.CountAsync(x => x.Status == PayoutStatus.Processing, ct);
        var processingRefunds = await refunds.CountAsync(x => x.Status == RefundStatus.Processing, ct);
        var feedback = await db.UserFeedbacks.CountAsync(x => (x.Status == "New" || x.Status == "InProgress") && !x.User.IsDemoAccount && x.User.DeletedAt == null, ct);
        return Ok(new {
            updatedAt = DateTime.UtcNow,
            counts = new Dictionary<string, int> {
                ["verification"] = verification, ["bank-accounts"] = banks,
                ["complaints"] = complaints, ["moderation"] = reports,
                ["payouts"] = payoutCount, ["refunds"] = refundCount, ["feedback"] = feedback
            },
            total = verification + banks + complaints + reports + payoutCount + refundCount + feedback,
            processingPayouts, processingRefunds
        });
    }
}
