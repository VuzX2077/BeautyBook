using System.Security.Cryptography;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeautyBook.PlayReviewProvisioning;

public sealed class ReviewScenarioSeeder(ApplicationDbContext db, ProvisioningSettings settings, BookingTimeService time)
{
    public static readonly string[] Scenarios = ["customer-confirm", "customer-refund", "mua-accept", "mua-reject", "mua-finish", "mua-earnings", "mua-payout-history"];
    public string Key(string scenario, int generation) => $"playreview:v1:{scenario}:{generation}";
    public Guid BookingId(string scenario, int generation) => settings.Id(Key(scenario, generation));

    public async Task SeedAsync(DateTime now, bool refresh, CancellationToken ct)
    {
        // PostgreSQL timestamptz stores microseconds. Keep in-memory and persisted financial instants identical.
        now = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        foreach (var scenario in Scenarios)
        {
            var managed = await db.Bookings.Where(x => x.IdempotencyKey != null && x.IdempotencyKey.StartsWith($"playreview:v1:{scenario}:"))
                .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
            foreach (var existing in managed)
                if (!TryGeneration(existing.IdempotencyKey!, scenario, out var generation) || existing.BookingId != BookingId(scenario, generation)) throw ProvisioningSettings.Error("SCENARIO_COLLISION");
            var latest = managed.OrderByDescending(x => { TryGeneration(x.IdempotencyKey!, scenario, out var n); return n; }).FirstOrDefault();
            if (latest != null && (!refresh || !NeedsRefresh(latest, scenario, now))) continue;
            var next = managed.Count == 0 ? 0 : managed.Select(x => { TryGeneration(x.IdempotencyKey!, scenario, out var n); return n; }).Max() + 1;
            if (next > 10000) throw ProvisioningSettings.Error("GENERATION_LIMIT");
            var id = BookingId(scenario, next);
            if (await db.Bookings.AnyAsync(x => x.BookingId == id, ct)) throw ProvisioningSettings.Error("SCENARIO_COLLISION");
            var customerSide = scenario.StartsWith("customer-", StringComparison.Ordinal);
            var mua = customerSide ? settings.CounterpartUserId : settings.ReviewUserId;
            var customer = customerSide ? settings.ReviewUserId : settings.CounterpartUserId;
            var status = scenario switch {
                "customer-confirm" => BookingStatus.WaitingCustomer, "customer-refund" => BookingStatus.Cancelled,
                "mua-accept" or "mua-reject" => BookingStatus.PendingConfirmation, "mua-finish" => BookingStatus.InProgress,
                _ => BookingStatus.Completed };
            // Derive the business calendar date with the same configured timezone, not the host timezone.
            var localDate = BusinessDate(now);
            var offset = scenario switch { "mua-accept" => 30, "mua-reject" => 31, "customer-confirm" => -1, "mua-earnings" => -2, "mua-payout-history" => -4, "customer-refund" => 7, _ => -1 };
            var date = localDate.AddDays(offset);
            var start = TimeSpan.FromHours(10);
            // Preserve old appointments: select another free slot rather than shifting their history.
            for (var attempts = 0; attempts < 400; attempts++)
            {
                if (!await db.Bookings.AnyAsync(x => x.MUAId == mua && x.BookingDate == date && x.StartTime < start.Add(TimeSpan.FromHours(1)) && x.EndTime > start, ct)) break;
                start += TimeSpan.FromHours(1);
                if (start >= TimeSpan.FromHours(19)) { start = TimeSpan.FromHours(8); date = date.AddDays(offset < 0 ? -1 : 1); }
                if (attempts == 399) throw ProvisioningSettings.Error("SCENARIO_SLOTS_EXHAUSTED");
            }
            var appointment = time.ToUtc(date, start);
            var financial = BookingFinancialCalculator.Calculate(500000);
            var historical = status is BookingStatus.InProgress or BookingStatus.WaitingCustomer or BookingStatus.Completed;
            var created = historical ? appointment.AddDays(-7) : now.AddHours(-3);
            var paid = created.AddMinutes(5);
            var booking = new Booking { BookingId = id, IsDemo = true, CustomerId = customer, MUAId = mua, IdempotencyKey = Key(scenario, next),
                TotalAmount = 500000, DepositRate = BookingFinancialCalculator.DepositRate, DepositAmount = financial.DepositAmount,
                RemainingAmount = financial.RemainingAmount, PlatformFeeAmount = financial.PlatformFeeAmount, MuaPayoutAmount = financial.MuaDepositPayoutAmount,
                FinancialPolicyVersion = BookingFinancialCalculator.CurrentPolicyVersion, TotalDurationMinutes = 60,
                BookingDate = date, StartTime = start, EndTime = start.Add(TimeSpan.FromHours(1)),
                ServiceAddress = settings.LocationLabel, Address = settings.LocationLabel, ServiceLatitude = (decimal)settings.Latitude, ServiceLongitude = (decimal)settings.Longitude,
                Notes = "Sample booking for app review. No real service or money transfer.", Status = status,
                PaymentStatus = status == BookingStatus.Completed ? PaymentStatus.Released : PaymentStatus.DepositHeld,
                CreatedAt = created, UpdatedAt = now, DepositPaidAt = paid, PaymentExpiresAt = created.AddMinutes(15) };
            var payment = new BookingPayment { PaymentId = settings.Id($"payment:{id:D}"), BookingId = id, CustomerId = customer,
                Provider = PaymentProvider.Simulated, ProviderOrderCode = OrderCode(id), Amount = financial.DepositAmount, Status = BookingPaymentStatus.Paid,
                CreatedAt = created, UpdatedAt = paid, ExpiresAt = created.AddMinutes(15), PaidAt = paid };
            if (await db.BookingPayments.AnyAsync(x => x.ProviderOrderCode == payment.ProviderOrderCode || x.PaymentId == payment.PaymentId, ct)) throw ProvisioningSettings.Error("PAYMENT_COLLISION");
            if (status is BookingStatus.InProgress or BookingStatus.WaitingCustomer or BookingStatus.Completed) {
                booking.ConfirmedAt = paid.AddMinutes(5); booking.StartedAt = appointment;
            }
            if (status is BookingStatus.WaitingCustomer or BookingStatus.Completed) {
                booking.WaitingCustomerAt = appointment.AddHours(1); booking.CustomerConfirmationDeadline = booking.WaitingCustomerAt.Value.AddHours(24);
            }
            if (status == BookingStatus.WaitingCustomer) { booking.WaitingCustomerAt = now.AddMinutes(-10); booking.CustomerConfirmationDeadline = booking.WaitingCustomerAt.Value.AddHours(24); }
            if (status == BookingStatus.Completed) booking.CompletedAt = booking.WaitingCustomerAt!.Value.AddMinutes(15);
            if (status == BookingStatus.Cancelled)
            {
                booking.ConfirmedAt = paid.AddMinutes(5);
                // Calculate while the original state is Approved, exactly as the cancellation service does.
                booking.Status = BookingStatus.Approved;
                var cancelledAt = now.AddHours(-1);
                var decision = new BookingRefundPolicyService(time, NullLogger<BookingRefundPolicyService>.Instance)
                    .Calculate(booking, BookingStatus.Cancelled, BookingCancellationActor.Customer, cancelledAt, payment.Amount);
                if (decision.RefundPercentage != 100 || decision.RefundAmount != payment.Amount) throw ProvisioningSettings.Error("REFUND_POLICY_MISMATCH");
                booking.Status = BookingStatus.Cancelled; booking.PaymentStatus = PaymentStatus.Refunded;
                booking.CancelledAt = cancelledAt; booking.CancelledBy = customer; booking.CancellationActor = BookingCancellationActor.Customer;
                booking.CancellationReason = "Sample cancellation for app review"; booking.CancellationPolicyRule = decision.PolicyRule;
                booking.CancellationRefundPercentage = decision.RefundPercentage; booking.CancellationRefundAmount = decision.RefundAmount; booking.CancellationAppointmentAtUtc = decision.AppointmentAtUtc;
                payment.Status = BookingPaymentStatus.Refunded; payment.RefundRequestedAt = cancelledAt; payment.RefundedAt = cancelledAt; payment.UpdatedAt = cancelledAt;
                db.Refunds.Add(new Refund { RefundId = settings.Id($"refund:{id:D}"), BookingId = id, BookingPaymentId = payment.PaymentId,
                    Amount = decision.RefundAmount, Status = RefundStatus.Completed, ReasonCode = decision.ReasonCode, Reason = decision.PolicyRule,
                    RequestedBy = customer, CreatedAt = cancelledAt, CompletedAt = cancelledAt, UpdatedAt = cancelledAt });
            }
            db.Bookings.Add(booking); db.BookingPayments.Add(payment);
            db.BookingServices.Add(new BeautyBookBackend.Models.BookingService { Id = settings.Id($"booking-service:{id:D}"), BookingId = id, ServiceId = settings.Id($"service:{mua:D}"),
                ServiceName = "Sample Event Makeup", PriceSnapshot = 500000, DurationMinutesSnapshot = 60, ParticipantsCount = 1 });
            if (status == BookingStatus.Completed)
            {
                var row = new MuaReceivable { Id = settings.Id($"receivable:{id:D}"), BookingId = id, MuaId = mua,
                    GrossAmount = financial.DepositAmount, PlatformFeeAmount = financial.PlatformFeeAmount, NetAmount = financial.MuaDepositPayoutAmount,
                    Status = MuaReceivableStatus.Available, CreatedAt = booking.CompletedAt!.Value, AvailableAt = booking.CompletedAt, UpdatedAt = booking.CompletedAt.Value };
                if (scenario == "mua-payout-history")
                {
                    var bank = await db.BankAccounts.SingleAsync(x => x.Id == settings.SampleBankAccountId, ct);
                    var payoutId = settings.Id($"payout:{id:D}"); var paidOut = booking.CompletedAt.Value.AddMinutes(5);
                    row.Status = MuaReceivableStatus.PaidOut; row.PaidOutAt = paidOut; row.UpdatedAt = paidOut;
                    db.Payouts.Add(new Payout { Id = payoutId, MuaId = mua, RequestedBy = mua, BankAccountId = bank.Id,
                        Amount = row.NetAmount, Provider = PayoutProvider.Simulated, Status = PayoutStatus.Paid,
                        BankCodeSnapshot = bank.BankCode, BankBinSnapshot = bank.BankBin, BankNameSnapshot = bank.BankName,
                        AccountNumberSnapshot = bank.AccountNumber, AccountHolderNameSnapshot = bank.AccountHolderName,
                        IdempotencyKey = $"playreview:v1:paid:{id:N}", CreatedAt = paidOut, PaidAt = paidOut, UpdatedAt = paidOut,
                        Items = [new PayoutItem { Id = settings.Id($"payout-item:{id:D}"), MuaReceivableId = row.Id, Amount = row.NetAmount, IsActive = true }] });
                }
                db.MuaReceivables.Add(row);
            }
            await db.SaveChangesAsync(ct);
        }
        foreach (var owner in new[] { settings.ReviewUserId, settings.CounterpartUserId })
        {
            var profile = await db.MakeupArtistProfiles.SingleAsync(x => x.MUAId == owner, ct);
            profile.TotalBookings = await db.Bookings.CountAsync(x => x.IsDemo && x.MUAId == owner && (x.Status == BookingStatus.Completed || x.Status == BookingStatus.AutoCompleted), ct);
        }
        await db.SaveChangesAsync(ct);
    }
    private DateTime BusinessDate(DateTime now)
    {
        // Find the date by comparing local midnight conversions; works across offset/DST changes.
        var date = now.Date;
        while (time.ToUtc(date, TimeSpan.Zero) > now) date = date.AddDays(-1);
        while (time.ToUtc(date.AddDays(1), TimeSpan.Zero) <= now) date = date.AddDays(1);
        return DateTime.SpecifyKind(date, DateTimeKind.Utc);
    }
    private bool NeedsRefresh(Booking booking, string scenario, DateTime now) => scenario switch {
        "mua-accept" or "mua-reject" => booking.Status != BookingStatus.PendingConfirmation || time.ToUtc(booking.BookingDate, booking.StartTime) < now.AddDays(7),
        "customer-confirm" => booking.Status != BookingStatus.WaitingCustomer || booking.CustomerConfirmationDeadline <= now,
        "mua-finish" => booking.Status != BookingStatus.InProgress || booking.CreatedAt < now.AddDays(-14),
        "mua-earnings" => !db.MuaReceivables.Any(x => x.BookingId == booking.BookingId && x.Status == MuaReceivableStatus.Available),
        _ => false };
    public static bool TryGeneration(string key, string scenario, out int value) => int.TryParse(key[$"playreview:v1:{scenario}:".Length..], out value) && value >= 0;
    private static long OrderCode(Guid id) { var value = BitConverter.ToInt64(SHA256.HashData(id.ToByteArray()), 0) & long.MaxValue; return -(value == 0 ? 1 : value); }
}
