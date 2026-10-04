using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

// Domain authority is persisted server state. No client flag or demo JWT claim is used.
public sealed class PlayReviewPolicy(ApplicationDbContext db, Microsoft.Extensions.Options.IOptionsMonitor<PlayReviewOptions>? options = null)
{
    public PlayReviewOptions Simulation => options?.CurrentValue ?? new();

    public async Task EnsureReviewPairAsync(Guid caller, Guid customer, Guid mua)
    {
        var settings = Simulation;
        if (!settings.SimulationEnabled || settings.ReviewUserId == Guid.Empty || settings.CounterpartUserId == Guid.Empty
            || settings.ReviewUserId == settings.CounterpartUserId || caller != settings.ReviewUserId
            || !((customer == settings.ReviewUserId && mua == settings.CounterpartUserId)
                || (customer == settings.CounterpartUserId && mua == settings.ReviewUserId))) throw Denied();
        foreach (var id in new[] { customer, mua }.OrderBy(x => x))
        {
            if (db.Database.CurrentTransaction != null)
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Users\" WHERE \"UserId\"={id} FOR SHARE");
            if (!await db.Users.AsNoTracking().AnyAsync(x => x.UserId == id && x.IsDemoAccount && x.IsActive && x.DeletedAt == null)) throw Denied();
        }
        if (!await db.MakeupArtistProfiles.AnyAsync(x => x.MUAId == mua && x.Status != MuaStatus.Suspended)) throw Denied();
    }

    public async Task EnsureDemoBookingAsync(Guid bookingId, Guid caller)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.BookingId == bookingId) ?? throw Denied();
        await EnsureReviewPairAsync(caller, booking.CustomerId, booking.MUAId);
        if (!await EnsureBookingDomainAsync(bookingId)) throw Denied();
        if (booking.TotalAmount <= 0) throw Denied();
        var calculated = BookingFinancialCalculator.Calculate(booking.TotalAmount);
        if (booking.DepositAmount != calculated.DepositAmount || booking.PlatformFeeAmount != calculated.PlatformFeeAmount || booking.MuaPayoutAmount != calculated.MuaDepositPayoutAmount || booking.RemainingAmount != calculated.RemainingAmount) throw Denied();
        var payments = await db.BookingPayments.AsNoTracking().Where(x => x.BookingId == bookingId).ToListAsync();
        if (booking.DepositAmount <= 0 || payments.Count > 1 || payments.Any(x => x.Provider != PaymentProvider.Simulated
            || x.CustomerId != booking.CustomerId || x.Amount != booking.DepositAmount || x.ProviderOrderCode >= 0)) throw Denied();
        var refunds = await db.Refunds.AsNoTracking().Where(x => x.BookingId == bookingId).ToListAsync();
        if (refunds.Count > 1) throw Denied();
        if (refunds.Count == 1)
        {
            var refund = refunds[0];
            if (payments.Count != 1 || refund.BookingPaymentId != payments[0].PaymentId || refund.Amount <= 0 || refund.Amount > payments[0].Amount
                || refund.Status != RefundStatus.Completed || !refund.CompletedAt.HasValue
                || booking.Status is not (BookingStatus.Cancelled or BookingStatus.Rejected)
                || booking.PaymentStatus is not (PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
                || payments[0].Status is not (BookingPaymentStatus.Refunded or BookingPaymentStatus.PartiallyRefunded)
                || refund.DestinationBankAccountId.HasValue || refund.ProviderPayoutId != null || refund.ProviderReferenceId != null) throw Denied();
        }
    }

    public async Task EnsureDemoMuaCapabilityAsync(Guid caller, Guid customer, Guid mua, BeautyBookBackend.DTOs.MuaEligibilityDto? eligibility)
    {
        await EnsureReviewPairAsync(caller, customer, mua);
        string[] required = ["accountActive", "basicInformation", "avatar", "city", "specialty", "activeService", "publicPortfolioImages"];
        if (eligibility == null || required.Any(key => !eligibility.Requirements.Any(x => x.Key == key && x.IsMet))) throw Denied();
        if (!await db.MuaWorkingSchedules.AnyAsync(x => x.MUAId == mua && x.IsActive && x.StartTime < x.EndTime)) throw Denied();
    }

    public async Task EnsureDemoPaymentAsync(Booking booking, BookingPayment payment, Guid caller)
    {
        await EnsureDemoBookingAsync(booking.BookingId, caller);
        if (payment.BookingId != booking.BookingId || payment.CustomerId != booking.CustomerId
            || payment.Provider != PaymentProvider.Simulated || payment.Amount != booking.DepositAmount || payment.ProviderOrderCode >= 0) throw Denied();
    }
    public async Task<Guid?> GetCounterpartEntryAsync(Guid caller)
    {
        if (!Simulation.SimulationEnabled || caller != Simulation.ReviewUserId) return null;
        try { await EnsureReviewPairAsync(caller, caller, Simulation.CounterpartUserId); return Simulation.CounterpartUserId; }
        catch (PlayReviewOperationException) { return null; }
    }

    public async Task<Guid?> GetSampleBankCapabilityAsync(Guid caller)
    {
        if (!Simulation.SimulationEnabled || caller != Simulation.ReviewUserId || Simulation.SampleBankAccountId == Guid.Empty) return null;
        try
        {
            await EnsureReviewPairAsync(caller, Simulation.CounterpartUserId, caller);
            if (!await db.BankAccounts.AnyAsync(x => x.Id == Simulation.SampleBankAccountId && x.UserId == caller && x.IsActive
                && x.VerificationStatus == BankAccountEligibility.Pending && x.FinancialQrMediaId == null)) return null;
            return Simulation.SampleBankAccountId;
        }
        catch (PlayReviewOperationException) { return null; }
    }

    public async Task EnsureDemoAvailableReceivableAsync(MuaReceivable row, Guid caller)
    {
        await EnsureDemoBookingAsync(row.BookingId, caller);
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.BookingId == row.BookingId) ?? throw Denied();
        var payments = await db.BookingPayments.AsNoTracking().Where(x => x.BookingId == row.BookingId).ToListAsync();
        if (row.MuaId != caller || booking.MUAId != caller || row.Status != MuaReceivableStatus.Available || row.AvailableAt > DateTime.UtcNow
            || row.NetAmount <= 0 || row.NetAmount != booking.MuaPayoutAmount || row.GrossAmount != booking.DepositAmount || row.PlatformFeeAmount != booking.PlatformFeeAmount
            || booking.Status is not (BookingStatus.Completed or BookingStatus.AutoCompleted) || !booking.CompletedAt.HasValue
            || booking.PaymentStatus != PaymentStatus.Released || payments.Count != 1 || payments[0].Status != BookingPaymentStatus.Paid || !payments[0].PaidAt.HasValue
            || await db.BookingComplaints.AnyAsync(x => x.BookingId == row.BookingId && x.IsOpen)
            || await db.Refunds.AnyAsync(x => x.BookingId == row.BookingId)
            || await db.PayoutItems.AnyAsync(x => x.MuaReceivableId == row.Id && x.IsActive)) throw Denied();
    }
    private static PlayReviewOperationException Denied() => new();

    public async Task<bool> IsDemoUserAsync(Guid id)
    {
        var domain = await db.Users.AsNoTracking().Where(x => x.UserId == id)
            .Select(x => (bool?)x.IsDemoAccount).SingleOrDefaultAsync();
        return domain ?? throw Denied();
    }

    public async Task EnsureNormalUserAsync(Guid id)
    {
        if (await IsDemoUserAsync(id)) throw Denied();
    }

    public async Task EnsureExternalActorAsync(IHttpContextAccessor? accessor)
    {
        var principal = accessor?.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true) return;
        var claim = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(claim, out var actor)) throw Denied();
        await EnsureNormalUserAsync(actor);
    }

    public async Task<bool> EnsureSameDomainAsync(Guid a, Guid b)
    {
        var first = await IsDemoUserAsync(a);
        if (first != await IsDemoUserAsync(b)) throw Denied();
        return first;
    }

    public async Task<bool> EnsureBookingDomainAsync(Guid id)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.BookingId == id)
            ?? throw Denied();
        if (!await db.MakeupArtistProfiles.AnyAsync(x => x.MUAId == booking.MUAId)) throw Denied();
        var domain = await EnsureSameDomainAsync(booking.CustomerId, booking.MUAId);
        if (booking.IsDemo != domain) throw Denied();
        return domain;
    }

    public async Task EnsureNormalBookingAsync(Guid id)
    {
        if (await EnsureBookingDomainAsync(id)) throw Denied();
        if (await db.BookingPayments.AnyAsync(x => x.BookingId == id && x.Provider != PaymentProvider.PayOS)) throw Denied();
    }

    // Batch jobs quarantine unsupported/inconsistent resources and continue normal work.
    public async Task<bool> CanProcessBookingAsync(Guid id)
    {
        try { await EnsureNormalBookingAsync(id); return true; }
        catch (InvalidOperationException) { return false; }
    }
    public async Task<bool> CanProcessRefundAsync(Guid id)
    {
        try { await EnsureNormalRefundAsync(id); return true; }
        catch (InvalidOperationException) { return false; }
    }
    public async Task<bool> CanProcessPayoutAsync(Guid id)
    {
        try { await EnsureNormalPayoutAsync(id); return true; }
        catch (InvalidOperationException) { return false; }
    }

    public async Task EnsureNormalPaymentAsync(Guid id)
    {
        var payment = await db.BookingPayments.AsNoTracking().SingleOrDefaultAsync(x => x.PaymentId == id)
            ?? throw Denied();
        if (payment.Provider != PaymentProvider.PayOS) throw Denied();
        await EnsureNormalBookingAsync(payment.BookingId);
        var customer = await db.Bookings.Where(x => x.BookingId == payment.BookingId).Select(x => x.CustomerId).SingleAsync();
        if (customer != payment.CustomerId) throw Denied();
    }

    public async Task EnsureNormalRefundAsync(Guid id)
    {
        var refund = await db.Refunds.AsNoTracking().SingleOrDefaultAsync(x => x.RefundId == id) ?? throw Denied();
        var payment = await db.BookingPayments.AsNoTracking().SingleOrDefaultAsync(x => x.PaymentId == refund.BookingPaymentId);
        if (payment == null || payment.BookingId != refund.BookingId) throw Denied();
        await EnsureNormalPaymentAsync(payment.PaymentId);
        if (refund.DestinationBankAccountId.HasValue)
        {
            var bank = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == refund.DestinationBankAccountId);
            var customer = await db.Bookings.Where(x => x.BookingId == refund.BookingId).Select(x => x.CustomerId).SingleAsync();
            if (bank == null || bank.UserId != customer) throw Denied();
            await EnsureNormalUserAsync(bank.UserId);
        }
    }

    public async Task EnsureNormalPayoutAsync(Guid id)
    {
        var payout = await db.Payouts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw Denied();
        if (payout.Provider is not (PayoutProvider.Manual or PayoutProvider.PayOS)) throw Denied();
        await EnsureNormalUserAsync(payout.MuaId);
        var bank = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == payout.BankAccountId);
        if (bank == null || bank.UserId != payout.MuaId) throw Denied();
        var items = await db.PayoutItems.AsNoTracking().Where(x => x.PayoutId == id).ToListAsync();
        if (items.Count == 0) throw Denied();
        foreach (var item in items)
        {
            var row = await db.MuaReceivables.AsNoTracking().SingleOrDefaultAsync(x => x.Id == item.MuaReceivableId);
            if (row == null || row.MuaId != payout.MuaId) throw Denied();
            var owner = await db.Bookings.Where(x => x.BookingId == row.BookingId).Select(x => (Guid?)x.MUAId).SingleOrDefaultAsync();
            if (owner != payout.MuaId) throw Denied();
            await EnsureNormalBookingAsync(row.BookingId);
        }
    }

    public async Task EnsurePortfolioDomainAsync(Guid actor, Guid portfolio)
    {
        var owner = await db.Portfolios.Where(x => x.PortfolioId == portfolio).Select(x => (Guid?)x.MUAId).SingleOrDefaultAsync();
        await EnsureSameDomainAsync(actor, owner ?? throw Denied());
    }

    public async Task EnsureEmailDeliveryAsync(string email, string purpose)
    {
        var users = await EmailDomainsAsync(email);
        // Registration legitimately starts without a persisted account.
        if (users.Any(x => x) || users.Count > 1 || (users.Count == 0 && purpose != "REGISTER")) throw Denied();
    }

    public async Task EnsureEmailOriginAsync(string email)
    {
        var domains = await EmailDomainsAsync(email);
        if (domains.Any(x => x) || domains.Count > 1) throw Denied();
    }

    private Task<List<bool>> EmailDomainsAsync(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return db.Users.AsNoTracking().Where(x => x.Email != null && x.Email.ToLower() == normalized)
            .Select(x => x.IsDemoAccount).ToListAsync();
    }

    public async Task EnsureChatDomainAsync(Guid id, Guid actor)
    {
        var room = await db.ChatRooms.AsNoTracking().SingleOrDefaultAsync(x => x.ChatRoomId == id) ?? throw Denied();
        if (actor != room.CustomerId && actor != room.MUAId) throw Denied();
        await EnsureSameDomainAsync(room.CustomerId, room.MUAId);
    }

    public async Task<bool> ExternalDeliveryAllowedAsync(AppNotification notification)
    {
        try
        {
            await EnsureNormalUserAsync(notification.UserId);
            if (notification.BookingId.HasValue)
            {
                await EnsureNormalBookingAsync(notification.BookingId.Value);
                var booking = await db.Bookings.AsNoTracking().SingleAsync(x => x.BookingId == notification.BookingId);
                if (notification.UserId != booking.CustomerId && notification.UserId != booking.MUAId) return false;
            }
            if (notification.MessageId.HasValue)
            {
                var message = await db.Messages.AsNoTracking().SingleOrDefaultAsync(x => x.MessageId == notification.MessageId);
                if (message == null) return false;
                await EnsureChatDomainAsync(message.ChatRoomId, message.SenderId);
                await EnsureChatDomainAsync(message.ChatRoomId, notification.UserId);
                await EnsureNormalUserAsync(message.SenderId);
            }
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }
}

public sealed class PlayReviewOperationException() : InvalidOperationException(
    "PLAY_REVIEW_OPERATION_BLOCKED: Demo or unresolved domain cannot use this operation.");
