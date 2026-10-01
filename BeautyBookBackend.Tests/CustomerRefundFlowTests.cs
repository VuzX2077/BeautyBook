using System.Text.Json;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeautyBookBackend.Tests;

public sealed class CustomerRefundFlowTests
{
    [Fact]
    public async Task EnsureRefund_without_destination_creates_one_refund_and_one_action_notification()
    {
        await using var store = await RefundStore.CreateAsync(withUsableBank: false);
        var first = await store.Service.EnsureRefundAsync(store.Booking, store.Payment, 100_000,
            RefundReasonCode.MuaRejected, "MUA từ chối", store.MuaId);
        await store.Db.SaveChangesAsync();
        var second = await store.Service.EnsureRefundAsync(store.Booking, store.Payment, 100_000,
            RefundReasonCode.MuaRejected, "MUA từ chối", store.MuaId);
        await store.Db.SaveChangesAsync();

        Assert.Equal(first.RefundId, second.RefundId);
        Assert.Equal(RefundStatus.AwaitingDestination, first.Status);
        Assert.Single(await store.Db.Refunds.ToListAsync());
        var notification = Assert.Single(await store.Db.AppNotifications.ToListAsync());
        Assert.StartsWith("REFUND_STATUS_CHANGED_AWAITINGDESTINATION_", notification.Type);
        Assert.Equal("Cần bổ sung tài khoản nhận tiền", notification.Title);
        using var data = JsonDocument.Parse(notification.DataJson!);
        Assert.Equal(first.RefundId, data.RootElement.GetProperty("refundId").GetGuid());
        Assert.Equal(store.Booking.BookingId, data.RootElement.GetProperty("bookingId").GetGuid());
        Assert.Equal("REFUND_STATUS_CHANGED", data.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task EnsureRefund_with_destination_starts_pending_and_masks_customer_response()
    {
        await using var store = await RefundStore.CreateAsync(withUsableBank: true);
        var refund = await store.Service.EnsureRefundAsync(store.Booking, store.Payment, 100_000,
            RefundReasonCode.MuaRejected, "MUA từ chối", store.MuaId);
        await store.Db.SaveChangesAsync();
        store.Db.ChangeTracker.Clear();

        var detail = await store.Service.GetCustomerRefundAsync(refund.RefundId, store.CustomerId);
        Assert.NotNull(detail);
        Assert.Equal(RefundStatus.Pending, detail.Status);
        Assert.Equal("**1111", detail.MaskedDestinationAccountNumber);
        Assert.DoesNotContain("111111", JsonSerializer.Serialize(detail));
        Assert.Equal("Đã tiếp nhận yêu cầu hoàn tiền", (await store.Db.AppNotifications.SingleAsync()).Title);
    }

    [Fact]
    public async Task Customer_can_only_read_own_refunds_and_list_is_newest_first()
    {
        await using var store = await RefundStore.CreateAsync(withUsableBank: false);
        var refund = await store.Service.EnsureRefundAsync(store.Booking, store.Payment, 100_000,
            RefundReasonCode.MuaRejected, "MUA từ chối", store.MuaId);
        await store.Db.SaveChangesAsync();
        store.Db.ChangeTracker.Clear();

        Assert.Null(await store.Service.GetCustomerRefundAsync(refund.RefundId, Guid.NewGuid()));
        Assert.Empty((await store.Service.GetCustomerRefundsAsync(Guid.NewGuid(), 1, 20)).Items);
        var owned = await store.Service.GetCustomerRefundsAsync(store.CustomerId, 1, 20);
        Assert.Single(owned.Items);
        Assert.Equal(refund.RefundId, owned.Items[0].RefundId);
    }

    private sealed class RefundStore : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public ApplicationDbContext Db { get; }
        public RefundService Service { get; }
        public Guid CustomerId { get; } = Guid.NewGuid();
        public Guid MuaId { get; } = Guid.NewGuid();
        public Booking Booking { get; private set; } = null!;
        public BookingPayment Payment { get; private set; } = null!;

        private RefundStore(SqliteConnection connection, ApplicationDbContext db)
        {
            _connection = connection;
            Db = db;
            Service = new RefundService(db, new UnusedReceivables(), new UnusedProvider(),
                new ConfigurationBuilder().AddInMemoryCollection().Build(), NullLogger<RefundService>.Instance);
        }

        public static async Task<RefundStore> CreateAsync(bool withUsableBank)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new RefundTestDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var store = new RefundStore(connection, db);
            var now = DateTime.UtcNow;
            db.Users.Add(new User { UserId=store.CustomerId, Email="customer@example.com", Role=UserRole.Customer, IsActive=true, CreatedAt=now });
            store.Booking = new Booking { BookingId=Guid.NewGuid(), CustomerId=store.CustomerId, MUAId=store.MuaId,
                Status=BookingStatus.Rejected, PaymentStatus=PaymentStatus.RefundPending, BookingDate=now,
                StartTime=TimeSpan.FromHours(8), EndTime=TimeSpan.FromHours(9), CreatedAt=now, UpdatedAt=now };
            store.Payment = new BookingPayment { PaymentId=Guid.NewGuid(), BookingId=store.Booking.BookingId,
                CustomerId=store.CustomerId, Amount=100_000, Status=BookingPaymentStatus.RefundPending,
                ExpiresAt=now.AddHours(1), CreatedAt=now, UpdatedAt=now };
            db.AddRange(store.Booking, store.Payment);
            if (withUsableBank) db.BankAccounts.Add(new BankAccount { Id=Guid.NewGuid(), UserId=store.CustomerId,
                BankCode="VCB", BankBin="970436", BankName="Vietcombank", AccountNumber="111111",
                NormalizedAccountNumber="111111", CanonicalBankKey="BIN:970436", AccountHolderName="NGUYEN VAN A",
                Method="BANK", VerificationStatus=BankAccountEligibility.Approved, ActivatedAt=now.AddDays(-1),
                IsActive=true, IsDefault=true, CreatedAt=now, UpdatedAt=now });
            await db.SaveChangesAsync();
            return store;
        }

        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private sealed class RefundTestDbContext : ApplicationDbContext
    {
        public RefundTestDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var allowed = new HashSet<Type>
            {
                typeof(User), typeof(Booking), typeof(BookingPayment),
                typeof(BankAccount), typeof(Refund), typeof(AppNotification)
            };
            foreach (var entityType in modelBuilder.Model.GetEntityTypes().Select(x => x.ClrType).Where(x => !allowed.Contains(x)).ToList())
                modelBuilder.Ignore(entityType);
            modelBuilder.Entity<User>(b =>
            {
                b.HasKey(x => x.UserId);
                b.Ignore(x => x.MakeupArtistProfile);
                b.Ignore(x => x.BankAccounts);
            });
            modelBuilder.Entity<Booking>(b =>
            {
                b.HasKey(x => x.BookingId);
                b.Ignore(x => x.MakeupArtistProfile);
                b.Ignore(x => x.BookingServices);
                b.Ignore(x => x.Payments);
                b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId);
            });
            modelBuilder.Entity<BookingPayment>(b =>
            {
                b.HasKey(x => x.PaymentId);
                b.Ignore(x => x.Booking);
                b.Ignore(x => x.Customer);
            });
            modelBuilder.Entity<BankAccount>(b =>
            {
                b.HasKey(x => x.Id);
                b.Ignore(x => x.User);
            });
            modelBuilder.Entity<Refund>(b =>
            {
                b.HasKey(x => x.RefundId);
                b.Ignore(x => x.BookingPayment);
                b.Ignore(x => x.RequestedByUser);
                b.Ignore(x => x.LastHandledByUser);
                b.Ignore(x => x.DestinationBankAccount);
                b.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId);
                b.HasIndex(x => x.BookingPaymentId).IsUnique();
            });
            modelBuilder.Entity<AppNotification>(b =>
            {
                b.HasKey(x => x.Id);
                b.Ignore(x => x.User);
                b.Ignore(x => x.Booking);
                b.Ignore(x => x.Message);
                b.Ignore(x => x.Campaign);
            });
        }
    }

    private sealed class UnusedProvider : IRefundPayoutProvider
    {
        public Task<RefundPayoutResult> CreateAsync(RefundPayoutRequest request, string idempotencyKey) => throw new NotSupportedException();
        public Task<RefundPayoutResult> GetAsync(string payoutId) => throw new NotSupportedException();
    }

    private sealed class UnusedReceivables : IMuaReceivableService
    {
        public Task<MuaReceivable> EnsureForCompletedBookingAsync(Booking booking) => throw new NotSupportedException();
        public Task FreezeForDisputeAsync(Guid bookingId) => throw new NotSupportedException();
        public Task RestoreAfterMuaWinsAsync(Guid bookingId) => throw new NotSupportedException();
        public Task ReverseAsync(Guid bookingId) => throw new NotSupportedException();
        public Task<int> ReconcileStatesAsync() => throw new NotSupportedException();
        public Task<MuaEarningsDto> GetEarningsAsync(Guid muaId) => throw new NotSupportedException();
    }
}
