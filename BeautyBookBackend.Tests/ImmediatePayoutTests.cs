using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

    [PostgreSqlFact]
    public async Task Old_guard_reproduces_Render_error_and_upgrade_preserves_live_mua_payout()
    {
        await using var database=await PostgreSqlDatabase.CreateAsync();
        await using(var original=database.CreateContext()) {
            // Current model needs the additive QR columns. Reinstall the exact old
            // guard function to reproduce Render's 23514, without an obsolete EF schema.
            await original.Database.MigrateAsync();
            var oldSql = new BeautyBookBackend.Migrations.AddAccountDeletionStorageLifecycle().UpOperations
                .OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().Single(x=>x.Sql.Contains("CREATE FUNCTION public.account_deletion_write_guard"));
            var function = System.Text.RegularExpressions.Regex.Match(oldSql.Sql, @"CREATE FUNCTION public\.account_deletion_write_guard\(\).*?END \$guard\$;", System.Text.RegularExpressions.RegexOptions.Singleline).Value;
            Assert.NotEmpty(function);
            await ExecuteGuardSqlAsync(original,function.Replace("CREATE FUNCTION", "CREATE OR REPLACE FUNCTION"));
        }
        var ids=await Seed(database);
        await using(var db=database.CreateContext()) {
            await BookingServiceFor(db).UpdateBookingStatusAsync(ids.Booking,ids.Customer,BookingStatus.Completed);
            var r=await db.MuaReceivables.SingleAsync();r.Status=MuaReceivableStatus.OnHold;r.AvailableAt=DateTime.UtcNow.AddHours(48);await db.SaveChangesAsync();
            await Tombstone(db,ids.Customer);
            var error=await Assert.ThrowsAsync<DbUpdateException>(()=>new MuaReceivableService(db).ReconcileStatesAsync());
            Assert.Equal("23514",Assert.IsType<Npgsql.PostgresException>(error.InnerException).SqlState);
        }
        await using(var upgrade=database.CreateContext())
            foreach(var operation in new BeautyBookBackend.Migrations.AllowRetainedReceivableLifecycleAfterCustomerDeletion().UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
                await ExecuteGuardSqlAsync(upgrade,operation.Sql);
        await using var check=database.CreateContext();Assert.Equal(1,await new MuaReceivableService(check).ReconcileStatesAsync());
        Assert.Equal(6600m,(await check.MuaReceivables.SingleAsync()).NetAmount);
        var service=new PayoutService(check,new EligibleMua());var p=await service.CreateAsync(ids.Mua,new(){BankAccountId=ids.Bank,IdempotencyKey="retained-ledger"});
        Assert.NotNull(await service.StartProcessingAsync(p.Id,ids.Admin,null));Assert.NotNull(await service.CompleteAsync(p.Id,ids.Admin,"verified-local-transfer"));
        Assert.Equal(MuaReceivableStatus.PaidOut,(await check.MuaReceivables.SingleAsync()).Status);
        Assert.NotNull((await check.Users.SingleAsync(u=>u.UserId==ids.Customer)).DeletedAt);
    }

    [PostgreSqlFact]
    public async Task Deleted_mua_is_skipped_and_does_not_block_other_receivables()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var retired=await Seed(database);var live=await Seed(database);
        await using var db=database.CreateContext();
        foreach(var ids in new[]{retired,live}) {
            await BookingServiceFor(db).UpdateBookingStatusAsync(ids.Booking,ids.Customer,BookingStatus.Completed);
            var r=await db.MuaReceivables.SingleAsync(x=>x.BookingId==ids.Booking);r.Status=MuaReceivableStatus.OnHold;await db.SaveChangesAsync();
        }
        await Tombstone(db,retired.Mua);
        Assert.Equal(1,await new MuaReceivableService(db).ReconcileStatesAsync());
        Assert.Equal(MuaReceivableStatus.OnHold,(await db.MuaReceivables.SingleAsync(x=>x.BookingId==retired.Booking)).Status);
        Assert.Equal(MuaReceivableStatus.Available,(await db.MuaReceivables.SingleAsync(x=>x.BookingId==live.Booking)).Status);
        Assert.Equal(0,await new MuaReceivableService(db).ReconcileStatesAsync());
    }

    [PostgreSqlFact]
    public async Task Retained_ledger_exception_cannot_change_amounts_references_restore_user_or_reopen_paid_money()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var ids=await Seed(database);
        await using var db=database.CreateContext();await BookingServiceFor(db).UpdateBookingStatusAsync(ids.Booking,ids.Customer,BookingStatus.Completed);await Tombstone(db,ids.Customer);
        var rid=(await db.MuaReceivables.SingleAsync()).Id;
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MuaReceivables\" SET \"NetAmount\"=999999 WHERE \"Id\"={rid}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MuaReceivables\" SET \"MuaId\"={ids.Customer} WHERE \"Id\"={rid}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"AvatarUrl\"={"https://example.test/restored.jpg"} WHERE \"UserId\"={ids.Customer}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Bookings\" SET \"Notes\"={"new personal data"} WHERE \"BookingId\"={ids.Booking}"))).SqlState);
        var now=DateTime.UtcNow;var newId=Guid.NewGuid();
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"MuaReceivables\" (\"Id\",\"BookingId\",\"MuaId\",\"GrossAmount\",\"PlatformFeeAmount\",\"NetAmount\",\"Status\",\"CreatedAt\",\"UpdatedAt\") VALUES ({newId},{ids.Booking},{ids.Mua},9000,2400,6600,1,{now},{now})"))).SqlState);
        var service=new PayoutService(db,new EligibleMua());var p=await service.CreateAsync(ids.Mua,new(){BankAccountId=ids.Bank,IdempotencyKey="terminal"});await service.StartProcessingAsync(p.Id,ids.Admin,null);await service.CompleteAsync(p.Id,ids.Admin,"local-only");
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MuaReceivables\" SET \"Status\"=1 WHERE \"Id\"={rid}"))).SqlState);
    }

    private static async Task ExecuteGuardSqlAsync(ApplicationDbContext db,string sql) {
        await db.Database.OpenConnectionAsync();
        try { await using var command=db.Database.GetDbConnection().CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync(); }
        finally { await db.Database.CloseConnectionAsync(); }
    }
    private static Task<int> Tombstone(ApplicationDbContext db,Guid id) => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"DeletedAt\"={DateTime.UtcNow},\"IsActive\"=FALSE,\"Email\"=NULL,\"FullName\"=NULL,\"AvatarUrl\"=NULL,\"PhoneNumber\"=NULL WHERE \"UserId\"={id}");

    private static BeautyBookBackend.Services.BookingService BookingServiceFor(ApplicationDbContext db){var config=new ConfigurationBuilder().Build();var time=new BookingTimeService(config);return new(new BookingRepository(db),new MuaRepository(db),new ReviewRepository(db),new UnitOfWork(db),new BookingNotificationService(db,time),db,null!,new RefundService(db,new MuaReceivableService(db),null!,config,Microsoft.Extensions.Logging.Abstractions.NullLogger<RefundService>.Instance),null!,new MuaReceivableService(db),config,null!,null!,time);}
    private record Ids(Guid Booking,Guid Customer,Guid Mua,Guid Bank,Guid Admin);
    private static async Task<Ids> Seed(PostgreSqlDatabase database){var ids=new Ids(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());var now=DateTime.UtcNow;await using var db=database.CreateContext();db.Users.AddRange(new User{UserId=ids.Customer,Role=UserRole.Customer,Email=ids.Customer+"@example.test",IsActive=true,CreatedAt=now},new User{UserId=ids.Mua,Role=UserRole.MUA,Email=ids.Mua+"@example.test",IsActive=true,CreatedAt=now},new User{UserId=ids.Admin,Role=UserRole.Admin,Email=ids.Admin+"@example.test",IsActive=true,CreatedAt=now});db.MakeupArtistProfiles.Add(new(){MUAId=ids.Mua});db.Bookings.Add(new(){BookingId=ids.Booking,CustomerId=ids.Customer,MUAId=ids.Mua,TotalAmount=30000,DepositAmount=9000,PlatformFeeAmount=2400,MuaPayoutAmount=6600,RemainingAmount=21000,Status=BookingStatus.WaitingCustomer,PaymentStatus=PaymentStatus.DepositHeld,BookingDate=now,StartTime=TimeSpan.FromHours(8),EndTime=TimeSpan.FromHours(9),WaitingCustomerAt=now.AddHours(-25),CustomerConfirmationDeadline=now.AddHours(-1),CreatedAt=now,UpdatedAt=now});db.BookingPayments.Add(new(){PaymentId=Guid.NewGuid(),BookingId=ids.Booking,CustomerId=ids.Customer,ProviderOrderCode=DateTime.UtcNow.Ticks,Amount=9000,Status=BookingPaymentStatus.Paid,PaidAt=now,ExpiresAt=now.AddHours(1),CreatedAt=now,UpdatedAt=now});db.BankAccounts.Add(new(){Id=ids.Bank,UserId=ids.Mua,BankCode="VCB",BankBin="970436",BankName="VCB",CanonicalBankKey="BIN:970436",AccountNumber="123456",NormalizedAccountNumber="123456",AccountHolderName="MUA",Method="BANK",VerificationStatus="APPROVED",IsActive=true,ActivatedAt=now,CreatedAt=now,UpdatedAt=now});await db.SaveChangesAsync();return ids;}
    private sealed class EligibleMua:IMuaEligibilityService
    {
        public Task<MuaIdentityVerificationRequestDto?> GetIdentityVerificationAsync(Guid muaId)=>Task.FromResult<MuaIdentityVerificationRequestDto?>(null);
        public Task<MuaEligibilityDto?> EvaluateAsync(Guid muaId,bool updateStatus=true)=>Task.FromResult<MuaEligibilityDto?>(new MuaEligibilityDto{CanWithdraw=true});
        public Task<bool> SetSuspendedAsync(Guid muaId,bool suspended)=>throw new NotSupportedException();public Task<bool> SetAccountActiveAsync(Guid userId,bool isActive)=>throw new NotSupportedException();public Task<(bool Success,string? Error)> SubmitForReviewAsync(Guid muaId)=>throw new NotSupportedException();public Task<(bool Success,string? Error)> UpdateIdentityVerificationAsync(Guid muaId,MuaIdentityVerificationRequestDto request)=>throw new NotSupportedException();public Task<bool> ReviewAsync(Guid muaId,Guid adminId,bool approved,string? reason=null,IReadOnlyList<string>? reasonCodes=null,IReadOnlyList<MuaApplicationRejectionItemDto>? items=null)=>throw new NotSupportedException();public Task<List<AdminMuaApplicationListItemDto>> GetApplicationsAsync(string? status,int page,int pageSize)=>throw new NotSupportedException();
    }

}
