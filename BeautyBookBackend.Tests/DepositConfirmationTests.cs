using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeautyBookBackend.Tests;

public class DepositConfirmationTests
{
    private const string Key = "local-test-checksum";
    private static IConfiguration Config => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["PayOS:ChecksumKey"] = Key, ["PayOS:ClientId"] = "test", ["PayOS:ApiKey"] = "test" }).Build();
    private static string Sign(string canonical) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Key), Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

    [Theory]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("\"null\"")]
    [InlineData("\"undefined\"")]
    public void Present_optional_fields_are_signed_as_empty_and_tampering_is_rejected(string value)
    {
        var data = new PayOsWebhookVerificationData {
            OrderCode = 123, Amount = 15000, AccountNumber = "test", Code = "00", Currency = "VND",
            Desc = "Thành công", Description = "COC BBOOK 123", PaymentLinkId = "link", Reference = "ref", TransactionDateTime = "2026-10-24 14:12:00",
            ExtraData = new() { ["counterAccountName"] = JsonDocument.Parse(value).RootElement.Clone() },
            Signature = Sign("accountNumber=test&amount=15000&code=00&counterAccountName=&currency=VND&desc=Thành công&description=COC BBOOK 123&orderCode=123&paymentLinkId=link&reference=ref&transactionDateTime=2026-10-24 14:12:00")
        };
        var service = new PayOsService(new HttpClient(), Config);
        Assert.True(service.IsValidWebhookSignature(data));
        data.Amount++;
        Assert.False(service.IsValidWebhookSignature(data));
    }

    [Fact]
    public void Provider_response_array_keeps_order_and_sorts_transaction_keys()
    {
        var data = JsonDocument.Parse("""{"transactions":[{"reference":"abc","amount":15000}],"status":"PAID","amountPaid":15000,"optional":null}""").RootElement;
        var canonical = "amountPaid=15000&optional=&status=PAID&transactions=[{\"amount\":15000,\"reference\":\"abc\"}]";
        Assert.Equal(canonical, PayOsSignature.Canonicalize(data));
        Assert.True(PayOsSignature.Verify(data, Sign(canonical), Key));
        Assert.False(PayOsSignature.Verify(data, "not-hex", Key));
    }

    private static async Task<(Booking Booking, BookingPayment Payment)> Seed(ApplicationDbContext db, bool demo = false)
    {
        var now = DateTime.UtcNow;
        var customer = new User { UserId = Guid.NewGuid(), Email = $"{Guid.NewGuid()}@test.local", Role = UserRole.Customer, IsActive = true, IsDemoAccount = demo };
        var mua = new User { UserId = Guid.NewGuid(), Email = $"{Guid.NewGuid()}@test.local", Role = UserRole.MUA, IsActive = true, IsDemoAccount = demo };
        db.Users.AddRange(customer, mua);
        db.MakeupArtistProfiles.Add(new() { MUAId = mua.UserId, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved });
        var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = customer.UserId, MUAId = mua.UserId, IsDemo = demo,
            Status = BookingStatus.PendingPayment, PaymentStatus = PaymentStatus.Unpaid, TotalAmount = 50000, DepositAmount = 15000,
            RemainingAmount = 35000, PlatformFeeAmount = 4000, MuaPayoutAmount = 11000, DepositRate = .3m,
            BookingDate = now.Date.AddDays(2), StartTime = TimeSpan.FromHours(10), EndTime = TimeSpan.FromHours(11),
            PaymentExpiresAt = now.AddMinutes(15), CreatedAt = now, UpdatedAt = now };
        var payment = new BookingPayment { PaymentId = Guid.NewGuid(), BookingId = booking.BookingId, CustomerId = customer.UserId,
            Provider = demo ? PaymentProvider.Simulated : PaymentProvider.PayOS, ProviderOrderCode = Random.Shared.Next(1, int.MaxValue),
            ProviderPaymentLinkId = Guid.NewGuid().ToString("N"), Amount = 15000, Status = BookingPaymentStatus.Pending,
            CreatedAt = now, UpdatedAt = now, ExpiresAt = now.AddMinutes(15) };
        db.Bookings.Add(booking); db.BookingPayments.Add(payment); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        return (booking, payment);
    }

    private static PayOsPaymentLinkResult Paid(BookingPayment payment, decimal? amountPaid = null) => new() {
        OrderCode = payment.ProviderOrderCode, PaymentLinkId = payment.ProviderPaymentLinkId!, Status = "PAID",
        Amount = payment.Amount, AmountPaid = amountPaid ?? payment.Amount, AmountRemaining = 0,
        Reference = "test-ref", TransactionDateTime = DateTime.UtcNow.ToString("O") };

    private static BeautyBookBackend.Services.BookingService Service(ApplicationDbContext db, IPayOsService provider)
    {
        var time = new BookingTimeService(TimeZoneInfo.Utc);
        var receivables = new MuaReceivableService(db);
        var refunds = new RefundService(db, receivables, ReviewTestProxy.Make<IRefundPayoutProvider>((_, _) => throw new Exception("Unexpected payout")), Config, NullLogger<RefundService>.Instance);
        return new(new BookingRepository(db), new MuaRepository(db), new ReviewRepository(db), new UnitOfWork(db),
            new BookingNotificationService(db, time), db, provider, refunds,
            new BookingRefundPolicyService(time, NullLogger<BookingRefundPolicyService>.Instance), receivables, Config,
            ReviewTestProxy.Make<IMuaEligibilityService>((_, _) => throw new Exception("Unexpected eligibility")),
            new MuaScheduleService(db, time), time, paymentReconciliationCache: new MemoryCache(new MemoryCacheOptions()));
    }

    [Fact]
    public async Task Lost_webhook_is_recovered_once_and_duplicate_webhook_does_not_repeat_notifications()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var (booking, payment) = await Seed(db); var calls = 0;
        var provider = ReviewTestProxy.Make<IPayOsService>((method, _) => method.Name == nameof(IPayOsService.IsValidWebhookSignature)
            ? true : (++calls > 0 ? Task.FromResult(Paid(payment)) : throw new Exception()));
        var service = Service(db, provider);
        var result = await service.ReconcileDepositAsync(booking.BookingId, booking.CustomerId);
        Assert.Equal(PaymentStatus.DepositHeld, result!.PaymentStatus);
        Assert.Equal(BookingStatus.PendingConfirmation, result.Status);
        Assert.NotNull(result.DepositPaidAt);
        var count = await db.AppNotifications.CountAsync();
        await service.ReconcileDepositAsync(booking.BookingId, booking.CustomerId);
        Assert.True(await service.HandlePayOsWebhookAsync(new() { Success = true, Data = new() {
            OrderCode = payment.ProviderOrderCode, Amount = 15000, Currency = "VND", PaymentLinkId = payment.ProviderPaymentLinkId, Code = "00" } }));
        Assert.Equal(1, calls);
        Assert.Equal(count, await db.AppNotifications.CountAsync());
        Assert.Equal(BookingPaymentStatus.Paid, (await db.BookingPayments.SingleAsync()).Status);
    }

    [Fact]
    public async Task Foreign_customer_and_demo_never_call_provider()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var (booking, _) = await Seed(db, demo: true);
        var service = Service(db, ReviewTestProxy.Make<IPayOsService>((_, _) => throw new Exception("Unexpected external request")));
        Assert.Null(await service.ReconcileDepositAsync(booking.BookingId, Guid.NewGuid()));
        Assert.NotNull(await service.ReconcileDepositAsync(booking.BookingId, booking.CustomerId));
        Assert.Equal(PaymentStatus.Unpaid, (await db.Bookings.SingleAsync()).PaymentStatus);
    }

    [Fact]
    public async Task Paid_with_wrong_amount_cannot_confirm_deposit()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var (booking, payment) = await Seed(db);
        var service = Service(db, ReviewTestProxy.Make<IPayOsService>((_, _) => Task.FromResult(Paid(payment, 14000))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReconcileDepositAsync(booking.BookingId, booking.CustomerId));
        Assert.Equal(PaymentStatus.Unpaid, (await db.Bookings.SingleAsync()).PaymentStatus);
        Assert.Empty(await db.AppNotifications.ToListAsync());
    }

    [Fact]
    public async Task Pending_lookup_is_throttled_and_cannot_confirm_deposit()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var (booking, payment) = await Seed(db); var calls = 0;
        var service = Service(db, ReviewTestProxy.Make<IPayOsService>((_, _) => { calls++; return Task.FromResult(new PayOsPaymentLinkResult {
            OrderCode = payment.ProviderOrderCode, PaymentLinkId = payment.ProviderPaymentLinkId!, Amount = payment.Amount, Status = "PENDING" }); }));
        await service.ReconcileDepositAsync(booking.BookingId, booking.CustomerId);
        await service.ReconcileDepositAsync(booking.BookingId, booking.CustomerId);
        Assert.Equal(1, calls); Assert.Equal(PaymentStatus.Unpaid, (await db.Bookings.SingleAsync()).PaymentStatus);
    }

    [Fact]
    public async Task Expired_booking_with_received_money_goes_to_refund_without_reviving_the_slot()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var (booking, payment) = await Seed(db);
        var persisted = await db.Bookings.SingleAsync();
        persisted.Status = BookingStatus.Cancelled; persisted.PaymentStatus = PaymentStatus.Failed;
        persisted.PaymentExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = Service(db, ReviewTestProxy.Make<IPayOsService>((_, _) => Task.FromResult(Paid(payment))));
        var result = await service.ReconcileDepositAsync(booking.BookingId, booking.CustomerId);
        Assert.Equal(BookingStatus.Cancelled, result!.Status);
        Assert.Equal(PaymentStatus.RefundPending, result.PaymentStatus);
        Assert.Single(await db.Refunds.ToListAsync());
    }

    [Fact]
    public async Task Existing_store_app_booking_poll_also_repairs_missing_webhook()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var (booking, payment) = await Seed(db);
        (await db.BookingPayments.SingleAsync()).CreatedAt = DateTime.UtcNow.AddSeconds(-20);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = Service(db, ReviewTestProxy.Make<IPayOsService>((_, _) => Task.FromResult(Paid(payment))));
        var result = await service.GetBookingByIdAsync(booking.BookingId, booking.CustomerId);
        Assert.Equal(PaymentStatus.DepositHeld, result!.PaymentStatus);
    }

    [Fact]
    public async Task Existing_booking_poll_stays_readable_during_provider_outage()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var (booking, _) = await Seed(db);
        (await db.BookingPayments.SingleAsync()).CreatedAt = DateTime.UtcNow.AddSeconds(-20);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = Service(db, ReviewTestProxy.Make<IPayOsService>((_, _) => throw new HttpRequestException("offline")));
        var result = await service.GetBookingByIdAsync(booking.BookingId, booking.CustomerId);
        Assert.Equal(PaymentStatus.Unpaid, result!.PaymentStatus);
        Assert.Empty(await db.AppNotifications.ToListAsync());
    }

    private sealed class FakeHttp(string body) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; Assert.Equal(HttpMethod.Get, request.Method); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }); }
    }

    [Fact]
    public async Task Provider_lookup_checks_response_signature_before_returning_paid()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var (booking, payment) = await Seed(db);
        var body = JsonSerializer.Serialize(new { code = "00", signature = "invalid", data = new { status = "PAID", orderCode = payment.ProviderOrderCode } });
        var http = new FakeHttp(body);
        var context = new DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, booking.CustomerId.ToString()) }, "test")) };
        var service = new PayOsService(new HttpClient(http) { BaseAddress = new Uri("https://provider.test") }, Config, db, new HttpContextAccessor { HttpContext = context });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetPaymentLinkAsync(payment.ProviderOrderCode));
        Assert.Equal(1, http.Calls); Assert.Equal(PaymentStatus.Unpaid, (await db.Bookings.SingleAsync()).PaymentStatus);
    }

    [Fact]
    public async Task Lookup_accepts_response_signed_by_the_official_sdk_with_nullable_transaction_fields()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var (booking, payment) = await Seed(db);
        var data = new Dictionary<string, object?> {
            ["id"] = payment.ProviderPaymentLinkId, ["orderCode"] = payment.ProviderOrderCode,
            ["amount"] = 15000, ["amountPaid"] = 15000, ["amountRemaining"] = 0, ["status"] = "PAID",
            ["transactions"] = new object[] { new Dictionary<string, object?> {
                ["reference"] = "test-ref", ["amount"] = 15000, ["transactionDateTime"] = "2026-10-24T14:12:00+07:00", ["counterAccountName"] = null } }
        };
        var signature = new PayOS.Crypto.CryptoProvider().CreateSignatureFromObject(data, Key);
        var http = new FakeHttp(JsonSerializer.Serialize(new { code = "00", data, signature }));
        var context = new DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, booking.CustomerId.ToString()) }, "test")) };
        var service = new PayOsService(new HttpClient(http) { BaseAddress = new Uri("https://provider.test") }, Config, db, new HttpContextAccessor { HttpContext = context });
        var result = await service.GetPaymentLinkAsync(payment.ProviderOrderCode);
        Assert.Equal("PAID", result.Status); Assert.Equal(15000, result.AmountPaid);
        Assert.Equal("test-ref", result.Reference);
        Assert.Empty(db.ChangeTracker.Entries<BookingPayment>());
    }

    [Fact]
    public async Task Booking_read_inside_existing_transaction_keeps_internal_flows_local()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var (booking, _) = await Seed(db);
        (await db.BookingPayments.SingleAsync()).CreatedAt = DateTime.UtcNow.AddSeconds(-20);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = Service(db, ReviewTestProxy.Make<IPayOsService>((_, _) => throw new Exception("Unexpected provider request inside transaction")));
        await using var transaction = await db.Database.BeginTransactionAsync();
        Assert.Equal(PaymentStatus.Unpaid, (await service.GetBookingByIdAsync(booking.BookingId, booking.CustomerId))!.PaymentStatus);
        await transaction.CommitAsync();
    }

    [PostgreSqlFact]
    public async Task Published_app_poll_and_webhook_race_records_the_deposit_once_on_postgresql()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        Booking booking; BookingPayment payment;
        await using (var seed = database.CreateContext())
        {
            (booking, payment) = await Seed(seed);
            (await seed.BookingPayments.SingleAsync()).CreatedAt = DateTime.UtcNow.AddSeconds(-20);
            await seed.SaveChangesAsync();
        }
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLookup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<PayOsPaymentLinkResult> Lookup()
        {
            lookupStarted.TrySetResult();
            await releaseLookup.Task.WaitAsync(TimeSpan.FromSeconds(15));
            return Paid(payment);
        }
        var provider = ReviewTestProxy.Make<IPayOsService>((method, _) => method.Name == nameof(IPayOsService.IsValidWebhookSignature) ? true : Lookup());
        await using var pollDb = database.CreateContext();
        await using var webhookDb = database.CreateContext();
        var poll = Service(pollDb, provider).GetBookingByIdAsync(booking.BookingId, booking.CustomerId);
        try
        {
            await lookupStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(await Service(webhookDb, provider).HandlePayOsWebhookAsync(new() { Success = true, Data = new() {
                OrderCode = payment.ProviderOrderCode, Amount = 15000, Currency = "VND", PaymentLinkId = payment.ProviderPaymentLinkId, Code = "00" } }));
            var notifications = await webhookDb.AppNotifications.CountAsync();
            releaseLookup.TrySetResult();
            Assert.Equal(PaymentStatus.DepositHeld, (await poll)!.PaymentStatus);
            await using var check = database.CreateContext();
            Assert.Equal(BookingPaymentStatus.Paid, (await check.BookingPayments.SingleAsync()).Status);
            Assert.Equal(BookingStatus.PendingConfirmation, (await check.Bookings.SingleAsync()).Status);
            Assert.Equal(notifications, await check.AppNotifications.CountAsync());
        }
        finally { releaseLookup.TrySetResult(); }
    }
}
