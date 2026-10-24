using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Service = BeautyBookBackend.Services.BookingService;

namespace BeautyBookBackend.Tests;

public class BookingConfirmationExpiryTests
{
    private static readonly BookingTimeService Utc = new(TimeZoneInfo.Utc);
    private static readonly IConfiguration Config = new ConfigurationBuilder().Build();

    private static Service CreateService(ApplicationDbContext db, BookingTimeService? time = null)
    {
        time ??= Utc;
        var receivables = new MuaReceivableService(db);
        var refunds = new RefundService(db, receivables,
            ReviewTestProxy.Make<IRefundPayoutProvider>((_, _) => throw new Exception("Unexpected external payout")),
            Config, NullLogger<RefundService>.Instance);
        return new(new BookingRepository(db), new MuaRepository(db), new ReviewRepository(db), new UnitOfWork(db),
            new BookingNotificationService(db, time), db,
            ReviewTestProxy.Make<IPayOsService>((method, _) => method.Name == nameof(IPayOsService.IsValidWebhookSignature)
                ? true : throw new Exception("Unexpected payOS call")),
            refunds, new BookingRefundPolicyService(time, NullLogger<BookingRefundPolicyService>.Instance),
            receivables, Config,
            ReviewTestProxy.Make<IMuaEligibilityService>((method, _) => method.Name == nameof(IMuaEligibilityService.EvaluateAsync)
                ? Task.FromResult<MuaEligibilityDto?>(new() { CanReceiveBookings = true }) : throw new Exception("Unexpected eligibility call")),
            new MuaScheduleService(db, time), time, paymentReconciliationCache: new MemoryCache(new MemoryCacheOptions()));
    }

    private static async Task<Booking> Seed(ApplicationDbContext db, DateTime appointment, BookingStatus status = BookingStatus.PendingConfirmation, bool demo = false)
    {
        var now = DateTime.UtcNow;
        var customer = new User { UserId = Guid.NewGuid(), Email = $"{Guid.NewGuid()}@test.local", Role = UserRole.Customer, IsActive = true, IsDemoAccount = demo };
        var mua = new User { UserId = Guid.NewGuid(), Email = $"{Guid.NewGuid()}@test.local", Role = UserRole.MUA, IsActive = true, IsDemoAccount = demo };
        db.Users.AddRange(customer, mua);
        db.MakeupArtistProfiles.Add(new() { MUAId = mua.UserId, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved });
        var endTime = appointment.TimeOfDay.Add(TimeSpan.FromMinutes(30));
        if (endTime > TimeSpan.FromDays(1)) endTime = TimeSpan.FromDays(1);
        var booking = new Booking {
            BookingId = Guid.NewGuid(), CustomerId = customer.UserId, MUAId = mua.UserId, IsDemo = demo,
            Status = status, PaymentStatus = PaymentStatus.DepositHeld, TotalAmount = 50000, DepositAmount = 15000,
            RemainingAmount = 35000, PlatformFeeAmount = 4000, MuaPayoutAmount = 11000, DepositRate = .3m,
            BookingDate = appointment.Date, StartTime = appointment.TimeOfDay, EndTime = endTime,
            DepositPaidAt = now.AddMinutes(-1), PaymentExpiresAt = now.AddMinutes(15), CreatedAt = now.AddMinutes(-2), UpdatedAt = now };
        db.Bookings.Add(booking);
        db.BookingPayments.Add(new() { PaymentId = Guid.NewGuid(), BookingId = booking.BookingId, CustomerId = customer.UserId,
            Provider = demo ? PaymentProvider.Simulated : PaymentProvider.PayOS, ProviderOrderCode = Random.Shared.Next(1, int.MaxValue),
            Amount = 15000, Status = BookingPaymentStatus.Paid, PaidAt = now.AddMinutes(-1),
            CreatedAt = now.AddMinutes(-2), UpdatedAt = now, ExpiresAt = now.AddMinutes(15) });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        return booking;
    }

    [Fact]
    public async Task Overdue_unanswered_booking_cancels_and_refunds_full_capture_once_without_paying_mua()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var booking = await Seed(db, DateTime.UtcNow.AddMinutes(-2)); var service = CreateService(db);
        Assert.Equal(1, await service.ExpireUnconfirmedBookingsAsync());
        Assert.Equal(0, await service.ExpireUnconfirmedBookingsAsync());
        db.ChangeTracker.Clear();
        var saved = await db.Bookings.SingleAsync(); var payment = await db.BookingPayments.SingleAsync();
        var refund = await db.Refunds.SingleAsync();
        Assert.Equal(BookingStatus.Cancelled, saved.Status);
        Assert.Equal(BookingCancellationActor.System, saved.CancellationActor);
        Assert.Null(saved.CancelledBy);
        Assert.Equal(BookingConfirmationPolicy.TimeoutRule, saved.CancellationPolicyRule);
        Assert.Equal(100m, saved.CancellationRefundPercentage);
        Assert.Equal(15000m, saved.CancellationRefundAmount);
        Assert.Equal(PaymentStatus.RefundPending, saved.PaymentStatus);
        Assert.Equal(BookingPaymentStatus.RefundPending, payment.Status);
        Assert.Equal(RefundReasonCode.MuaConfirmationTimeout, refund.ReasonCode);
        Assert.Equal(15000m, refund.Amount);
        Assert.Equal(RefundStatus.AwaitingDestination, refund.Status);
        Assert.Null(refund.CompletedAt);
        Assert.Empty(await db.MuaReceivables.ToListAsync());
        Assert.Equal(2, await db.AppNotifications.CountAsync(x => x.BookingId == booking.BookingId && x.Type == "STATUS_Cancelled"));
    }

    [Fact]
    public async Task Deposit_older_than_24_hours_waits_until_appointment_and_exposes_deadline()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var appointment = DateTime.UtcNow.AddDays(2); var booking = await Seed(db, appointment);
        (await db.Bookings.SingleAsync()).DepositPaidAt = DateTime.UtcNow.AddDays(-2);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); var service = CreateService(db);
        Assert.Equal(0, await service.ExpireUnconfirmedBookingsAsync());
        var result = await service.GetBookingByIdAsync(booking.BookingId, booking.CustomerId);
        Assert.Equal(BookingStatus.PendingConfirmation, result!.Status);
        Assert.Equal(appointment, result.MuaConfirmationDeadline);
        Assert.Empty(await db.Refunds.ToListAsync());
    }

    [Theory]
    [InlineData(BookingStatus.Approved)]
    [InlineData(BookingStatus.InProgress)]
    [InlineData(BookingStatus.WaitingCustomer)]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.Cancelled)]
    public async Task Other_lifecycle_states_are_not_cancelled_by_response_timeout(BookingStatus status)
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        await Seed(db, DateTime.UtcNow.AddHours(-1), status);
        Assert.Equal(0, await CreateService(db).ExpireUnconfirmedBookingsAsync());
        Assert.Equal(status, (await db.Bookings.SingleAsync()).Status);
        Assert.Empty(await db.Refunds.ToListAsync());
    }

    [Fact]
    public async Task Legacy_pending_booking_also_expires_but_review_demo_is_untouched()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var legacy = await Seed(db, DateTime.UtcNow.AddHours(-1), BookingStatus.Pending);
        var demo = await Seed(db, DateTime.UtcNow.AddHours(-1), demo: true);
        Assert.Equal(1, await CreateService(db).ExpireUnconfirmedBookingsAsync());
        Assert.Equal(BookingStatus.Cancelled, (await db.Bookings.FindAsync(legacy.BookingId))!.Status);
        Assert.Equal(BookingStatus.PendingConfirmation, (await db.Bookings.FindAsync(demo.BookingId))!.Status);
        Assert.Single(await db.Refunds.ToListAsync());
    }

    [Theory]
    [InlineData(BookingStatus.Approved)]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Cancelled)]
    public async Task Expired_action_cancels_and_refunds_before_worker_without_accepting_or_customer_penalty(BookingStatus action)
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var booking = await Seed(db, DateTime.UtcNow.AddMinutes(-1));
        var actor = action == BookingStatus.Cancelled ? booking.CustomerId : booking.MUAId;
        var error = await Assert.ThrowsAsync<BookingRuleException>(() => CreateService(db).UpdateBookingStatusAsync(booking.BookingId, actor, action));
        Assert.Equal("BOOKING_CONFIRMATION_EXPIRED", error.Code);
        db.ChangeTracker.Clear();
        Assert.Equal(BookingStatus.Cancelled, (await db.Bookings.SingleAsync()).Status);
        Assert.Equal(RefundReasonCode.MuaConfirmationTimeout, (await db.Refunds.SingleAsync()).ReasonCode);
    }

    [Fact]
    public async Task Mua_can_accept_minutes_before_start_and_it_will_not_be_cancelled_later()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var booking = await Seed(db, DateTime.UtcNow.AddMinutes(2)); var service = CreateService(db);
        var result = await service.UpdateBookingStatusAsync(booking.BookingId, booking.MUAId, BookingStatus.Approved);
        Assert.Equal(BookingStatus.Approved, result!.Status);
        Assert.NotNull(result.ConfirmedAt);
        var saved = await db.Bookings.SingleAsync(); saved.BookingDate = DateTime.UtcNow.Date.AddDays(-1);
        await db.SaveChangesAsync();
        Assert.Equal(0, await service.ExpireUnconfirmedBookingsAsync());
        Assert.Empty(await db.Refunds.ToListAsync());
    }

    [Fact]
    public async Task Mua_can_reject_minutes_before_start_with_full_refund()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var booking = await Seed(db, DateTime.UtcNow.AddMinutes(2));
        var result = await CreateService(db).UpdateBookingStatusAsync(booking.BookingId, booking.MUAId, BookingStatus.Rejected);
        Assert.Equal(BookingStatus.Rejected, result!.Status);
        Assert.Equal(15000m, (await db.Refunds.SingleAsync()).Amount);
        Assert.Equal(RefundReasonCode.MuaRejected, (await db.Refunds.SingleAsync()).ReasonCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_store_app_reads_expire_booking_even_before_worker_runs(bool list)
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var booking = await Seed(db, DateTime.UtcNow.AddMinutes(-1)); var service = CreateService(db);
        var result = list ? (await service.GetBookingsAsync(booking.CustomerId, "customer")).Single()
            : await service.GetBookingByIdAsync(booking.BookingId, booking.CustomerId);
        Assert.Equal(BookingStatus.Cancelled, result!.Status);
        Assert.Equal(PaymentStatus.RefundPending, result.PaymentStatus);
        Assert.NotNull(result.Refund);
        Assert.Equal(0, await service.ExpireUnconfirmedBookingsAsync());
    }

    [Fact]
    public async Task Foreign_read_cannot_trigger_a_refund()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var booking = await Seed(db, DateTime.UtcNow.AddMinutes(-1));
        Assert.Null(await CreateService(db).GetBookingByIdAsync(booking.BookingId, Guid.NewGuid()));
        Assert.Empty(await db.Refunds.ToListAsync());
    }

    [Fact]
    public async Task Payment_confirmed_after_appointment_is_refunded_even_if_old_link_has_not_expired()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var booking = await Seed(db, DateTime.UtcNow.AddMinutes(-1));
        var saved = await db.Bookings.SingleAsync(); saved.Status = BookingStatus.PendingPayment; saved.PaymentStatus = PaymentStatus.Unpaid;
        var payment = await db.BookingPayments.SingleAsync(); payment.Status = BookingPaymentStatus.Pending; payment.PaidAt = null;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.True(await CreateService(db).HandlePayOsWebhookAsync(new() { Success = true, Data = new() {
            OrderCode = payment.ProviderOrderCode, Amount = 15000, Code = "00", Currency = "VND" } }));
        Assert.Equal(BookingStatus.Cancelled, (await db.Bookings.SingleAsync()).Status);
        Assert.Equal(PaymentStatus.RefundPending, (await db.Bookings.SingleAsync()).PaymentStatus);
        Assert.Equal(RefundReasonCode.LatePayment, (await db.Refunds.SingleAsync()).ReasonCode);
    }

    [Fact]
    public async Task Customer_can_create_booking_minutes_before_appointment_and_payment_deadline_is_capped()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var appointment = DateTime.UtcNow.AddMinutes(5);
        if (appointment.TimeOfDay > TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(55)))
            appointment = appointment.Date.AddDays(1).AddMinutes(5);
        var source = await Seed(db, appointment.AddDays(1), BookingStatus.Cancelled);
        var serviceId = Guid.NewGuid();
        db.Services.Add(new BeautyBookBackend.Models.Service { ServiceId = serviceId, MUAId = source.MUAId,
            ServiceName = "Synthetic short service", Price = 50000, DurationMinutes = 1, IsActive = true });
        db.MuaWorkingSchedules.Add(new() { Id = Guid.NewGuid(), MUAId = source.MUAId, DayOfWeek = appointment.DayOfWeek,
            StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromDays(1) });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var result = await CreateService(db).CreateBookingAsync(source.CustomerId, new() {
            MUAId = source.MUAId, BookingDate = appointment.Date, StartTime = appointment.TimeOfDay,
            Address = "Synthetic customer address", IdempotencyKey = Guid.NewGuid().ToString(),
            Services = [new() { ServiceId = serviceId, ParticipantsCount = 1 }] });
        Assert.Equal(BookingStatus.PendingPayment, result!.Status);
        Assert.Equal(appointment, result.PaymentExpiresAt);
        Assert.Null(result.MuaConfirmationDeadline);
    }

    [Fact]
    public async Task Existing_unpaid_booking_cannot_open_payment_after_appointment()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var booking = await Seed(db, DateTime.UtcNow.AddMinutes(-1));
        var saved = await db.Bookings.SingleAsync(); saved.Status = BookingStatus.PendingPayment; saved.PaymentStatus = PaymentStatus.Unpaid;
        (await db.BookingPayments.SingleAsync()).Status = BookingPaymentStatus.Pending;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(db).CreateDepositPaymentAsync(booking.BookingId, booking.CustomerId));
        db.ChangeTracker.Clear();
        Assert.Equal(BookingStatus.Cancelled, (await db.Bookings.SingleAsync()).Status);
        Assert.Empty(await db.Refunds.ToListAsync());
    }

    [Fact]
    public async Task Worker_continues_past_inconsistent_financial_record_without_inventing_a_refund()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var inconsistent = await Seed(db, DateTime.UtcNow.AddMinutes(-2));
        (await db.BookingPayments.SingleAsync()).Status = BookingPaymentStatus.Failed;
        await db.SaveChangesAsync();
        var normal = await Seed(db, DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(1, await CreateService(db).ExpireUnconfirmedBookingsAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(BookingStatus.PendingConfirmation, (await db.Bookings.FindAsync(inconsistent.BookingId))!.Status);
        Assert.Equal(BookingStatus.Cancelled, (await db.Bookings.FindAsync(normal.BookingId))!.Status);
        Assert.Equal(normal.BookingId, (await db.Refunds.SingleAsync()).BookingId);
    }

    [Fact]
    public void Expiration_uses_vietnam_appointment_time_and_includes_exact_start_boundary()
    {
        var time = new BookingTimeService(TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"));
        var booking = new Booking { Status = BookingStatus.PendingConfirmation,
            BookingDate = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc), StartTime = TimeSpan.FromHours(11) };
        var start = new DateTime(2026, 10, 25, 4, 0, 0, DateTimeKind.Utc);
        Assert.False(BookingConfirmationPolicy.IsExpired(booking, time, start.AddTicks(-1)));
        Assert.True(BookingConfirmationPolicy.IsExpired(booking, time, start));
    }

    [PostgreSqlFact]
    public async Task Two_expiration_workers_create_one_refund_and_keep_confirmed_booking()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); Booking expired; Booking accepted;
        await using (var seed = database.CreateContext()) {
            expired = await Seed(seed, DateTime.UtcNow.AddMinutes(-1));
            accepted = await Seed(seed, DateTime.UtcNow.AddMinutes(-1), BookingStatus.Approved);
        }
        await using var a = database.CreateContext(); await using var b = database.CreateContext();
        var counts = await Task.WhenAll(CreateService(a).ExpireUnconfirmedBookingsAsync(), CreateService(b).ExpireUnconfirmedBookingsAsync());
        Assert.Equal(1, counts.Sum());
        await using var check = database.CreateContext();
        Assert.Equal(BookingStatus.Cancelled, (await check.Bookings.FindAsync(expired.BookingId))!.Status);
        Assert.Equal(BookingStatus.Approved, (await check.Bookings.FindAsync(accepted.BookingId))!.Status);
        Assert.Single(await check.Refunds.ToListAsync());
        Assert.Equal(2, await check.AppNotifications.CountAsync(x => x.Type == "STATUS_Cancelled"));
    }

    [PostgreSqlFact]
    public async Task Accept_and_expiration_race_never_revives_an_overdue_booking()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); Booking booking;
        await using (var seed = database.CreateContext()) booking = await Seed(seed, DateTime.UtcNow.AddMinutes(-1));
        await using var a = database.CreateContext(); await using var b = database.CreateContext();
        async Task Accept() {
            try { var result = await CreateService(a).UpdateBookingStatusAsync(booking.BookingId, booking.MUAId, BookingStatus.Approved);
                Assert.True(result == null || result.Status == BookingStatus.Cancelled); }
            catch (BookingRuleException ex) { Assert.Equal("BOOKING_CONFIRMATION_EXPIRED", ex.Code); }
            catch (BookingConcurrencyException) { }
        }
        await Task.WhenAll(Accept(), CreateService(b).ExpireUnconfirmedBookingsAsync());
        await using var check = database.CreateContext();
        Assert.Equal(BookingStatus.Cancelled, (await check.Bookings.SingleAsync()).Status);
        Assert.Single(await check.Refunds.ToListAsync());
        Assert.Empty(await check.MuaReceivables.ToListAsync());
    }
}
