using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BeautyBookBackend.Tests;

public sealed class ImmediatePayoutTests
{
    [Fact]
    public void Support_window_does_not_hold_completed_earnings()
    {
        var b=new Booking{Status=BookingStatus.Completed,CompletedAt=DateTime.UtcNow};
        Assert.True(ComplaintPolicy.CanCreate(b,DateTime.UtcNow));
        Assert.False(ComplaintPolicy.HasHold(b,DateTime.UtcNow));
        Assert.Equal(b.CompletedAt.Value.AddHours(48),ComplaintPolicy.Deadline(b));
    }

    [PostgreSqlFact]
    public async Task Customer_completion_opens_earnings_and_allows_immediate_idempotent_payout()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var ids=await Seed(database);
        await using var db=database.CreateContext();
        Assert.NotNull(await BookingServiceFor(db).UpdateBookingStatusAsync(ids.Booking,ids.Customer,BookingStatus.Completed));
        var r=await db.MuaReceivables.SingleAsync();var b=await db.Bookings.SingleAsync();
        Assert.Equal(MuaReceivableStatus.Available,r.Status);Assert.Equal(b.CompletedAt,r.AvailableAt);
        Assert.Equal(6600m,r.NetAmount);Assert.True(r.AvailableAt<=DateTime.UtcNow);
        var service=new PayoutService(db,new EligibleMua());var request=new CreatePayoutRequest{BankAccountId=ids.Bank,ReceivableIds=[r.Id],IdempotencyKey="same-request"};
        var first=await service.CreateAsync(ids.Mua,request);var retry=await service.CreateAsync(ids.Mua,request);
        Assert.Equal(first.Id,retry.Id);Assert.Single(await db.Payouts.ToListAsync());
        Assert.Equal(MuaReceivableStatus.PayoutPending,r.Status);
    }

    [PostgreSqlFact]
    public async Task Worker_completion_opens_earnings_without_another_wait()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var ids=await Seed(database);
        await using var db=database.CreateContext();Assert.Equal(1,await BookingServiceFor(db).AutoCompleteOverdueAsync());
        Assert.Equal(0,await BookingServiceFor(db).AutoCompleteOverdueAsync());
        var b=await db.Bookings.SingleAsync();var r=await db.MuaReceivables.SingleAsync();
        Assert.Equal(BookingStatus.AutoCompleted,b.Status);Assert.Equal(MuaReceivableStatus.Available,r.Status);Assert.Equal(b.CompletedAt,r.AvailableAt);
        Assert.NotNull(await new PayoutService(db,new EligibleMua()).CreateAsync(ids.Mua,new(){BankAccountId=ids.Bank,IdempotencyKey="worker-payout"}));
    }

    [PostgreSqlFact]
    public async Task Open_complaint_blocks_customer_and_worker_completion()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var ids=await Seed(database);
        await using var db=database.CreateContext();db.BookingComplaints.Add(new(){Id=Guid.NewGuid(),BookingId=ids.Booking,IsOpen=true,CreatedAt=DateTime.UtcNow,UpdatedAt=DateTime.UtcNow});await db.SaveChangesAsync();
        Assert.Equal("COMPLAINT_OPEN",(await Assert.ThrowsAsync<BookingRuleException>(()=>BookingServiceFor(db).UpdateBookingStatusAsync(ids.Booking,ids.Customer,BookingStatus.Completed))).Code);
        Assert.Equal(0,await BookingServiceFor(db).AutoCompleteOverdueAsync());Assert.Empty(await db.MuaReceivables.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task Legacy_hold_opens_but_pending_paid_and_frozen_obligations_stay_safe()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var ids=await Seed(database);
        await using var db=database.CreateContext();await BookingServiceFor(db).UpdateBookingStatusAsync(ids.Booking,ids.Customer,BookingStatus.Completed);
        var r=await db.MuaReceivables.SingleAsync();r.Status=MuaReceivableStatus.OnHold;r.AvailableAt=DateTime.UtcNow.AddHours(48);await db.SaveChangesAsync();
        Assert.Equal(1,await new MuaReceivableService(db).ReconcileStatesAsync());Assert.Equal(MuaReceivableStatus.Available,r.Status);Assert.True(r.AvailableAt<=DateTime.UtcNow);
        db.BookingComplaints.Add(new(){Id=Guid.NewGuid(),BookingId=ids.Booking,IsOpen=true,CreatedAt=DateTime.UtcNow,UpdatedAt=DateTime.UtcNow});await db.SaveChangesAsync();
        await new MuaReceivableService(db).FreezeForDisputeAsync(ids.Booking);await db.SaveChangesAsync();
        Assert.Equal(0,await new MuaReceivableService(db).ReconcileStatesAsync());Assert.Equal(MuaReceivableStatus.Frozen,r.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new PayoutService(db,new EligibleMua()).CreateAsync(ids.Mua,new(){BankAccountId=ids.Bank,IdempotencyKey="blocked"}));
        foreach(var status in new[]{MuaReceivableStatus.PayoutPending,MuaReceivableStatus.PaidOut,MuaReceivableStatus.Reversed}){
            r.Status=status;await db.SaveChangesAsync();await new MuaReceivableService(db).ReconcileStatesAsync();Assert.Equal(status,r.Status);
        }
    }

    [PostgreSqlFact]
    public async Task Complaint_after_payout_claim_blocks_transfer_and_after_paid_never_reopens_funds()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var ids=await Seed(database);
        await using var db=database.CreateContext();await BookingServiceFor(db).UpdateBookingStatusAsync(ids.Booking,ids.Customer,BookingStatus.Completed);
        var payouts=new PayoutService(db,new EligibleMua());var p=await payouts.CreateAsync(ids.Mua,new(){BankAccountId=ids.Bank,IdempotencyKey="claim"});
        var complaint=new ComplaintService(db,new MuaReceivableService(db),null!);
        var c=await complaint.Create(ids.Booking,ids.Customer,new(){Category="Quality",Description="Dịch vụ cần được kiểm tra lại",RequestedOutcome="Support"});
        Assert.Null(await payouts.StartProcessingAsync(p.Id,ids.Admin,null));
        await complaint.Action(c,ids.Admin,new(){Action="Reject",Reason="Đã xác minh dịch vụ hoàn thành"});
        Assert.NotNull(await payouts.StartProcessingAsync(p.Id,ids.Admin,null));Assert.NotNull(await payouts.CompleteAsync(p.Id,ids.Admin,"manual-test-reference"));
        var later=await complaint.Create(ids.Booking,ids.Customer,new(){Category="Other",Description="Yêu cầu hỗ trợ sau khi chi trả",RequestedOutcome="Support"});
        Assert.Equal("FINANCIAL_RECONCILIATION_REQUIRED",(await Assert.ThrowsAsync<BookingRuleException>(()=>complaint.Action(later,ids.Admin,new(){Action="Refund",Reason="Cần đối soát khoản đã chuyển",RefundAmount=9000}))).Code);
        Assert.Equal(MuaReceivableStatus.PaidOut,(await db.MuaReceivables.SingleAsync()).Status);Assert.Empty(await db.Refunds.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task Concurrent_complaint_and_payout_cannot_transfer_disputed_money()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var ids=await Seed(database);
        await using(var db=database.CreateContext())await BookingServiceFor(db).UpdateBookingStatusAsync(ids.Booking,ids.Customer,BookingStatus.Completed);
        async Task Claim(){await using var db=database.CreateContext();try{await new PayoutService(db,new EligibleMua()).CreateAsync(ids.Mua,new(){BankAccountId=ids.Bank,IdempotencyKey="race"});}catch(InvalidOperationException){}}
        async Task Report(){await using var db=database.CreateContext();await new ComplaintService(db,new MuaReceivableService(db),null!).Create(ids.Booking,ids.Customer,new(){Category="Quality",Description="Báo vấn đề trong khi yêu cầu rút",RequestedOutcome="Support"});}
        await Task.WhenAll(Claim(),Report());
        await using var verify=database.CreateContext();Assert.Single(await verify.BookingComplaints.Where(x=>x.IsOpen).ToListAsync());
        var payout=await verify.Payouts.SingleOrDefaultAsync();if(payout!=null)Assert.Null(await new PayoutService(verify,new EligibleMua()).StartProcessingAsync(payout.Id,ids.Admin,null));
        else Assert.Equal(MuaReceivableStatus.Frozen,(await verify.MuaReceivables.SingleAsync()).Status);
        Assert.DoesNotContain(await verify.MuaReceivables.ToListAsync(),r=>r.Status==MuaReceivableStatus.PaidOut);
    }

    [PostgreSqlFact]
    public async Task Mua_cannot_self_confirm_and_unresolved_refund_blocks_completion()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var ids=await Seed(database);
        await using var db=database.CreateContext();
        Assert.Null(await BookingServiceFor(db).UpdateBookingStatusAsync(ids.Booking,ids.Mua,BookingStatus.Completed));
        db.Refunds.Add(new(){RefundId=Guid.NewGuid(),BookingId=ids.Booking,BookingPaymentId=(await db.BookingPayments.SingleAsync()).PaymentId,Amount=9000,Status=RefundStatus.AwaitingDestination,Reason="test",CreatedAt=DateTime.UtcNow,UpdatedAt=DateTime.UtcNow});await db.SaveChangesAsync();
        Assert.Null(await BookingServiceFor(db).UpdateBookingStatusAsync(ids.Booking,ids.Customer,BookingStatus.Completed));
        Assert.Equal(0,await BookingServiceFor(db).AutoCompleteOverdueAsync());
        Assert.Empty(await db.MuaReceivables.ToListAsync());Assert.Null((await db.Bookings.SingleAsync()).CompletedAt);
    }

    private static BeautyBookBackend.Services.BookingService BookingServiceFor(ApplicationDbContext db){var config=new ConfigurationBuilder().Build();var time=new BookingTimeService(config);return new(new BookingRepository(db),new MuaRepository(db),new ReviewRepository(db),new UnitOfWork(db),new BookingNotificationService(db,time),db,null!,new RefundService(db,new MuaReceivableService(db),null!,config,Microsoft.Extensions.Logging.Abstractions.NullLogger<RefundService>.Instance),null!,new MuaReceivableService(db),config,null!,null!,time);}
    private record Ids(Guid Booking,Guid Customer,Guid Mua,Guid Bank,Guid Admin);
    private static async Task<Ids> Seed(PostgreSqlDatabase database){var ids=new Ids(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());var now=DateTime.UtcNow;await using var db=database.CreateContext();db.Users.AddRange(new User{UserId=ids.Customer,Role=UserRole.Customer,Email="customer@example.test",IsActive=true,CreatedAt=now},new User{UserId=ids.Mua,Role=UserRole.MUA,Email="mua@example.test",IsActive=true,CreatedAt=now},new User{UserId=ids.Admin,Role=UserRole.Admin,Email="admin@example.test",IsActive=true,CreatedAt=now});db.MakeupArtistProfiles.Add(new(){MUAId=ids.Mua});db.Bookings.Add(new(){BookingId=ids.Booking,CustomerId=ids.Customer,MUAId=ids.Mua,TotalAmount=30000,DepositAmount=9000,PlatformFeeAmount=2400,MuaPayoutAmount=6600,RemainingAmount=21000,Status=BookingStatus.WaitingCustomer,PaymentStatus=PaymentStatus.DepositHeld,BookingDate=now,StartTime=TimeSpan.FromHours(8),EndTime=TimeSpan.FromHours(9),WaitingCustomerAt=now.AddHours(-25),CustomerConfirmationDeadline=now.AddHours(-1),CreatedAt=now,UpdatedAt=now});db.BookingPayments.Add(new(){PaymentId=Guid.NewGuid(),BookingId=ids.Booking,CustomerId=ids.Customer,ProviderOrderCode=DateTime.UtcNow.Ticks,Amount=9000,Status=BookingPaymentStatus.Paid,PaidAt=now,ExpiresAt=now.AddHours(1),CreatedAt=now,UpdatedAt=now});db.BankAccounts.Add(new(){Id=ids.Bank,UserId=ids.Mua,BankCode="VCB",BankBin="970436",BankName="VCB",CanonicalBankKey="BIN:970436",AccountNumber="123456",NormalizedAccountNumber="123456",AccountHolderName="MUA",Method="BANK",VerificationStatus="APPROVED",IsActive=true,ActivatedAt=now,CreatedAt=now,UpdatedAt=now});await db.SaveChangesAsync();return ids;}
    private sealed class EligibleMua:IMuaEligibilityService
    {
        public Task<MuaIdentityVerificationRequestDto?> GetIdentityVerificationAsync(Guid muaId)=>Task.FromResult<MuaIdentityVerificationRequestDto?>(null);
        public Task<MuaEligibilityDto?> EvaluateAsync(Guid muaId,bool updateStatus=true)=>Task.FromResult<MuaEligibilityDto?>(new MuaEligibilityDto{CanWithdraw=true});
        public Task<bool> SetSuspendedAsync(Guid muaId,bool suspended)=>throw new NotSupportedException();public Task<bool> SetAccountActiveAsync(Guid userId,bool isActive)=>throw new NotSupportedException();public Task<(bool Success,string? Error)> SubmitForReviewAsync(Guid muaId)=>throw new NotSupportedException();public Task<(bool Success,string? Error)> UpdateIdentityVerificationAsync(Guid muaId,MuaIdentityVerificationRequestDto request)=>throw new NotSupportedException();public Task<bool> ReviewAsync(Guid muaId,Guid adminId,bool approved,string? reason=null,IReadOnlyList<string>? reasonCodes=null,IReadOnlyList<MuaApplicationRejectionItemDto>? items=null)=>throw new NotSupportedException();public Task<List<AdminMuaApplicationListItemDto>> GetApplicationsAsync(string? status,int page,int pageSize)=>throw new NotSupportedException();
    }

}
