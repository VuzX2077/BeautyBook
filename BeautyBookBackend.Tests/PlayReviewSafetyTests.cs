using System.Net;
using System.Reflection;
using System.Text.Json;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeautyBookBackend.Tests;

public sealed class PlayReviewSafetyTests
{
    private static IConfiguration Config => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Otp:HashKey"]="test-only-hash-secret", ["PayOS:ClientId"]="test", ["PayOS:ApiKey"]="test",
        ["PayOS:ChecksumKey"]="test", ["Email:BrevoApiKey"]="test", ["Email:FromEmail"]="sender@example.com",
        ["Refunds:AutomatedPayoutEnabled"]="true" }).Build();

    private static async Task<(User Customer, User Mua, Guid Service)> Seed(ApplicationDbContext db, bool customerDemo, bool muaDemo)
    {
        var c = new User { UserId=Guid.NewGuid(), Email=$"{Guid.NewGuid():N}@example.com", IsActive=true, IsDemoAccount=customerDemo, Role=UserRole.Customer, CreatedAt=DateTime.UtcNow };
        var m = new User { UserId=Guid.NewGuid(), Email=$"{Guid.NewGuid():N}@example.com", IsActive=true, IsDemoAccount=muaDemo, Role=UserRole.MUA, CreatedAt=DateTime.UtcNow };
        db.Users.AddRange(c,m);
        db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId=m.UserId, Status=MuaStatus.Listed, VerificationStatus=MuaVerificationStatus.Approved });
        var service = new BeautyBookBackend.Models.Service { ServiceId=Guid.NewGuid(), MUAId=m.UserId, ServiceName="Test makeup", Price=100_000, DurationMinutes=60, IsActive=true };
        db.Services.Add(service); await db.SaveChangesAsync(); return (c,m,service.ServiceId);
    }

    private static IMuaEligibilityService Eligible(Action<bool>? evaluated = null) => ReviewTestProxy.Make<IMuaEligibilityService>((method,args) => {
        if (method.Name != nameof(IMuaEligibilityService.EvaluateAsync)) throw new NotSupportedException();
        evaluated?.Invoke((bool)args![1]!);
        return Task.FromResult<MuaEligibilityDto?>(new() { CanReceiveBookings=true, CanWithdraw=true });
    });
    private static IMuaScheduleService Schedule => ReviewTestProxy.Make<IMuaScheduleService>((method,_) => method.Name switch {
        nameof(IMuaScheduleService.IsAvailableAsync) or nameof(IMuaScheduleService.HasValidScheduleAsync) => Task.FromResult(true),
        nameof(IMuaScheduleService.GetAvailableStartsAsync) => Task.FromResult<IReadOnlyList<TimeSpan>>([TimeSpan.FromHours(10)]),
        _ => throw new NotSupportedException() });
    private static BeautyBookBackend.Services.BookingService Bookings(ApplicationDbContext db, IPayOsService? pay = null, IMuaEligibilityService? eligibility = null, IHttpContextAccessor? accessor = null) => new(
        new BookingRepository(db), new MuaRepository(db), new ReviewRepository(db), new UnitOfWork(db), new BookingNotificationService(db, new BookingTimeService(TimeZoneInfo.Utc)),
        db, pay ?? ReviewTestProxy.Make<IPayOsService>((_,_) => throw new Exception("Unexpected provider call")),
        new RefundService(db,new MuaReceivableService(db),ReviewTestProxy.Make<IRefundPayoutProvider>((_,_) => throw new Exception("Unexpected payout call")),Config,NullLogger<RefundService>.Instance),
        new BookingRefundPolicyService(new BookingTimeService(TimeZoneInfo.Utc), NullLogger<BookingRefundPolicyService>.Instance), new MuaReceivableService(db), Config,
        eligibility ?? Eligible(), Schedule, new BookingTimeService(TimeZoneInfo.Utc), accessor);
    private static BookingCreateDto Request(Guid mua, Guid service) => new() {
        MUAId=mua, IdempotencyKey=Guid.NewGuid().ToString(), BookingDate=DateTime.UtcNow.Date.AddDays(2),
        StartTime=TimeSpan.FromHours(10), ServiceAddress="Test address", Services=[new() { ServiceId=service, ParticipantsCount=1 }] };

    [Theory]
    [InlineData(false,false)] [InlineData(true,true)] [InlineData(true,false)] [InlineData(false,true)]
    public async Task Booking_domain_matrix_guards_before_eligibility_schedule_and_slot_writes(bool customerDemo, bool muaDemo)
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db=store.Db;
        var ids = await Seed(db,customerDemo,muaDemo); var evaluations=0;
        var service=Bookings(db,eligibility:Eligible(_ => evaluations++));
        if (customerDemo || customerDemo != muaDemo)
        {
            await Assert.ThrowsAsync<PlayReviewOperationException>(() => service.CreateBookingAsync(ids.Customer.UserId,Request(ids.Mua.UserId,ids.Service)));
            Assert.Equal(0,evaluations); Assert.Empty(await db.Bookings.ToListAsync()); Assert.Empty(await db.BookingServices.ToListAsync());
        }
        else
        {
            Assert.NotNull(await service.CreateBookingAsync(ids.Customer.UserId,Request(ids.Mua.UserId,ids.Service)));
            var booking=await db.Bookings.SingleAsync(); Assert.Equal(customerDemo,booking.IsDemo); Assert.Equal(BookingStatus.PendingPayment,booking.Status);
            Assert.Equal(1,evaluations);
        }
        Assert.Empty(await db.BookingPayments.ToListAsync()); Assert.Empty(await db.AppNotifications.ToListAsync());
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Deposit_normal_uses_provider_once_demo_creates_no_payment_and_calls_zero(bool demo)
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var ids=await Seed(db,demo,demo);
        var calls=0; var fake=ReviewTestProxy.Make<IPayOsService>((method,_) => {
            Assert.Equal(nameof(IPayOsService.CreatePaymentLinkAsync),method.Name); calls++;
            return Task.FromResult(new PayOsCreatePaymentResult { PaymentLinkId="test-link",CheckoutUrl="https://test.invalid",QrCode="test" }); });
        var service=Bookings(db,fake);
        if (demo)
        {
            // Phase 3 requires configured simulation even for create. Seed only this
            // isolated test resource so this case still verifies deposit fails closed.
            db.Bookings.Add(new Booking { BookingId=Guid.NewGuid(), CustomerId=ids.Customer.UserId, MUAId=ids.Mua.UserId,
                IsDemo=true, Status=BookingStatus.PendingPayment, PaymentStatus=PaymentStatus.Unpaid,
                TotalAmount=100_000, DepositAmount=30_000, PlatformFeeAmount=8_000, MuaPayoutAmount=22_000,
                RemainingAmount=70_000, PaymentExpiresAt=DateTime.UtcNow.AddMinutes(15) });
            await db.SaveChangesAsync();
        }
        else await service.CreateBookingAsync(ids.Customer.UserId,Request(ids.Mua.UserId,ids.Service));
        var booking=await db.Bookings.SingleAsync();
        if(demo) { await Assert.ThrowsAsync<PlayReviewOperationException>(()=>service.CreateDepositPaymentAsync(booking.BookingId,ids.Customer.UserId));Assert.Equal(0,calls);Assert.Empty(await db.BookingPayments.ToListAsync()); }
        else { Assert.NotNull(await service.CreateDepositPaymentAsync(booking.BookingId,ids.Customer.UserId));Assert.Equal(1,calls);Assert.Equal(PaymentProvider.PayOS,(await db.BookingPayments.SingleAsync()).Provider); }
    }

    private static async Task<(Booking Booking, BookingPayment Payment)> Money(ApplicationDbContext db, User customer, User mua, bool demo)
    {
        var now=DateTime.UtcNow; var b=new Booking { BookingId=Guid.NewGuid(),CustomerId=customer.UserId,MUAId=mua.UserId,IsDemo=demo,
            Status=BookingStatus.Completed,PaymentStatus=PaymentStatus.DepositHeld,BookingDate=now.AddDays(2),StartTime=TimeSpan.FromHours(10),EndTime=TimeSpan.FromHours(11),
            DepositAmount=30_000,TotalAmount=100_000,PlatformFeeAmount=8_000,MuaPayoutAmount=22_000,CreatedAt=now,UpdatedAt=now,CompletedAt=now };
        var p=new BookingPayment { PaymentId=Guid.NewGuid(),BookingId=b.BookingId,CustomerId=customer.UserId,ProviderOrderCode=Random.Shared.NextInt64(1,long.MaxValue),
            Amount=30_000,Status=BookingPaymentStatus.Paid,PaidAt=now,CreatedAt=now,UpdatedAt=now,ExpiresAt=now.AddDays(1) };
        db.AddRange(b,p);await db.SaveChangesAsync();return(b,p);
    }
    private static BankAccount Bank(ApplicationDbContext db, Guid user) {
        var bank=new BankAccount { Id=Guid.NewGuid(),UserId=user,BankCode="VCB",BankBin="970436",BankName="VCB",AccountNumber="123456789",NormalizedAccountNumber="123456789",
            CanonicalBankKey="BIN:970436",AccountHolderName="TEST USER",Method="BANK",IsActive=true,IsDefault=true,VerificationStatus=BankAccountEligibility.Approved,CreatedAt=DateTime.UtcNow,UpdatedAt=DateTime.UtcNow,ActivatedAt=DateTime.UtcNow };
        db.BankAccounts.Add(bank);return bank;
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Refund_normal_creates_existing_obligation_demo_no_obligation_no_queue_no_provider(bool demo)
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var ids=await Seed(db,demo,demo);var money=await Money(db,ids.Customer,ids.Mua,demo);var calls=0;
        var refund=new RefundService(db,new MuaReceivableService(db),ReviewTestProxy.Make<IRefundPayoutProvider>((_,_)=>{calls++;throw new Exception("Unexpected provider");}),Config,NullLogger<RefundService>.Instance);
        if(demo) await Assert.ThrowsAsync<PlayReviewOperationException>(()=>refund.EnsureRefundAsync(money.Booking,money.Payment,30_000,RefundReasonCode.MuaRejected,"test",ids.Mua.UserId));
        else { await refund.EnsureRefundAsync(money.Booking,money.Payment,30_000,RefundReasonCode.MuaRejected,"test",ids.Mua.UserId);await db.SaveChangesAsync();Assert.Single(await db.Refunds.ToListAsync());Assert.Single(await refund.GetAdminQueueAsync()); }
        if(demo) { Assert.Empty(await db.Refunds.ToListAsync()); Assert.Empty(await refund.GetAdminQueueAsync());Assert.Empty(await db.AppNotifications.ToListAsync()); }
        Assert.Equal(0,calls);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Payout_normal_existing_flow_demo_no_production_obligation(bool demo)
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var ids=await Seed(db,demo,demo);var money=await Money(db,ids.Customer,ids.Mua,demo);var bank=Bank(db,ids.Mua.UserId);
        var r=new MuaReceivable { Id=Guid.NewGuid(),BookingId=money.Booking.BookingId,MuaId=ids.Mua.UserId,NetAmount=22_000,Status=MuaReceivableStatus.Available,AvailableAt=DateTime.UtcNow.AddMinutes(-1),CreatedAt=DateTime.UtcNow,UpdatedAt=DateTime.UtcNow };
        db.MuaReceivables.Add(r);await db.SaveChangesAsync();var payout=new PayoutService(db,Eligible());var request=new CreatePayoutRequest { BankAccountId=bank.Id,IdempotencyKey="test",ReceivableIds=[r.Id] };
        if(demo) { await Assert.ThrowsAsync<PlayReviewOperationException>(()=>payout.CreateAsync(ids.Mua.UserId,request));Assert.Empty(await db.Payouts.ToListAsync());Assert.Empty(await payout.GetPendingAdminAsync());Assert.Equal(MuaReceivableStatus.Available,r.Status); }
        else { var result=await payout.CreateAsync(ids.Mua.UserId,request);Assert.Equal(PayoutProvider.Manual,result.Provider);Assert.Equal(PayoutStatus.ManualActionRequired,result.Status);Assert.Single(await payout.GetPendingAdminAsync()); }
    }

    [Theory] [InlineData(true,false)] [InlineData(false,true)]
    public async Task Social_mixed_domains_block_follow_unfollow_chat_comment_like_save_reply_and_review(bool actorDemo,bool targetDemo)
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var ids=await Seed(db,actorDemo,targetDemo);
        var portfolio=new Portfolio { PortfolioId=Guid.NewGuid(),MUAId=ids.Mua.UserId };db.Portfolios.Add(portfolio);
        var booking=new Booking { BookingId=Guid.NewGuid(),CustomerId=ids.Customer.UserId,MUAId=ids.Mua.UserId,IsDemo=actorDemo,Status=BookingStatus.Completed };db.Bookings.Add(booking);await db.SaveChangesAsync();
        var follow=new FollowService(db);var mua=new MuaService(new MuaRepository(db),new UserRepository(db),new UnitOfWork(db),db,Eligible());
        var chat=new ChatService(new ChatRepository(db),db,new ChatNotificationService(db),NullLogger<ChatService>.Instance);
        foreach(var following in new[]{true,false}) await Assert.ThrowsAsync<PlayReviewOperationException>(()=>follow.SetFollowing(ids.Mua.UserId,ids.Customer.UserId,following));
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>chat.GetOrCreateChatRoomAsync(ids.Customer.UserId,ids.Mua.UserId));
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>mua.TogglePortfolioLikeAsync(ids.Customer.UserId,portfolio.PortfolioId));
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>mua.TogglePortfolioSaveAsync(ids.Customer.UserId,portfolio.PortfolioId));
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>mua.AddPortfolioCommentAsync(ids.Customer.UserId,portfolio.PortfolioId,"test"));
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>mua.ReplyToPortfolioCommentAsync(ids.Customer.UserId,portfolio.PortfolioId,Guid.NewGuid(),"test"));
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>Bookings(db).AddReviewAsync(booking.BookingId,ids.Customer.UserId,new(){Rating=5,Comment="test"}));
        Assert.Empty(await db.MuaFollows.ToListAsync());Assert.Empty(await db.ChatRooms.ToListAsync());Assert.Empty(await db.PortfolioComments.ToListAsync());Assert.Empty(await db.Reviews.ToListAsync());
    }

    [Fact]
    public async Task Demo_bank_all_mutations_and_otp_identity_upload_submit_admin_approval_are_blocked()
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var ids=await Seed(db,true,true);var calls=0;
        var otp=ReviewTestProxy.Make<IEmailOtpService>((_,_)=>{calls++;throw new Exception("Unexpected OTP");});var banks=new BankAccountService(db,otp);
        var draft=new BankAccountDraftRequest();var upsert=new UpsertBankAccountRequest();var user=ids.Customer.UserId;var id=Guid.NewGuid();
        var operations=new Func<Task>[] { ()=>banks.RequestAddOtpAsync(user,draft),()=>banks.RequestUpdateOtpAsync(user,id,draft),()=>banks.AddAsync(user,upsert),()=>banks.UpdateAsync(user,id,upsert),()=>banks.SetDefaultAsync(user,id),()=>banks.DeactivateAsync(user,id),()=>banks.RequestDefaultOtpAsync(user,id),()=>banks.SetDefaultWithOtpAsync(user,id,"123456") };
        foreach(var operation in operations)await Assert.ThrowsAsync<PlayReviewOperationException>(operation);Assert.Equal(0,calls);Assert.Empty(await db.BankAccounts.ToListAsync());
        var media=new VerificationMediaService(db,ReviewTestProxy.Make<IVerificationStorage>((_,_)=>throw new Exception("Unexpected storage")));var eligibility=new MuaEligibilityService(db,Schedule,media);
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>media.UploadAsync(ids.Mua.UserId,"identity-front",[1]));
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>eligibility.UpdateIdentityVerificationAsync(ids.Mua.UserId,new()));
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>eligibility.SubmitForReviewAsync(ids.Mua.UserId));
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>eligibility.ReviewAsync(ids.Mua.UserId,Guid.NewGuid(),true));
        Assert.Empty(await db.VerificationMedia.ToListAsync());
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Email_normal_uses_abstraction_demo_otp_and_Brevo_boundary_call_zero(bool demo)
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var ids=await Seed(db,demo,demo);var abstractionCalls=0;
        var sender=ReviewTestProxy.Make<IEmailSender>((_,_)=>{abstractionCalls++;return Task.CompletedTask;});var otp=new EmailOtpService(db,sender,Config);
        if(demo) await Assert.ThrowsAsync<PlayReviewOperationException>(()=>otp.IssueAsync(ids.Customer.Email!,"RESET_PASSWORD"));
        else await otp.IssueAsync(ids.Customer.Email!,"RESET_PASSWORD");
        Assert.Equal(demo?0:1,abstractionCalls);Assert.Equal(demo?0:1,await db.EmailOtps.CountAsync());
        var http=new CountingHttp();var brevo=new BrevoEmailSender(new HttpClient(http),Config,NullLogger<BrevoEmailSender>.Instance,db);
        if(demo)await Assert.ThrowsAsync<PlayReviewOperationException>(()=>brevo.SendOtpAsync(ids.Customer.Email!,"123456","RESET_PASSWORD"));
        else await brevo.SendOtpAsync(ids.Customer.Email!,"123456","RESET_PASSWORD");Assert.Equal(demo?0:1,http.Calls);
    }

    [Theory] [InlineData(true,false)] [InlineData(false,true)] [InlineData(true,true)]
    public async Task PayOS_boundary_rejects_demo_or_inconsistent_provider_without_HTTP(bool demo,bool simulated)
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var ids=await Seed(db,demo,demo);var money=await Money(db,ids.Customer,ids.Mua,demo);
        money.Payment.Provider=simulated?PaymentProvider.Simulated:PaymentProvider.PayOS;await db.SaveChangesAsync();var http=new CountingHttp();var pay=new PayOsService(new HttpClient(http),Config,db);
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>pay.CreatePaymentLinkAsync(new(){OrderCode=money.Payment.ProviderOrderCode,Amount=30_000}));Assert.Equal(0,http.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>pay.CreatePaymentLinkAsync(new(){OrderCode=-1,Amount=30_000}));Assert.Equal(0,http.Calls);
    }

    [Fact]
    public async Task Demo_notifications_producer_and_stale_worker_rows_never_call_Expo_or_advance_booking()
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var ids=await Seed(db,true,true);var money=await Money(db,ids.Customer,ids.Mua,true);
        money.Booking.Status=BookingStatus.Approved;money.Booking.BookingDate=DateTime.UtcNow.Date.AddDays(-1);
        await new BookingNotificationService(db,new BookingTimeService(TimeZoneInfo.Utc)).QueueBookingStatusAsync(money.Booking,BookingStatus.Approved,ids.Mua.UserId);await db.SaveChangesAsync();
        Assert.All(await db.AppNotifications.ToListAsync(),x=>Assert.Equal("Skipped",x.Status));
        db.AppNotifications.Add(new(){Id=Guid.NewGuid(),UserId=ids.Customer.UserId,BookingId=money.Booking.BookingId,Type="LEGACY",Status="Pending",ScheduledAt=DateTime.UtcNow.AddMinutes(-1)});
        db.DevicePushTokens.Add(new(){Id=Guid.NewGuid(),UserId=ids.Customer.UserId,ExpoPushToken="ExponentPushToken[test]",IsActive=true,Platform="android"});await db.SaveChangesAsync();
        var http=new CountingHttp();using var provider=new ServiceCollection().AddSingleton(db).AddSingleton(new BookingTimeService(TimeZoneInfo.Utc)).BuildServiceProvider();
        var factory=ReviewTestProxy.Make<IHttpClientFactory>((_,_)=>new HttpClient(http){BaseAddress=new Uri("https://test.invalid/")});
        var worker=new PushNotificationWorker(provider.GetRequiredService<IServiceScopeFactory>(),factory,NullLogger<PushNotificationWorker>.Instance);
        await (Task)typeof(PushNotificationWorker).GetMethod("ProcessAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(worker,[CancellationToken.None])!;
        Assert.Equal(0,http.Calls);Assert.Equal(BookingStatus.Approved,money.Booking.Status);Assert.All(await db.AppNotifications.ToListAsync(),x=>Assert.Equal("Skipped",x.Status));
    }

    [Fact]
    public async Task Missing_users_relations_and_booking_snapshot_mismatch_fail_closed_and_snapshot_is_immutable()
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var policy=new PlayReviewPolicy(db);
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>policy.IsDemoUserAsync(Guid.NewGuid()));await Assert.ThrowsAsync<PlayReviewOperationException>(()=>policy.EnsureNormalBookingAsync(Guid.NewGuid()));
        var ids=await Seed(db,true,true);var money=await Money(db,ids.Customer,ids.Mua,false);
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>policy.EnsureBookingDomainAsync(money.Booking.BookingId));
        money.Booking.IsDemo=true;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }

    private sealed class CountingHttp : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) {
            Calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created){Content=new StringContent("{\"data\":[{\"status\":\"ok\"}]}")});
        }
    }

    [PostgreSqlFact]
    public async Task PostgreSQL_booking_matrix_and_normal_deposit_regression()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync(); await using var db=database.CreateContext();
        foreach(var pair in new[]{(false,false),(true,true),(true,false),(false,true)})
        {
            var ids=await Seed(db,pair.Item1,pair.Item2);var calls=0;var evaluations=0;
            var pay=ReviewTestProxy.Make<IPayOsService>((_,_)=>{calls++;return Task.FromResult(new PayOsCreatePaymentResult{PaymentLinkId=Guid.NewGuid().ToString(),CheckoutUrl="https://test.invalid",QrCode="test"});});
            var service=Bookings(db,pay,Eligible(_=>evaluations++));var request=Request(ids.Mua.UserId,ids.Service);
            if(pair.Item1 || pair.Item1 != pair.Item2)
            {
                await Assert.ThrowsAsync<PlayReviewOperationException>(()=>service.CreateBookingAsync(ids.Customer.UserId,request));Assert.Equal(0,evaluations);
                Assert.False(await db.Bookings.AnyAsync(x=>x.CustomerId==ids.Customer.UserId));
            }
            else
            {
                var result=await service.CreateBookingAsync(ids.Customer.UserId,request);Assert.NotNull(result);
                var b=await db.Bookings.SingleAsync(x=>x.BookingId==result.BookingId);Assert.Equal(pair.Item1,b.IsDemo);
                if(pair.Item1)await Assert.ThrowsAsync<PlayReviewOperationException>(()=>service.CreateDepositPaymentAsync(b.BookingId,ids.Customer.UserId));
                else {Assert.NotNull(await service.CreateDepositPaymentAsync(b.BookingId,ids.Customer.UserId));Assert.Equal(1,calls);}
            }
            if(pair.Item1 || pair.Item1!=pair.Item2)Assert.Equal(0,calls);
        }
    }

    [PostgreSqlFact]
    public async Task Dashboard_counts_only_normal_users_bookings_payments_and_refunds()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();await using var db=database.CreateContext();
        foreach(var demo in new[]{false,true})
        {
            var ids=await Seed(db,demo,demo);var money=await Money(db,ids.Customer,ids.Mua,demo);
            db.Refunds.Add(new(){RefundId=Guid.NewGuid(),BookingId=money.Booking.BookingId,BookingPaymentId=money.Payment.PaymentId,Amount=10_000,
                Status=RefundStatus.Completed,CreatedAt=DateTime.UtcNow,UpdatedAt=DateTime.UtcNow,CompletedAt=DateTime.UtcNow});
        }
        await db.SaveChangesAsync();var day=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var result=Assert.IsType<OkObjectResult>(await new AdminDashboardController(db).Get(day,day,CancellationToken.None));
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(result.Value));var root=json.RootElement;
        Assert.Equal(2,root.GetProperty("users").GetProperty("total").GetInt32());var current=root.GetProperty("current");
        Assert.Equal(1,current.GetProperty("completedBookings").GetInt32());Assert.Equal(8_000,current.GetProperty("revenue").GetDecimal());
        Assert.Equal(100_000,current.GetProperty("bookingValue").GetDecimal());Assert.Equal(30_000,current.GetProperty("depositsCollected").GetDecimal());
        Assert.Equal(10_000,current.GetProperty("refundsCompleted").GetDecimal());Assert.Equal(1,root.GetProperty("bookingStatuses")[0].GetProperty("count").GetInt32());
    }

    [PostgreSqlFact]
    public async Task Admin_direct_demo_refund_and_bank_actions_and_workers_are_blocked()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();await using var db=database.CreateContext();var ids=await Seed(db,true,true);var money=await Money(db,ids.Customer,ids.Mua,true);
        var bank=Bank(db,ids.Customer.UserId);bank.VerificationStatus=BankAccountEligibility.Pending;bank.IsDefault=false;
        var refund=new Refund{RefundId=Guid.NewGuid(),BookingId=money.Booking.BookingId,BookingPaymentId=money.Payment.PaymentId,Amount=30_000,
            Status=RefundStatus.Pending,CreatedAt=DateTime.UtcNow,UpdatedAt=DateTime.UtcNow,DestinationBankAccountId=bank.Id,ProviderReferenceId="demo-reference"};
        db.Refunds.Add(refund);money.Booking.Status=BookingStatus.PendingPayment;money.Booking.PaymentExpiresAt=DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();var providerCalls=0;var provider=ReviewTestProxy.Make<IRefundPayoutProvider>((_,_)=>{providerCalls++;throw new Exception("Unexpected real refund");});
        var service=new RefundService(db,new MuaReceivableService(db),provider,Config,NullLogger<RefundService>.Instance);
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>service.GetAdminByIdAsync(refund.RefundId));
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>service.StartProcessingAsync(refund.RefundId,Guid.NewGuid(),null));
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>service.CompleteAsync(refund.RefundId,Guid.NewGuid(),"reference"));
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>new AdminBankAccountController(db).Approve(bank.Id,new()));
        Assert.Empty(await service.GetAdminQueueAsync());Assert.Equal(0,await service.MovePendingToManualActionRequiredAsync());Assert.Equal(0,providerCalls);
        var boundary=new PayOsRefundPayoutProvider(Config,db);
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>boundary.CreateAsync(new("demo-reference",30_000,"test",null!,null!),"test"));
        Assert.Equal(0,await Bookings(db).ExpirePendingPaymentsAsync());Assert.Equal(BookingStatus.PendingPayment,money.Booking.Status);
        money.Booking.Status=BookingStatus.WaitingCustomer;money.Booking.CustomerConfirmationDeadline=DateTime.UtcNow.AddMinutes(-1);
        db.MuaReceivables.Add(new(){Id=Guid.NewGuid(),BookingId=money.Booking.BookingId,MuaId=ids.Mua.UserId,NetAmount=22_000,Status=MuaReceivableStatus.OnHold});await db.SaveChangesAsync();
        Assert.Equal(0,await Bookings(db).AutoCompleteOverdueAsync());Assert.Equal(0,await new MuaReceivableService(db).ReconcileStatesAsync());
        Assert.Equal(MuaReceivableStatus.OnHold,(await db.MuaReceivables.SingleAsync()).Status);
    }

    [PostgreSqlFact]
    public async Task Simulated_and_mixed_payouts_never_enter_admin_queue_transfer_or_reconciliation()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();await using var db=database.CreateContext();var normal=await Seed(db,false,false);var demo=await Seed(db,true,true);
        var n=await Money(db,normal.Customer,normal.Mua,false);var d=await Money(db,demo.Customer,demo.Mua,true);var bank=Bank(db,normal.Mua.UserId);
        var rows=new[]{new MuaReceivable{Id=Guid.NewGuid(),BookingId=n.Booking.BookingId,MuaId=normal.Mua.UserId,NetAmount=22_000,Status=MuaReceivableStatus.PayoutPending},
            new MuaReceivable{Id=Guid.NewGuid(),BookingId=d.Booking.BookingId,MuaId=demo.Mua.UserId,NetAmount=22_000,Status=MuaReceivableStatus.PayoutPending}};
        db.MuaReceivables.AddRange(rows);var payouts=new[]{
            new Payout{Id=Guid.NewGuid(),MuaId=normal.Mua.UserId,RequestedBy=normal.Mua.UserId,BankAccountId=bank.Id,Provider=PayoutProvider.Simulated,Status=PayoutStatus.Pending,IdempotencyKey="simulated"},
            new Payout{Id=Guid.NewGuid(),MuaId=normal.Mua.UserId,RequestedBy=normal.Mua.UserId,BankAccountId=bank.Id,Provider=PayoutProvider.Manual,Status=PayoutStatus.Pending,IdempotencyKey="mixed"}};
        foreach(var p in payouts) { p.Items.Add(new(){Id=Guid.NewGuid(),MuaReceivableId=rows[0].Id,IsActive=false});p.Items.Add(new(){Id=Guid.NewGuid(),MuaReceivableId=rows[1].Id,IsActive=false}); }
        db.Payouts.AddRange(payouts);await db.SaveChangesAsync();var service=new PayoutService(db,Eligible());
        foreach(var p in payouts)
        {
            await Assert.ThrowsAsync<PlayReviewOperationException>(()=>service.GetAdminByIdAsync(p.Id));
            await Assert.ThrowsAsync<PlayReviewOperationException>(()=>service.StartProcessingAsync(p.Id,Guid.NewGuid(),null));
        }
        Assert.Empty(await service.GetPendingAdminAsync());Assert.Equal(0,await service.MovePendingToManualActionRequiredAsync());
        Assert.All(payouts,p=>Assert.Equal(PayoutStatus.Pending,p.Status));
    }

    [Fact]
    public async Task Demo_availability_view_is_read_only_and_normal_view_keeps_existing_evaluation()
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var ids=await Seed(db,true,false);
        var context=new DefaultHttpContext();context.User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,ids.Customer.UserId.ToString())],"test"));
        bool? update=null;var service=Bookings(db,eligibility:Eligible(x=>update=x),accessor:new HttpContextAccessor{HttpContext=context});
        await service.GetAvailableSlotsAsync(ids.Mua.UserId,DateTime.UtcNow.AddDays(2),60);Assert.False(update);
        context.User=new System.Security.Claims.ClaimsPrincipal();await service.GetAvailableSlotsAsync(ids.Mua.UserId,DateTime.UtcNow.AddDays(2),60);Assert.True(update);
    }

    [Fact]
    public async Task Persisted_chat_mixed_domains_block_message_reaction_and_demo_same_domain_is_safe()
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;
        foreach(var pair in new[]{(true,false),(false,true),(true,true)})
        {
            var ids=await Seed(db,pair.Item1,pair.Item2);var room=new ChatRoom{ChatRoomId=Guid.NewGuid(),CustomerId=ids.Customer.UserId,MUAId=ids.Mua.UserId};db.ChatRooms.Add(room);await db.SaveChangesAsync();
            var chat=new ChatService(new ChatRepository(db),db,new ChatNotificationService(db),NullLogger<ChatService>.Instance);
            if(pair.Item1!=pair.Item2)
            {
                await Assert.ThrowsAsync<PlayReviewOperationException>(()=>chat.SendMessageAsync(room.ChatRoomId,ids.Customer.UserId,"test",null,null));
                await Assert.ThrowsAsync<PlayReviewOperationException>(()=>chat.ToggleReactionAsync(room.ChatRoomId,Guid.NewGuid(),ids.Customer.UserId,"👍"));
            }
            else
            {
                Assert.NotNull(await chat.SendMessageAsync(room.ChatRoomId,ids.Customer.UserId,"test",null,null));
                Assert.All(await db.AppNotifications.ToListAsync(),x=>Assert.Equal("Skipped",x.Status));
            }
        }
    }

    [Fact]
    public async Task Demo_style_creation_is_blocked_but_existing_catalog_selection_is_read_only()
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var ids=await Seed(db,true,true);db.MakeupStyles.Add(new(){Name="Existing",IsActive=true});await db.SaveChangesAsync();
        var context=new DefaultHttpContext();context.User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,ids.Customer.UserId.ToString())],"test"));
        var mua=new MuaService(new MuaRepository(db),new UserRepository(db),new UnitOfWork(db),db,Eligible(),new HttpContextAccessor{HttpContext=context});
        Assert.NotNull(await mua.CreateStyleAsync(new(){Name="Existing"}));await Assert.ThrowsAsync<PlayReviewOperationException>(()=>mua.CreateStyleAsync(new(){Name="New style"}));
        Assert.Single(await db.MakeupStyles.ToListAsync());
    }

    [Fact]
    public void Public_request_DTOs_do_not_bind_demo_markers_and_provider_values_remain_append_only()
    {
        // Response-only markers are additive in Phase 3; no public write DTO may bind them.
        var dtoTypes=typeof(BookingCreateDto).Assembly.GetTypes().Where(x=>x.Namespace?.StartsWith("BeautyBookBackend.DTOs")==true
            && x != typeof(UserDto) && x != typeof(UserProfileDto) && x != typeof(TokenDto));
        Assert.All(dtoTypes,t=>{Assert.Null(t.GetProperty("IsDemoAccount"));Assert.Null(t.GetProperty("IsDemo"));});
        Assert.Equal(0,(byte)PaymentProvider.PayOS);Assert.Equal(1,(byte)PaymentProvider.Simulated);
        Assert.Equal(0,(byte)PayoutProvider.Manual);Assert.Equal(1,(byte)PayoutProvider.PayOS);Assert.Equal(2,(byte)PayoutProvider.Simulated);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Eligibility_normal_publishes_existing_complete_profile_demo_never_auto_lists(bool demo)
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var ids=await Seed(db,demo,demo);
        ids.Mua.FullName="Test artist";ids.Mua.AvatarUrl="https://test.invalid/avatar.jpg";
        var profile=await db.MakeupArtistProfiles.SingleAsync();profile.Status=MuaStatus.Draft;profile.City="Test city";profile.Specialization="Test style";profile.ProfileQualityScore=7;
        var refs=new List<string>();foreach(var purpose in new[]{"identity-front","identity-back","portrait"})
        {
            var media=new VerificationMedia{Id=Guid.NewGuid(),OwnerId=ids.Mua.UserId,Purpose=purpose,ReadyAt=DateTime.UtcNow,ObjectKey=Guid.NewGuid().ToString(),Sha256="test",StorageLocationId="test"};
            db.VerificationMedia.Add(media);refs.Add(VerificationMediaService.Reference(media.Id));
        }
        profile.IdentityFrontUrl=refs[0];profile.IdentityBackUrl=refs[1];profile.PortraitUrl=refs[2];Bank(db,ids.Mua.UserId);
        db.Portfolios.Add(new(){PortfolioId=Guid.NewGuid(),MUAId=ids.Mua.UserId,ImageUrls=["https://test.invalid/a.jpg","https://test.invalid/b.jpg","https://test.invalid/c.jpg"]});await db.SaveChangesAsync();
        var service=new MuaEligibilityService(db,Schedule,new VerificationMediaService(db,ReviewTestProxy.Make<IVerificationStorage>((_,_)=>throw new Exception("Unexpected storage"))));
        var result=await service.EvaluateAsync(ids.Mua.UserId);Assert.NotNull(result);Assert.Equal(demo?MuaStatus.Draft:MuaStatus.Listed,profile.Status);
        Assert.Equal(demo?7:100,profile.ProfileQualityScore);Assert.Equal(!demo,result.CanPublishProfile);
    }

    [Fact]
    public async Task Normal_push_delivery_and_start_transition_keep_existing_behavior_with_fake_HTTP()
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var ids=await Seed(db,false,false);var money=await Money(db,ids.Customer,ids.Mua,false);
        money.Booking.Status=BookingStatus.Approved;money.Booking.BookingDate=DateTime.UtcNow.Date.AddDays(-1);
        await new BookingNotificationService(db,new BookingTimeService(TimeZoneInfo.Utc)).QueueBookingStatusAsync(money.Booking,BookingStatus.Approved,ids.Mua.UserId);
        db.DevicePushTokens.Add(new(){Id=Guid.NewGuid(),UserId=ids.Customer.UserId,ExpoPushToken="ExponentPushToken[test]",IsActive=true,Platform="android"});await db.SaveChangesAsync();
        var http=new CountingHttp();using var services=new ServiceCollection().AddSingleton(db).AddSingleton(new BookingTimeService(TimeZoneInfo.Utc)).BuildServiceProvider();
        var factory=ReviewTestProxy.Make<IHttpClientFactory>((_,_)=>new HttpClient(http){BaseAddress=new Uri("https://test.invalid/")});
        var worker=new PushNotificationWorker(services.GetRequiredService<IServiceScopeFactory>(),factory,NullLogger<PushNotificationWorker>.Instance);
        await (Task)typeof(PushNotificationWorker).GetMethod("ProcessAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(worker,[CancellationToken.None])!;
        Assert.Equal(1,http.Calls);Assert.Equal("Sent",(await db.AppNotifications.SingleAsync()).Status);Assert.Equal(BookingStatus.InProgress,money.Booking.Status);
    }

    [Fact]
    public async Task Reply_review_rejects_both_mixed_directions_and_inconsistent_review_relations()
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;
        foreach(var pair in new[]{(true,false),(false,true)})
        {
            var ids=await Seed(db,pair.Item1,pair.Item2);var b=new Booking{BookingId=Guid.NewGuid(),CustomerId=ids.Customer.UserId,MUAId=ids.Mua.UserId,IsDemo=pair.Item1};db.Bookings.Add(b);
            var review=new Review{ReviewId=Guid.NewGuid(),BookingId=b.BookingId,CustomerId=ids.Customer.UserId,MUAId=ids.Mua.UserId,Rating=5};db.Reviews.Add(review);await db.SaveChangesAsync();
            await Assert.ThrowsAsync<PlayReviewOperationException>(()=>Bookings(db).ReplyReviewAsync(review.ReviewId,ids.Mua.UserId,"test"));Assert.Null(review.MuaReply);
        }
    }

    [PostgreSqlFact]
    public async Task Additive_migration_defaults_existing_users_and_bookings_to_normal()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();await using var db=database.CreateContext();var ids=await Seed(db,false,false);var money=await Money(db,ids.Customer,ids.Mua,false);
        // Reproduce pre-marker rows in this disposable database, then apply only the additive migration.
        await db.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261003010637_SharedReceivingAccountReview");
        await db.Database.MigrateAsync();db.ChangeTracker.Clear();Assert.All(await db.Users.ToListAsync(),x=>Assert.False(x.IsDemoAccount));
        Assert.False((await db.Bookings.SingleAsync()).IsDemo);
    }

    [Theory] [InlineData(true,false)] [InlineData(false,true)]
    public async Task Push_token_cannot_be_reassigned_across_domains(bool actorDemo,bool ownerDemo)
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var ids=await Seed(db,actorDemo,ownerDemo);
        var token=new DevicePushToken{Id=Guid.NewGuid(),UserId=ids.Mua.UserId,ExpoPushToken="ExponentPushToken[owned]",Platform="android",IsActive=true};
        db.DevicePushTokens.Add(token);await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>new BookingNotificationService(db,new BookingTimeService(TimeZoneInfo.Utc))
            .RegisterDeviceAsync(ids.Customer.UserId,token.ExpoPushToken,"android",null));
        Assert.Equal(ids.Mua.UserId,(await db.DevicePushTokens.SingleAsync()).UserId);
    }

    [Theory] [InlineData("REGISTER")] [InlineData("RESET_PASSWORD")]
    public async Task Authenticated_demo_cannot_send_email_to_a_normal_or_unregistered_recipient(string purpose)
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var ids=await Seed(db,true,false);
        var context=new DefaultHttpContext();context.User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,ids.Customer.UserId.ToString())],"test"));
        var accessor=new HttpContextAccessor{HttpContext=context};var recipient=purpose=="REGISTER"?"unregistered@example.test":ids.Mua.Email!;var calls=0;
        var sender=ReviewTestProxy.Make<IEmailSender>((_,_)=>{calls++;return Task.CompletedTask;});
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>new EmailOtpService(db,sender,Config,accessor).IssueAsync(recipient,purpose));
        var http=new CountingHttp();await Assert.ThrowsAsync<PlayReviewOperationException>(()=>new BrevoEmailSender(new HttpClient(http),Config,NullLogger<BrevoEmailSender>.Instance,db,accessor)
            .SendOtpAsync(recipient,"123456",purpose));
        Assert.Equal(0,calls);Assert.Equal(0,http.Calls);Assert.Empty(await db.EmailOtps.ToListAsync());
    }

    [Fact]
    public async Task PayOS_boundary_rejects_authenticated_demo_actor_even_for_normal_persisted_payment()
    {
        await using var store=await PlayReviewTestStore.CreateAsync();var db=store.Db;var normal=await Seed(db,false,false);var demo=await Seed(db,true,true);
        var money=await Money(db,normal.Customer,normal.Mua,false);var context=new DefaultHttpContext();
        context.User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,demo.Customer.UserId.ToString())],"test"));
        var http=new CountingHttp();var pay=new PayOsService(new HttpClient(http),Config,db,new HttpContextAccessor{HttpContext=context});
        await Assert.ThrowsAsync<PlayReviewOperationException>(()=>pay.CreatePaymentLinkAsync(new(){OrderCode=money.Payment.ProviderOrderCode,Amount=30_000}));Assert.Equal(0,http.Calls);
    }
}
