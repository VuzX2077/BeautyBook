using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace BeautyBookBackend.Tests;

public sealed class PlayReviewSimulationTests
{
    private sealed class Settings(PlayReviewOptions value) : IOptionsMonitor<PlayReviewOptions>
    {
        public PlayReviewOptions CurrentValue => value;
        public PlayReviewOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<PlayReviewOptions, string?> listener) => null;
    }
    private sealed class Fixture
    {
        public PlayReviewOptions Options { get; } = new() { SimulationEnabled = true, ReviewUserId = Guid.NewGuid(), CounterpartUserId = Guid.NewGuid(), SampleBankAccountId = Guid.NewGuid() };
        public Guid BookingId { get; } = Guid.NewGuid();
        public Guid ServiceId { get; } = Guid.NewGuid();
        public int PayOsCalls;
        public int RefundProviderCalls;
        public PlayReviewPolicy Policy(ApplicationDbContext db) => new(db, new Settings(Options));
        public async Task Seed(ApplicationDbContext db, bool reviewerMua = false, BookingStatus status = BookingStatus.PendingPayment)
        {
            foreach (var id in new[] { Options.ReviewUserId, Options.CounterpartUserId })
            {
                db.Users.Add(new User { UserId = id, FullName = "Test profile", Email = $"{id:N}@example.com", AvatarUrl = "https://example.com/test.png", Role = UserRole.MUA, IsDemoAccount = true, IsActive = true, CreatedAt = DateTime.UtcNow });
                db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId = id, Status = MuaStatus.Draft, VerificationStatus = MuaVerificationStatus.Draft, City = "Test city", Specialization = "Test specialty" });
                foreach (var day in Enum.GetValues<DayOfWeek>()) db.MuaWorkingSchedules.Add(new MuaWorkingSchedule { Id = Guid.NewGuid(), MUAId = id, DayOfWeek = day, StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(22), IsActive = true });
                db.Portfolios.Add(new Portfolio { PortfolioId = Guid.NewGuid(), MUAId = id, ImageUrls = ["https://example.com/a", "https://example.com/b", "https://example.com/c"] });
            }
            var mua = reviewerMua ? Options.ReviewUserId : Options.CounterpartUserId;
            db.Services.Add(new BeautyBookBackend.Models.Service { ServiceId = ServiceId, MUAId = mua, ServiceName = "Test service", Price = 100000, DurationMinutes = 60, IsActive = true });
            db.BankAccounts.Add(new BankAccount { Id = Options.SampleBankAccountId, UserId = Options.ReviewUserId, BankCode = "TEST", BankName = "Test sample", AccountNumber = "DEMO", AccountHolderName = "TEST SAMPLE", CanonicalBankKey = "TEST", NormalizedAccountNumber = "DEMO", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            var now = DateTime.UtcNow;
            db.Bookings.Add(new Booking { BookingId = BookingId, CustomerId = reviewerMua ? Options.CounterpartUserId : Options.ReviewUserId, MUAId = mua, IsDemo = true,
                Status = status, PaymentStatus = status == BookingStatus.PendingPayment ? PaymentStatus.Unpaid : status == BookingStatus.Completed ? PaymentStatus.Released : PaymentStatus.DepositHeld,
                BookingDate = now.Date.AddDays(3), StartTime = TimeSpan.FromHours(10), EndTime = TimeSpan.FromHours(11), TotalAmount = 100000,
                DepositAmount = 30000, PlatformFeeAmount = 8000, MuaPayoutAmount = 22000, RemainingAmount = 70000, TotalDurationMinutes = 60,
                PaymentExpiresAt = now.AddMinutes(15), DepositPaidAt = status == BookingStatus.PendingPayment ? null : now,
                CompletedAt = status == BookingStatus.Completed ? now : null, CreatedAt = now, UpdatedAt = now });
            if (status != BookingStatus.PendingPayment)
                db.BookingPayments.Add(new BookingPayment { PaymentId = Guid.NewGuid(), BookingId = BookingId, CustomerId = reviewerMua ? Options.CounterpartUserId : Options.ReviewUserId,
                    Provider = PaymentProvider.Simulated, ProviderOrderCode = -Math.Abs(now.Ticks), Amount = 30000, Status = BookingPaymentStatus.Paid,
                    PaidAt = now, CreatedAt = now, UpdatedAt = now, ExpiresAt = now.AddMinutes(15) });
            if (status == BookingStatus.Completed)
                db.MuaReceivables.Add(new MuaReceivable { Id = Guid.NewGuid(), BookingId = BookingId, MuaId = mua, GrossAmount = 30000, PlatformFeeAmount = 8000, NetAmount = 22000,
                    Status = MuaReceivableStatus.Available, AvailableAt = now, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }
        public BeautyBookBackend.Services.BookingService Bookings(ApplicationDbContext db, IUnitOfWork? unitOfWork = null, IPayOsService? payOs = null)
        {
            var policy = Policy(db); var time = new BookingTimeService(TimeZoneInfo.Utc);
            var receivables = new MuaReceivableService(db, policy);
            var config = new ConfigurationBuilder().Build();
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Options.ReviewUserId.ToString())], "test")) } };
            return new(new BookingRepository(db), new MuaRepository(db), new ReviewRepository(db), unitOfWork ?? new UnitOfWork(db), new BookingNotificationService(db, time), db,
                payOs ?? ReviewTestProxy.Make<IPayOsService>((_, _) => { PayOsCalls++; throw new Exception("Unexpected PayOS"); }),
                new RefundService(db, receivables, ReviewTestProxy.Make<IRefundPayoutProvider>((_, _) => { RefundProviderCalls++; throw new Exception("Unexpected refund provider"); }), config, NullLogger<RefundService>.Instance),
                new BookingRefundPolicyService(time, NullLogger<BookingRefundPolicyService>.Instance), receivables, config,
                new MuaEligibilityService(db, new MuaScheduleService(db, time), new VerificationMediaService(db, ReviewTestProxy.Make<IVerificationStorage>((_, _) => throw new NotSupportedException()))),
                new MuaScheduleService(db, time), time, accessor, policy);
        }
        public PayoutService Payouts(ApplicationDbContext db) => new(db, ReviewTestProxy.Make<IMuaEligibilityService>((_, _) => throw new Exception("Production eligibility must not run")), Policy(db));
        public CreatePayoutRequest PayoutRequest => new() { BankAccountId = Options.SampleBankAccountId, IdempotencyKey = "test-request" };
    }

    [Fact]
    public async Task Payment_success_accept_and_cancel_are_atomic_idempotent_and_have_zero_provider_calls()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var f = new Fixture(); await f.Seed(db);
        var service = f.Bookings(db); var payment = await service.CreateDepositPaymentAsync(f.BookingId, f.Options.ReviewUserId);
        Assert.Equal(PaymentProvider.Simulated, payment!.Provider); Assert.Null(payment.CheckoutUrl); Assert.Null(payment.QrCode);
        Assert.Equal(payment.PaymentId, (await service.CreateDepositPaymentAsync(f.BookingId, f.Options.ReviewUserId))!.PaymentId);
        await service.DemoPaymentSucceedAsync(f.BookingId, f.Options.ReviewUserId); await service.DemoPaymentSucceedAsync(f.BookingId, f.Options.ReviewUserId);
        await service.DemoCounterpartAcceptAsync(f.BookingId, f.Options.ReviewUserId);
        var confirmed = (await db.Bookings.SingleAsync()).ConfirmedAt;
        await service.DemoCounterpartAcceptAsync(f.BookingId, f.Options.ReviewUserId); Assert.Equal(confirmed, (await db.Bookings.SingleAsync()).ConfirmedAt);
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => service.UpdateBookingStatusAsync(f.BookingId, f.Options.ReviewUserId, BookingStatus.Approved));
        await service.UpdateBookingStatusAsync(f.BookingId, f.Options.ReviewUserId, BookingStatus.Cancelled);
        Assert.Equal(RefundStatus.Completed, (await db.Refunds.SingleAsync()).Status); Assert.Equal(PaymentStatus.Refunded, (await db.Bookings.SingleAsync()).PaymentStatus);
        Assert.All(await db.AppNotifications.ToListAsync(), x => Assert.Contains(x.Status, new[] { "Skipped", "Cancelled" }));
        Assert.Equal(0, f.PayOsCalls); Assert.Equal(0, f.RefundProviderCalls);
    }

    [Theory]
    [InlineData("off")] [InlineData("normal")] [InlineData("caller")] [InlineData("pair")] [InlineData("amount")] [InlineData("provider")] [InlineData("expired")] [InlineData("missing")]
    public async Task Payment_authority_or_state_failure_does_not_settle(string attack)
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var f = new Fixture(); await f.Seed(db); var service = f.Bookings(db);
        await service.CreateDepositPaymentAsync(f.BookingId, f.Options.ReviewUserId); var payment = await db.BookingPayments.SingleAsync(); var caller = f.Options.ReviewUserId;
        switch (attack) {
            case "off": f.Options.SimulationEnabled = false; break;
            case "normal": (await db.Users.FindAsync(caller))!.IsDemoAccount = false; break;
            case "caller": caller = f.Options.CounterpartUserId; break;
            case "pair": f.Options.CounterpartUserId = Guid.NewGuid(); break;
            case "amount": payment.Amount++; break;
            case "provider": payment.Provider = PaymentProvider.PayOS; break;
            case "expired": payment.ExpiresAt = DateTime.UtcNow.AddMinutes(-1); break;
            case "missing": f.Options.ReviewUserId = Guid.NewGuid(); break;
        }
        await db.SaveChangesAsync(); await Assert.ThrowsAsync<PlayReviewOperationException>(() => service.DemoPaymentSucceedAsync(f.BookingId, caller));
        db.ChangeTracker.Clear(); Assert.Equal(BookingPaymentStatus.Pending, (await db.BookingPayments.SingleAsync()).Status); Assert.Equal(0, f.PayOsCalls);
    }

    [Theory] [InlineData(9, 15000)] [InlineData(3, 0)]
    public async Task Cancellation_partial_and_zero_use_real_policy_without_production_refund(int hours, int expected)
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var f = new Fixture(); await f.Seed(db, status: BookingStatus.Approved);
        var b = await db.Bookings.SingleAsync(); var appointment = DateTime.UtcNow.AddHours(hours); b.BookingDate = appointment.Date; b.StartTime = appointment.TimeOfDay; b.ConfirmedAt = DateTime.UtcNow.AddHours(-1); await db.SaveChangesAsync();
        await f.Bookings(db).UpdateBookingStatusAsync(f.BookingId, f.Options.ReviewUserId, BookingStatus.Cancelled);
        Assert.Equal(expected, (await db.Refunds.Select(x => (decimal?)x.Amount).SingleOrDefaultAsync()) ?? 0);
        Assert.Equal(expected == 0 ? PaymentStatus.Forfeited : PaymentStatus.PartiallyRefunded, b.PaymentStatus);
        Assert.Equal(0, f.RefundProviderCalls);
    }

    [Fact]
    public async Task Reject_repeat_creates_one_refund_and_completion_reuses_demo_receivable_core()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var f = new Fixture(); await f.Seed(db, status: BookingStatus.PendingConfirmation);
        await f.Bookings(db).DemoCounterpartRejectAsync(f.BookingId, f.Options.ReviewUserId); await f.Bookings(db).DemoCounterpartRejectAsync(f.BookingId, f.Options.ReviewUserId);
        Assert.Single(await db.Refunds.ToListAsync()); Assert.Equal(f.Options.ReviewUserId, (await db.Bookings.SingleAsync()).CancelledBy);
        await using var other = await PlayReviewTestStore.CreateAsync(); var second = new Fixture(); await second.Seed(other.Db, status: BookingStatus.WaitingCustomer);
        await second.Bookings(other.Db).UpdateBookingStatusAsync(second.BookingId, second.Options.ReviewUserId, BookingStatus.Completed);
        Assert.Equal(22000, (await other.Db.MuaReceivables.SingleAsync()).NetAmount); Assert.Equal(PaymentStatus.Released, (await other.Db.Bookings.SingleAsync()).PaymentStatus);
        Assert.Equal(0, await new MuaReceivableService(other.Db).ReconcileStatesAsync());
    }

    [Fact]
    public async Task Simulated_payout_paid_once_never_enters_admin_queue_or_actions()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var f = new Fixture(); await f.Seed(db, true, BookingStatus.Completed);
        var service = f.Payouts(db); var result = await service.CreateAsync(f.Options.ReviewUserId, f.PayoutRequest);
        Assert.Equal(PayoutProvider.Simulated, result.Provider); Assert.Equal(PayoutStatus.Paid, result.Status);
        Assert.Equal(result.Id, (await service.CreateAsync(f.Options.ReviewUserId, f.PayoutRequest)).Id);
        Assert.Equal(MuaReceivableStatus.PaidOut, (await db.MuaReceivables.SingleAsync()).Status); Assert.Empty(await service.GetPendingAdminAsync());
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => service.GetAdminByIdAsync(result.Id));
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => service.StartProcessingAsync(result.Id, Guid.NewGuid(), "test"));
        Assert.False(BankAccountEligibility.IsUsable(await db.BankAccounts.SingleAsync(), DateTime.UtcNow));
    }

    [Theory] [InlineData("off")] [InlineData("bank")] [InlineData("owner")] [InlineData("normal")] [InlineData("provider")]
    public async Task Payout_invalid_authority_rolls_back(string attack)
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var f = new Fixture(); await f.Seed(db, true, BookingStatus.Completed);
        switch (attack) { case "off": f.Options.SimulationEnabled = false; break; case "bank": f.Options.SampleBankAccountId = Guid.Empty; break;
            case "owner": (await db.MuaReceivables.SingleAsync()).MuaId = f.Options.CounterpartUserId; break;
            case "normal": (await db.Users.FindAsync(f.Options.CounterpartUserId))!.IsDemoAccount = false; break;
            case "provider": (await db.BookingPayments.SingleAsync()).Provider = PaymentProvider.PayOS; break; }
        await db.SaveChangesAsync(); await Assert.ThrowsAsync<PlayReviewOperationException>(() => f.Payouts(db).CreateAsync(f.Options.ReviewUserId, f.PayoutRequest));
        Assert.Empty(await db.Payouts.ToListAsync()); Assert.Equal(MuaReceivableStatus.Available, (await db.MuaReceivables.SingleAsync()).Status);
    }

    [Fact]
    public async Task Mixed_receivables_reject_entire_payout_without_claiming_valid_demo_funds()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var f = new Fixture(); await f.Seed(db, true, BookingStatus.Completed);
        var normalCustomer = Guid.NewGuid(); var otherBooking = Guid.NewGuid();
        db.Users.Add(new User { UserId = normalCustomer, IsActive = true, IsDemoAccount = false });
        db.Bookings.Add(new Booking { BookingId = otherBooking, CustomerId = normalCustomer, MUAId = f.Options.ReviewUserId,
            Status = BookingStatus.Completed, PaymentStatus = PaymentStatus.Released, CompletedAt = DateTime.UtcNow, DepositAmount = 30000, MuaPayoutAmount = 22000 });
        db.MuaReceivables.Add(new MuaReceivable { Id = Guid.NewGuid(), BookingId = otherBooking, MuaId = f.Options.ReviewUserId,
            Status = MuaReceivableStatus.Available, GrossAmount = 30000, PlatformFeeAmount = 8000, NetAmount = 22000, AvailableAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => f.Payouts(db).CreateAsync(f.Options.ReviewUserId, f.PayoutRequest));
        Assert.Empty(await db.Payouts.ToListAsync()); Assert.Empty(await db.PayoutItems.ToListAsync());
        Assert.All(await db.MuaReceivables.ToListAsync(), x => Assert.Equal(MuaReceivableStatus.Available, x.Status));
    }

    [PostgreSqlFact]
    public async Task Draft_capability_uses_real_schedule_and_ownership_without_publication_or_verification()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); var f = new Fixture(); await f.Seed(db);
        var time = new BookingTimeService(TimeZoneInfo.Utc); var schedule = new MuaScheduleService(db, time);
        var eligibility = new MuaEligibilityService(db, schedule, new VerificationMediaService(db, ReviewTestProxy.Make<IVerificationStorage>((_, _) => throw new NotSupportedException())));
        var mua = new MuaService(new MuaRepository(db), new UserRepository(db), new UnitOfWork(db), db, eligibility, playReview: f.Policy(db));
        Assert.NotNull(await mua.GetMuaByIdAsync(f.Options.CounterpartUserId, f.Options.ReviewUserId));
        Assert.Null(await mua.GetMuaByIdAsync(f.Options.CounterpartUserId));
        var actual = await eligibility.EvaluateAsync(f.Options.CounterpartUserId);
        Assert.False(actual!.CanReceiveBookings); Assert.False(actual.CanPublishProfile); Assert.False(actual.CanWithdraw);
        var service = f.Bookings(db); var date = DateTime.UtcNow.Date.AddDays(5);
        Assert.NotEmpty(await service.GetAvailableSlotsAsync(f.Options.CounterpartUserId, date, 60));
        var request = new BookingCreateDto { MUAId = f.Options.CounterpartUserId, IdempotencyKey = "interactive", BookingDate = date, StartTime = TimeSpan.FromHours(10), ServiceAddress = "Test location", Services = [new() { ServiceId = f.ServiceId }] };
        Assert.NotNull(await service.CreateBookingAsync(f.Options.ReviewUserId, request));
        request.IdempotencyKey = "overlap";
        await Assert.ThrowsAsync<BookingRuleException>(() => service.CreateBookingAsync(f.Options.ReviewUserId, request));
        Assert.All(await db.MakeupArtistProfiles.ToListAsync(), x => { Assert.Equal(MuaStatus.Draft, x.Status); Assert.Equal(MuaVerificationStatus.Draft, x.VerificationStatus); });
        f.Options.SimulationEnabled = false;
        Assert.Null(await mua.GetMuaByIdAsync(f.Options.CounterpartUserId, f.Options.ReviewUserId));
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => service.GetAvailableSlotsAsync(f.Options.CounterpartUserId, date, 60));
    }

    [Theory] [InlineData("normalCaller")] [InlineData("otherDemo")] [InlineData("outsidePair")] [InlineData("wrongParty")] [InlineData("normalBooking")]
    public async Task Counterpart_actions_never_accept_unrelated_or_normal_resources(string attack)
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var f = new Fixture(); await f.Seed(db, status: BookingStatus.PendingConfirmation);
        var caller = f.Options.ReviewUserId;
        switch (attack) {
            case "normalCaller": (await db.Users.FindAsync(caller))!.IsDemoAccount = false; break;
            case "otherDemo": caller = Guid.NewGuid(); db.Users.Add(new User { UserId = caller, IsDemoAccount = true, IsActive = true }); break;
            case "outsidePair": f.Options.CounterpartUserId = Guid.NewGuid(); break;
            case "wrongParty": caller = f.Options.CounterpartUserId; break;
            case "normalBooking": await db.Database.ExecuteSqlRawAsync("UPDATE \"Bookings\" SET \"IsDemo\"=0"); db.ChangeTracker.Clear(); break;
        }
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => f.Bookings(db).DemoCounterpartAcceptAsync(f.BookingId, caller));
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => f.Bookings(db).DemoCounterpartRejectAsync(f.BookingId, caller));
        Assert.Equal(BookingStatus.PendingConfirmation, (await db.Bookings.SingleAsync()).Status); Assert.Empty(await db.Refunds.ToListAsync());
    }

    [Fact]
    public async Task Mua_mode_uses_existing_participant_transitions_and_blocks_customer_side_counterpart_actions()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var f = new Fixture(); await f.Seed(db, true, BookingStatus.PendingConfirmation);
        var service = f.Bookings(db);
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => service.DemoCounterpartAcceptAsync(f.BookingId, f.Options.ReviewUserId));
        await service.UpdateBookingStatusAsync(f.BookingId, f.Options.ReviewUserId, BookingStatus.Approved);
        await service.UpdateBookingStatusAsync(f.BookingId, f.Options.ReviewUserId, BookingStatus.InProgress);
        await service.UpdateBookingStatusAsync(f.BookingId, f.Options.ReviewUserId, BookingStatus.WaitingCustomer);
        var b = await db.Bookings.SingleAsync(); Assert.NotNull(b.ConfirmedAt); Assert.NotNull(b.StartedAt); Assert.NotNull(b.CustomerConfirmationDeadline);
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => service.UpdateBookingStatusAsync(f.BookingId, f.Options.ReviewUserId, BookingStatus.Completed));
    }

    [Fact]
    public async Task Injected_save_failure_rolls_back_payment_and_notifications_without_provider_fallback()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var f = new Fixture(); await f.Seed(db);
        await f.Bookings(db).CreateDepositPaymentAsync(f.BookingId, f.Options.ReviewUserId);
        var failing = ReviewTestProxy.Make<IUnitOfWork>((_, _) => throw new InvalidOperationException("Injected save failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Bookings(db, failing).DemoPaymentSucceedAsync(f.BookingId, f.Options.ReviewUserId));
        db.ChangeTracker.Clear(); Assert.Equal(BookingStatus.PendingPayment, (await db.Bookings.SingleAsync()).Status);
        Assert.Equal(BookingPaymentStatus.Pending, (await db.BookingPayments.SingleAsync()).Status); Assert.Empty(await db.AppNotifications.ToListAsync()); Assert.Equal(0, f.PayOsCalls);
    }

    [Fact]
    public async Task Pending_actions_and_payout_capability_are_server_computed_and_revoked_by_switch()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var f = new Fixture(); await f.Seed(db);
        var service = f.Bookings(db); await service.CreateDepositPaymentAsync(f.BookingId, f.Options.ReviewUserId);
        Assert.Equal(new[] { "paymentSucceed" }, (await service.GetBookingByIdAsync(f.BookingId, f.Options.ReviewUserId))!.AvailableDemoActions);
        await service.DemoPaymentSucceedAsync(f.BookingId, f.Options.ReviewUserId);
        Assert.Equal(new[] { "counterpartAccept", "counterpartReject" }, (await service.GetBookingByIdAsync(f.BookingId, f.Options.ReviewUserId))!.AvailableDemoActions);
        f.Options.SimulationEnabled = false;
        Assert.Empty((await service.GetBookingByIdAsync(f.BookingId, f.Options.ReviewUserId))!.AvailableDemoActions);
        Assert.Null(await f.Policy(db).GetCounterpartEntryAsync(f.Options.ReviewUserId));
        Assert.Null(await f.Policy(db).GetSampleBankCapabilityAsync(f.Options.ReviewUserId));
    }

    private sealed class CountingHttp : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("External delivery is forbidden in this test"); }
    }

    [Fact]
    public async Task Simulated_flow_has_zero_PayOS_Brevo_Expo_HTTP_and_rejects_production_webhook()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var f = new Fixture(); await f.Seed(db);
        var service = f.Bookings(db); await service.CreateDepositPaymentAsync(f.BookingId, f.Options.ReviewUserId);
        await service.DemoPaymentSucceedAsync(f.BookingId, f.Options.ReviewUserId);
        var payment = await db.BookingPayments.SingleAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Email:BrevoApiKey"] = "test", ["Email:FromEmail"] = "sender@example.com" }).Build();
        var payHttp = new CountingHttp(); var brevoHttp = new CountingHttp(); var expoHttp = new CountingHttp();
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => new PayOsService(new HttpClient(payHttp), config, db).CreatePaymentLinkAsync(new PayOsCreatePaymentRequest { OrderCode = payment.ProviderOrderCode, Amount = 30000 }));
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => new BrevoEmailSender(new HttpClient(brevoHttp), config, NullLogger<BrevoEmailSender>.Instance, db).SendOtpAsync($"{f.Options.ReviewUserId:N}@example.com", "test-only", "RESET_PASSWORD"));
        var verify = ReviewTestProxy.Make<IPayOsService>((method, _) => method.Name == nameof(IPayOsService.IsValidWebhookSignature) ? true : throw new InvalidOperationException("Unexpected PayOS"));
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => f.Bookings(db, payOs: verify).HandlePayOsWebhookAsync(new PayOsWebhookDto { Data = new PayOsWebhookDataDto { OrderCode = payment.ProviderOrderCode, Amount = 30000 } }));
        await service.DemoCounterpartRejectAsync(f.BookingId, f.Options.ReviewUserId);
        db.AppNotifications.Add(new AppNotification { Id = Guid.NewGuid(), BookingId = f.BookingId, UserId = f.Options.ReviewUserId, Type = "STALE", Status = "Pending", ScheduledAt = DateTime.UtcNow });
        db.DevicePushTokens.Add(new DevicePushToken { Id = Guid.NewGuid(), UserId = f.Options.ReviewUserId, ExpoPushToken = "ExponentPushToken[test]", IsActive = true, Platform = "android" });
        await db.SaveChangesAsync();
        using var provider = new ServiceCollection().AddSingleton(db).AddSingleton(new BookingTimeService(TimeZoneInfo.Utc)).BuildServiceProvider();
        var factory = ReviewTestProxy.Make<IHttpClientFactory>((_, _) => new HttpClient(expoHttp));
        var worker = new PushNotificationWorker(provider.GetRequiredService<IServiceScopeFactory>(), factory, NullLogger<PushNotificationWorker>.Instance);
        await (Task)typeof(PushNotificationWorker).GetMethod("ProcessAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(worker, [CancellationToken.None])!;
        Assert.Equal(0, payHttp.Calls); Assert.Equal(0, brevoHttp.Calls); Assert.Equal(0, expoHttp.Calls); Assert.Equal(0, f.RefundProviderCalls);
        var refund = await db.Refunds.SingleAsync(); refund.Status = RefundStatus.Pending; await db.SaveChangesAsync();
        var refunds = new RefundService(db, new MuaReceivableService(db), ReviewTestProxy.Make<IRefundPayoutProvider>((_, _) => { f.RefundProviderCalls++; throw new InvalidOperationException(); }), config, NullLogger<RefundService>.Instance);
        Assert.Empty(await refunds.GetAdminQueueAsync()); Assert.Equal(0, await refunds.MovePendingToManualActionRequiredAsync()); Assert.Equal(0, f.RefundProviderCalls);
    }

    [PostgreSqlFact]
    public async Task PostgreSQL_concurrent_payment_accept_reject_cancel_and_payout()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var f = new Fixture();
        await using (var db = database.CreateContext()) { await f.Seed(db); await f.Bookings(db).CreateDepositPaymentAsync(f.BookingId, f.Options.ReviewUserId); }
        async Task<bool> Run(Func<BeautyBookBackend.Services.BookingService, Task> action) {
            await using var db = database.CreateContext(); try { await action(f.Bookings(db)); return true; }
            catch (BookingConcurrencyException) { return false; } catch (PlayReviewOperationException) { return false; }
        }
        Assert.All(await Task.WhenAll(Run(async s => await s.DemoPaymentSucceedAsync(f.BookingId, f.Options.ReviewUserId)), Run(async s => await s.DemoPaymentSucceedAsync(f.BookingId, f.Options.ReviewUserId))), Assert.True);
        var results = await Task.WhenAll(Run(async s => await s.DemoCounterpartAcceptAsync(f.BookingId, f.Options.ReviewUserId)), Run(async s => await s.DemoCounterpartRejectAsync(f.BookingId, f.Options.ReviewUserId)));
        Assert.Single(results, x => x);
        await using (var db = database.CreateContext()) { var b = await db.Bookings.SingleAsync(); Assert.Contains(b.Status, new[] { BookingStatus.Approved, BookingStatus.Rejected }); Assert.True(await db.Refunds.CountAsync() <= 1); }
        var cancel = new Fixture(); await using (var db = database.CreateContext()) await cancel.Seed(db, status: BookingStatus.Approved);
        async Task Cancel() { await using var db = database.CreateContext(); try { await cancel.Bookings(db).UpdateBookingStatusAsync(cancel.BookingId, cancel.Options.ReviewUserId, BookingStatus.Cancelled); } catch (BookingConcurrencyException) { } }
        await Task.WhenAll(Cancel(), Cancel());
        await using (var db = database.CreateContext()) Assert.Equal(1, await db.Refunds.CountAsync(x => x.BookingId == cancel.BookingId));
        var payout = new Fixture(); await using (var db = database.CreateContext()) await payout.Seed(db, true, BookingStatus.Completed);
        async Task<bool> Withdraw(string key) { await using var db = database.CreateContext(); try { var request = payout.PayoutRequest; request.IdempotencyKey = key; await payout.Payouts(db).CreateAsync(payout.Options.ReviewUserId, request); return true; } catch (PlayReviewOperationException) { return false; } }
        Assert.Single(await Task.WhenAll(Withdraw("a"), Withdraw("b")), x => x);
        await using (var db = database.CreateContext()) Assert.Equal(1, await db.Payouts.CountAsync(x => x.MuaId == payout.Options.ReviewUserId));
        Assert.Equal(0, f.PayOsCalls); Assert.Equal(0, f.RefundProviderCalls);
    }
}
