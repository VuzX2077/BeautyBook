using System.Security.Claims;
using System.Text.Json;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Xunit;

namespace BeautyBookBackend.Tests;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BEAUTYBOOK_TEST_POSTGRES")))
            Skip="Set BEAUTYBOOK_TEST_POSTGRES to a PostgreSQL server connection string with CREATE DATABASE permission.";
    }
}

public sealed class PostgreSqlBankFlowIntegrationTests
{
    [PostgreSqlFact]
    public async Task Clean_database_applies_all_migrations()
    {
        await using var database=await PostgreSqlDatabase.CreateAsync();
        await using var db=database.CreateContext();
        await db.Database.MigrateAsync();
        Assert.Contains("20260930051339_UnifyUserBankAccounts",await db.Database.GetAppliedMigrationsAsync());
    }

    [PostgreSqlFact]
    public async Task Legacy_customer_and_mua_accounts_are_merged_without_losing_uncertain_data()
    {
        await using var database=await PostgreSqlDatabase.CreateAsync();
        var owner=Guid.NewGuid();var customerBankId=Guid.NewGuid();var muaBankId=Guid.NewGuid();var unknownBankId=Guid.NewGuid();var now=DateTime.UtcNow;
        await using(var legacy=database.CreateContext())
        {
            await legacy.Database.GetService<IMigrator>().MigrateAsync("20260929201739_CleanupLegacyInvalidBankDefaults");
            legacy.Users.Add(User(owner,UserRole.MUA));legacy.MakeupArtistProfiles.Add(new MakeupArtistProfile{MUAId=owner});await legacy.SaveChangesAsync();
            await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "CustomerBankAccounts" ("Id","CustomerId","BankBin","BankName","AccountNumber","AccountHolderName","Method","QrCodeUrl","VerificationStatus","ActivatedAt","IsDefault","IsActive","ReviewedAt","ReviewedBy","CreatedAt","UpdatedAt")
                VALUES ({customerBankId},{owner},{"970436"},{"Vietcombank"},{" 123-456 "},{" Nguyen  Van A "},{"BANK"},NULL,{"APPROVED"},{now.AddDays(-2)},TRUE,TRUE,{now.AddDays(-3)},NULL,{now.AddDays(-5)},{now.AddDays(-2)});
                INSERT INTO "MuaBankAccounts" ("Id","MuaId","BankCode","BankName","AccountNumber","AccountHolderName","Method","QrCodeUrl","VerificationStatus","ActivatedAt","IsDefault","IsActive","ReviewedAt","ReviewedBy","CreatedAt","UpdatedAt")
                VALUES ({muaBankId},{owner},{"vcb"},{"VCB"},{"123456"},{"NGUYEN VAN A"},{"BANK"},NULL,{"PENDING_ADMIN"},NULL,FALSE,TRUE,NULL,NULL,{now.AddDays(-4)},{now.AddDays(-1)}),
                       ({unknownBankId},{owner},{"LEGACY"},{"Legacy bank"},{"999999"},{"NGUYEN VAN A"},{"BANK"},NULL,{"APPROVED"},{now.AddDays(-2)},FALSE,TRUE,{now.AddDays(-3)},NULL,{now.AddDays(-4)},{now.AddDays(-2)});
                """);
            await legacy.Database.MigrateAsync();
        }
        await using var verify=database.CreateContext();var rows=await verify.BankAccounts.AsNoTracking().Where(x=>x.UserId==owner).OrderBy(x=>x.AccountNumber).ToListAsync();
        Assert.Equal(2,rows.Count);
        var merged=Assert.Single(rows,x=>x.AccountNumber=="123456");Assert.Equal("VCB",merged.BankCode);Assert.Equal("970436",merged.BankBin);Assert.Equal(BankAccountEligibility.Pending,merged.VerificationStatus);Assert.Null(merged.ActivatedAt);Assert.False(merged.IsDefault);
        var uncertain=Assert.Single(rows,x=>x.AccountNumber=="999999");Assert.Equal("LEGACY",uncertain.BankCode);Assert.Equal("UNKNOWN",uncertain.BankBin);Assert.Equal(BankAccountEligibility.Pending,uncertain.VerificationStatus);
        var oldCustomer=await verify.Database.SqlQueryRaw<string?>("SELECT to_regclass('public.\"CustomerBankAccounts\"')::text AS \"Value\"").SingleAsync();
        var oldMua=await verify.Database.SqlQueryRaw<string?>("SELECT to_regclass('public.\"MuaBankAccounts\"')::text AS \"Value\"").SingleAsync();
        Assert.Null(oldCustomer);Assert.Null(oldMua);
    }

    [PostgreSqlFact]
    public async Task Concurrent_set_default_requests_leave_at_most_one_default()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();
        var owner=Guid.NewGuid();var first=Guid.NewGuid();var second=Guid.NewGuid();
        await SeedMuaAsync(database,owner,first,second);
        await using var db1=database.CreateContext();await using var db2=database.CreateContext();
        var service1=new BankAccountService(db1);var service2=new BankAccountService(db2);
        await Task.WhenAll(service1.SetDefaultAsync(owner,first),service2.SetDefaultAsync(owner,second));
        await using var verify=database.CreateContext();
        Assert.Single(await verify.BankAccounts.Where(x=>x.UserId==owner&&x.IsDefault&&x.IsActive).ToListAsync());
    }

    [PostgreSqlFact]
    public async Task Advisory_lock_serializes_the_same_owner()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();
        var key="user-bank-default:"+Guid.NewGuid().ToString("N");
        await using var first=new NpgsqlConnection(database.ConnectionString);await first.OpenAsync();await using var firstTx=await first.BeginTransactionAsync();
        await new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))",first,firstTx){Parameters={new("key",key)}}.ExecuteNonQueryAsync();
        await using var second=new NpgsqlConnection(database.ConnectionString);await second.OpenAsync();await using var secondTx=await second.BeginTransactionAsync();
        var acquired=(bool)(await new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(hashtextextended(@key, 0))",second,secondTx){Parameters={new("key",key)}}.ExecuteScalarAsync())!;
        Assert.False(acquired);
    }

    [PostgreSqlFact]
    public async Task Different_owners_do_not_share_locks()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();
        var muaKey="user-bank-default:"+Guid.NewGuid().ToString("N");var customerKey="user-bank-default:"+Guid.NewGuid().ToString("N");
        await using var first=new NpgsqlConnection(database.ConnectionString);await first.OpenAsync();await using var firstTx=await first.BeginTransactionAsync();
        await new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))",first,firstTx){Parameters={new("key",muaKey)}}.ExecuteNonQueryAsync();
        await using var second=new NpgsqlConnection(database.ConnectionString);await second.OpenAsync();await using var secondTx=await second.BeginTransactionAsync();
        var acquired=(bool)(await new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(hashtextextended(@key, 0))",second,secondTx){Parameters={new("key",customerKey)}}.ExecuteScalarAsync())!;
        Assert.True(acquired);
    }

    [PostgreSqlFact]
    public async Task Partial_unique_index_rejects_two_active_defaults()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var owner=Guid.NewGuid();
        await SeedMuaAsync(database,owner,Guid.NewGuid());
        await using var db=database.CreateContext();
        db.BankAccounts.Add(UsableBank(owner,Guid.NewGuid(),true,"222222"));
        await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task Concurrent_payouts_cannot_claim_the_same_receivable()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();
        var muaId=Guid.NewGuid();var customerId=Guid.NewGuid();var bankId=Guid.NewGuid();var bookingId=Guid.NewGuid();var receivableId=Guid.NewGuid();var now=DateTime.UtcNow;
        await using(var seed=database.CreateContext())
        {
            seed.Users.AddRange(User(muaId,UserRole.MUA),User(customerId,UserRole.Customer));seed.MakeupArtistProfiles.Add(new MakeupArtistProfile{MUAId=muaId});
            seed.Bookings.Add(new Booking{BookingId=bookingId,MUAId=muaId,CustomerId=customerId,Status=BookingStatus.Completed,PaymentStatus=PaymentStatus.Released,BookingDate=now,StartTime=TimeSpan.FromHours(8),EndTime=TimeSpan.FromHours(9),CreatedAt=now,UpdatedAt=now});
            seed.BankAccounts.Add(UsableBank(muaId,bankId,false,"111111"));
            seed.MuaReceivables.Add(new MuaReceivable{Id=receivableId,BookingId=bookingId,MuaId=muaId,GrossAmount=100000,NetAmount=90000,PlatformFeeAmount=10000,Status=MuaReceivableStatus.Available,CreatedAt=now,UpdatedAt=now});
            await seed.SaveChangesAsync();
        }
        async Task<bool> Run(string key){await using var db=database.CreateContext();try{await new PayoutService(db,new EligibleMua()).CreateAsync(muaId,new CreatePayoutRequest{BankAccountId=bankId,ReceivableIds=new[]{receivableId},IdempotencyKey=key});return true;}catch(InvalidOperationException){return false;}catch(DbUpdateException){return false;}}
        var results=await Task.WhenAll(Run("concurrent-a"),Run("concurrent-b"));
        Assert.Single(results,x=>x);
        await using var verify=database.CreateContext();Assert.Single(await verify.PayoutItems.Where(x=>x.MuaReceivableId==receivableId&&x.IsActive).ToListAsync());
        var payout=await verify.Payouts.SingleAsync();Assert.Equal(bankId,payout.BankAccountId);Assert.Equal("VCB",payout.BankCodeSnapshot);Assert.Equal("970436",payout.BankBinSnapshot);Assert.Equal("111111",payout.AccountNumberSnapshot);Assert.Equal("NGUYEN VAN A",payout.AccountHolderNameSnapshot);
    }

    [PostgreSqlFact]
    public async Task Customer_without_mua_profile_cannot_create_payout()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var customer=Guid.NewGuid();var bankId=Guid.NewGuid();
        await using(var seed=database.CreateContext()){seed.Users.Add(User(customer,UserRole.Customer));seed.BankAccounts.Add(UsableBank(customer,bankId,false,"111111"));await seed.SaveChangesAsync();}
        await using var db=database.CreateContext();var service=new PayoutService(db,new MuaEligibilityService(db,new AlwaysAvailableSchedule()));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.CreateAsync(customer,new CreatePayoutRequest{BankAccountId=bankId,IdempotencyKey="customer-cannot-payout"}));
        Assert.Empty(await db.Payouts.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task Admin_pending_hides_role_and_approval_starts_24_hour_cooldown()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var owner=Guid.NewGuid();var admin=Guid.NewGuid();var bankId=Guid.NewGuid();
        await using(var seed=database.CreateContext()){seed.Users.AddRange(User(owner,UserRole.Customer),User(admin,UserRole.Admin));seed.MakeupArtistProfiles.Add(new MakeupArtistProfile{MUAId=owner});var bank=UsableBank(owner,bankId,false,"111111");bank.VerificationStatus=BankAccountEligibility.Pending;bank.ActivatedAt=null;seed.BankAccounts.Add(bank);await seed.SaveChangesAsync();}
        await using var db=database.CreateContext();var controller=new AdminBankAccountController(db){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext{User=Principal(admin,UserRole.Admin)}}};
        var pending=Assert.IsType<OkObjectResult>(await controller.Pending());using var json=JsonDocument.Parse(JsonSerializer.Serialize(pending.Value));var item=Assert.Single(json.RootElement.EnumerateArray());Assert.Equal(owner,item.GetProperty("OwnerId").GetGuid());Assert.False(item.TryGetProperty("OwnerRole",out _));Assert.False(item.TryGetProperty("HasMuaProfile",out _));
        var before=DateTime.UtcNow;Assert.IsType<OkObjectResult>(await controller.Approve(bankId));db.ChangeTracker.Clear();var approved=await db.BankAccounts.SingleAsync(x=>x.Id==bankId);Assert.Equal(BankAccountEligibility.Approved,approved.VerificationStatus);Assert.False(approved.IsDefault);Assert.InRange(approved.ActivatedAt!.Value,before.AddHours(24),DateTime.UtcNow.AddHours(24).AddSeconds(2));Assert.Equal(admin,approved.ReviewedBy);
    }

    [PostgreSqlFact]
    public async Task Refund_destination_captures_the_shared_bank_snapshot()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var ids=await SeedRefundAsync(database,includeSource:false);
        await using(var db=database.CreateContext()){var result=await RefundServiceFor(db).SetDestinationAsync(ids.RefundId,ids.CustomerId,ids.BankId);Assert.NotNull(result);}
        await using var verify=database.CreateContext();var refund=await verify.Refunds.SingleAsync(x=>x.RefundId==ids.RefundId);Assert.Equal(RefundStatus.Pending,refund.Status);Assert.Equal(ids.BankId,refund.DestinationBankAccountId);Assert.Equal("VCB",refund.DestinationBankCode);Assert.Equal("970436",refund.DestinationBankBin);Assert.Equal("111111",refund.DestinationAccountNumber);Assert.Equal("NGUYEN VAN A",refund.DestinationAccountName);
    }

    [PostgreSqlFact]
    public async Task Refund_with_changed_snapshot_returns_to_awaiting_destination()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();
        var ids=await SeedRefundAsync(database,includeSource:true);
        await using(var mutate=database.CreateContext()){var bank=await mutate.BankAccounts.FindAsync(ids.BankId);bank!.AccountNumber="999999";bank.NormalizedAccountNumber="999999";await mutate.SaveChangesAsync();}
        await using(var db=database.CreateContext()){var service=RefundServiceFor(db);Assert.Null(await service.StartProcessingAsync(ids.RefundId,Guid.NewGuid(),null));}
        await using var verify=database.CreateContext();var refund=await verify.Refunds.FindAsync(ids.RefundId);Assert.Equal(RefundStatus.AwaitingDestination,refund!.Status);Assert.Null(refund.DestinationBankAccountId);
    }

    [PostgreSqlFact]
    public async Task Legacy_refund_without_source_account_cannot_start_processing()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();
        var ids=await SeedRefundAsync(database,includeSource:false);
        await using(var db=database.CreateContext()){var service=RefundServiceFor(db);Assert.Null(await service.StartProcessingAsync(ids.RefundId,Guid.NewGuid(),null));}
        await using var verify=database.CreateContext();var refund=await verify.Refunds.FindAsync(ids.RefundId);Assert.Equal(RefundStatus.AwaitingDestination,refund!.Status);Assert.Null(refund.DestinationBankAccountId);
    }

    private static async Task SeedMuaAsync(PostgreSqlDatabase database,Guid owner,params Guid[] accounts)
    {
        await using var db=database.CreateContext();db.Users.Add(User(owner,UserRole.MUA));db.MakeupArtistProfiles.Add(new MakeupArtistProfile{MUAId=owner});
        for(var index=0;index<accounts.Length;index++)db.BankAccounts.Add(UsableBank(owner,accounts[index],index==0,"11111"+index));
        await db.SaveChangesAsync();
    }

    private static async Task<(Guid RefundId,Guid BankId,Guid CustomerId)> SeedRefundAsync(PostgreSqlDatabase database,bool includeSource)
    {
        var customerId=Guid.NewGuid();var muaId=Guid.NewGuid();var bookingId=Guid.NewGuid();var paymentId=Guid.NewGuid();var bankId=Guid.NewGuid();var refundId=Guid.NewGuid();var now=DateTime.UtcNow;
        await using var db=database.CreateContext();db.Users.AddRange(User(customerId,UserRole.Customer),User(muaId,UserRole.MUA));db.MakeupArtistProfiles.Add(new MakeupArtistProfile{MUAId=muaId});
        db.Bookings.Add(new Booking{BookingId=bookingId,CustomerId=customerId,MUAId=muaId,Status=BookingStatus.Cancelled,PaymentStatus=PaymentStatus.RefundPending,BookingDate=now,StartTime=TimeSpan.FromHours(8),EndTime=TimeSpan.FromHours(9),CreatedAt=now,UpdatedAt=now});
        db.BookingPayments.Add(new BookingPayment{PaymentId=paymentId,BookingId=bookingId,CustomerId=customerId,ProviderOrderCode=Math.Abs(DateTime.UtcNow.Ticks),Amount=100000,Status=BookingPaymentStatus.RefundPending,ExpiresAt=now.AddHours(1),CreatedAt=now,UpdatedAt=now});
        var bank=UsableBank(customerId,bankId,false,"111111");db.BankAccounts.Add(bank);
        db.Refunds.Add(new Refund{RefundId=refundId,BookingId=bookingId,BookingPaymentId=paymentId,Amount=100000,Status=includeSource?RefundStatus.Pending:RefundStatus.AwaitingDestination,Reason="test",DestinationBankAccountId=includeSource?bankId:null,DestinationBankCode=includeSource?bank.BankCode:null,DestinationBankBin=includeSource?bank.BankBin:null,DestinationBankName=includeSource?bank.BankName:null,DestinationAccountNumber=includeSource?bank.AccountNumber:null,DestinationAccountName=includeSource?bank.AccountHolderName:null,DestinationQrCodeUrl=includeSource?bank.QrCodeUrl:null,DestinationCapturedAt=includeSource?now:null,CreatedAt=now,UpdatedAt=now});
        await db.SaveChangesAsync();return(refundId,bankId,customerId);
    }

    private static RefundService RefundServiceFor(ApplicationDbContext db)=>new(db,new NoopReceivables(),new NoopProvider(),new ConfigurationBuilder().Build(),NullLogger<RefundService>.Instance);
    private static User User(Guid id,UserRole role)=>new(){UserId=id,Role=role,FullName=role.ToString(),IsActive=true,CreatedAt=DateTime.UtcNow};
    private static BankAccount UsableBank(Guid owner,Guid id,bool isDefault,string number)=>new(){Id=id,UserId=owner,BankCode="VCB",BankBin="970436",BankName="VCB",CanonicalBankKey="BIN:970436",NormalizedAccountNumber=number,AccountNumber=number,AccountHolderName="NGUYEN VAN A",Method="BANK",QrCodeUrl="https://example.com/qr.png",IsActive=true,IsDefault=isDefault,VerificationStatus=BankAccountEligibility.Approved,ActivatedAt=DateTime.UtcNow.AddDays(-1),CreatedAt=DateTime.UtcNow.AddDays(-2),UpdatedAt=DateTime.UtcNow};
    private static ClaimsPrincipal Principal(Guid userId,UserRole role)=>new(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,userId.ToString()),new Claim(ClaimTypes.Role,role.ToString())},"test"));

    private sealed class EligibleMua:IMuaEligibilityService
    {
        public Task<MuaEligibilityDto?> EvaluateAsync(Guid muaId,bool updateStatus=true)=>Task.FromResult<MuaEligibilityDto?>(new MuaEligibilityDto{CanWithdraw=true});
        public Task<bool> SetSuspendedAsync(Guid muaId,bool suspended)=>throw new NotSupportedException();public Task<bool> SetAccountActiveAsync(Guid userId,bool isActive)=>throw new NotSupportedException();public Task<(bool Success,string? Error)> SubmitForReviewAsync(Guid muaId)=>throw new NotSupportedException();public Task<bool> ReviewAsync(Guid muaId,Guid adminId,bool approved,string? reason=null)=>throw new NotSupportedException();public Task<List<AdminMuaApplicationListItemDto>> GetApplicationsAsync(string? status,int page,int pageSize)=>throw new NotSupportedException();
    }
    private sealed class NoopReceivables:IMuaReceivableService
    {
        public Task<MuaReceivable> EnsureForCompletedBookingAsync(Booking booking)=>throw new NotSupportedException();public Task FreezeForDisputeAsync(Guid bookingId)=>Task.CompletedTask;public Task RestoreAfterMuaWinsAsync(Guid bookingId)=>Task.CompletedTask;public Task ReverseAsync(Guid bookingId)=>Task.CompletedTask;public Task<int> ReconcileStatesAsync()=>Task.FromResult(0);public Task<MuaEarningsDto> GetEarningsAsync(Guid muaId)=>throw new NotSupportedException();
    }
    private sealed class NoopProvider:IRefundPayoutProvider{public Task<RefundPayoutResult>CreateAsync(RefundPayoutRequest request,string idempotencyKey)=>throw new NotSupportedException();public Task<RefundPayoutResult>GetAsync(string payoutId)=>throw new NotSupportedException();}
    private sealed class AlwaysAvailableSchedule:IMuaScheduleService
    {
        public Task<bool> HasValidScheduleAsync(Guid muaId)=>Task.FromResult(true);public Task<bool> IsAvailableAsync(Guid muaId,DateTime date,TimeSpan startTime,TimeSpan endTime)=>Task.FromResult(true);public Task<IReadOnlyList<TimeSpan>> GetAvailableStartsAsync(Guid muaId,DateTime date,int durationMinutes,int intervalMinutes=30)=>Task.FromResult<IReadOnlyList<TimeSpan>>(Array.Empty<TimeSpan>());public Task<IReadOnlyList<WorkingScheduleDto>> GetPublicScheduleAsync(Guid muaId)=>Task.FromResult<IReadOnlyList<WorkingScheduleDto>>(Array.Empty<WorkingScheduleDto>());public Task<MuaScheduleManagementDto?> GetManagementScheduleAsync(Guid muaId)=>Task.FromResult<MuaScheduleManagementDto?>(null);public Task ReplaceWorkingScheduleAsync(Guid muaId,IReadOnlyList<WorkingScheduleRequest> schedules)=>Task.CompletedTask;public Task<MuaTimeOffDto> AddTimeOffAsync(Guid muaId,CreateMuaTimeOffRequest request)=>throw new NotSupportedException();public Task<bool> DeleteTimeOffAsync(Guid muaId,Guid timeOffId)=>Task.FromResult(false);
    }
}

public sealed class PostgreSqlDatabase:IAsyncDisposable
{
    private readonly string _adminConnectionString;private readonly string _databaseName;
    public string ConnectionString{get;}
    private PostgreSqlDatabase(string adminConnectionString,string databaseName,string connectionString){_adminConnectionString=adminConnectionString;_databaseName=databaseName;ConnectionString=connectionString;}
    public static async Task<PostgreSqlDatabase>CreateAsync(){var configured=Environment.GetEnvironmentVariable("BEAUTYBOOK_TEST_POSTGRES")!;var adminBuilder=new NpgsqlConnectionStringBuilder(configured){Database="postgres",Pooling=false};var name="beautybook_test_"+Guid.NewGuid().ToString("N");await using(var connection=new NpgsqlConnection(adminBuilder.ConnectionString)){await connection.OpenAsync();await new NpgsqlCommand($"CREATE DATABASE \"{name}\"",connection).ExecuteNonQueryAsync();}var testBuilder=new NpgsqlConnectionStringBuilder(configured){Database=name,Pooling=false};return new PostgreSqlDatabase(adminBuilder.ConnectionString,name,testBuilder.ConnectionString);}
    public static async Task<PostgreSqlDatabase>CreateMigratedAsync(){var value=await CreateAsync();await using var db=value.CreateContext();await db.Database.MigrateAsync();return value;}
    public ApplicationDbContext CreateContext()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options);
    public async ValueTask DisposeAsync(){NpgsqlConnection.ClearAllPools();await using var connection=new NpgsqlConnection(_adminConnectionString);await connection.OpenAsync();await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)",connection).ExecuteNonQueryAsync();}
}
