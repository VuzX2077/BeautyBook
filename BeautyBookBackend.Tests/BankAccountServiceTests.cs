using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BeautyBookBackend.Tests;

public sealed class BankAccountServiceTests
{
    [Fact] public async Task Customer_and_mua_modes_share_one_user_list()
    {await using var s=await Store.Create();var created=await s.Service.AddAsync(s.UserId,Request("111 111"));var a=await s.Service.GetAsync(s.UserId);var b=await s.Service.GetAsync(s.UserId);Assert.Single(a);Assert.Equal(created.Id,b.Single().Id);}

    [Fact] public async Task Create_is_normalized_pending_and_never_default()
    {await using var s=await Store.Create();var dto=await s.Service.AddAsync(s.UserId,Request("ab 12-34",true));var row=await s.Db.BankAccounts.SingleAsync();Assert.Equal("AB1234",row.AccountNumber);Assert.Equal("NGUYEN VAN A",row.AccountHolderName);Assert.Equal(BankAccountEligibility.Pending,dto.VerificationStatus);Assert.False(dto.IsDefault);}

    [Fact] public async Task Duplicate_active_identity_is_rejected()
    {await using var s=await Store.Create();await s.Service.AddAsync(s.UserId,Request("ab 12-34"));var ex=await Assert.ThrowsAsync<BookingRuleException>(()=>s.Service.AddAsync(s.UserId,Request("AB1234")));Assert.Equal("BANK_ACCOUNT_DUPLICATE",ex.Code);}

    [Fact] public async Task Unsupported_receiving_method_is_rejected()
    {await using var s=await Store.Create();var request=Request("111111");request.Method="CRYPTO";var ex=await Assert.ThrowsAsync<BookingRuleException>(()=>s.Service.AddAsync(s.UserId,request));Assert.Equal("BANK_ACCOUNT_INVALID",ex.Code);}

    [Theory] [InlineData("PENDING_ADMIN",false,-1)] [InlineData("APPROVED",true,1)] [InlineData("REJECTED",false,-1)]
    public async Task Non_usable_account_cannot_be_default(string status,bool active,int activatedOffset)
    {await using var s=await Store.Create();var current=s.AddUsable("111111",true);var candidate=s.Add("222222",status,active,DateTime.UtcNow.AddHours(activatedOffset));await s.Db.SaveChangesAsync();await Assert.ThrowsAsync<BookingRuleException>(()=>s.Service.SetDefaultAsync(s.UserId,candidate.Id));Assert.True((await s.Db.BankAccounts.FindAsync(current.Id))!.IsDefault);}

    [Fact] public async Task Set_default_is_unique_and_idempotent()
    {await using var s=await Store.Create();var old=s.AddUsable("111111",true);var next=s.AddUsable("222222");await s.Db.SaveChangesAsync();await s.Service.SetDefaultAsync(s.UserId,next.Id);await s.Service.SetDefaultAsync(s.UserId,next.Id);s.Db.ChangeTracker.Clear();Assert.Single(await s.Db.BankAccounts.Where(x=>x.IsDefault).ToListAsync());Assert.False((await s.Db.BankAccounts.AsNoTracking().SingleAsync(x=>x.Id==old.Id)).IsDefault);}

    [Fact] public async Task Sensitive_update_resets_review_and_promotes_oldest_replacement()
    {await using var s=await Store.Create();var target=s.AddUsable("111111",true,DateTime.UtcNow.AddDays(-1));var oldest=s.AddUsable("222222",false,DateTime.UtcNow.AddDays(-3));await s.Db.SaveChangesAsync();await s.Service.UpdateAsync(s.UserId,target.Id,Request("999999"));s.Db.ChangeTracker.Clear();Assert.Equal(BankAccountEligibility.Pending,(await s.Db.BankAccounts.AsNoTracking().SingleAsync(x=>x.Id==target.Id)).VerificationStatus);Assert.True((await s.Db.BankAccounts.AsNoTracking().SingleAsync(x=>x.Id==oldest.Id)).IsDefault);}

    [Fact] public async Task Other_user_cannot_read_update_or_delete_account()
    {await using var s=await Store.Create();var bank=s.AddUsable("111111");await s.Db.SaveChangesAsync();var other=Guid.NewGuid();Assert.Empty(await s.Service.GetAsync(other));Assert.Null(await s.Service.UpdateAsync(other,bank.Id,Request("222222")));Assert.False(await s.Service.DeactivateAsync(other,bank.Id));}

    [Fact] public async Task Deactivating_default_promotes_usable_replacement()
    {await using var s=await Store.Create();var target=s.AddUsable("111111",true);var replacement=s.AddUsable("222222");await s.Db.SaveChangesAsync();Assert.True(await s.Service.DeactivateAsync(s.UserId,target.Id));s.Db.ChangeTracker.Clear();Assert.True((await s.Db.BankAccounts.AsNoTracking().SingleAsync(x=>x.Id==replacement.Id)).IsDefault);}

    private static UpsertBankAccountRequest Request(string number,bool requestedDefault=false)=>new(){BankCode="VCB",BankBin="970436",BankName="Vietcombank",AccountNumber=number,AccountHolderName=" Nguyen  Van A ",Method="BANK",CurrentPassword="unused",IsDefault=requestedDefault};
    private sealed class Store: IAsyncDisposable
    {private readonly SqliteConnection connection;public ApplicationDbContext Db{get;}public BankAccountService Service{get;}public Guid UserId{get;}=Guid.NewGuid();private Store(SqliteConnection c,ApplicationDbContext db){connection=c;Db=db;Service=new(db);}public static async Task<Store>Create(){var c=new SqliteConnection("Data Source=:memory:");await c.OpenAsync();var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(c).Options);await db.Database.ExecuteSqlRawAsync("""CREATE TABLE "BankAccounts" ("Id" TEXT PRIMARY KEY,"UserId" TEXT NOT NULL,"BankCode" TEXT NOT NULL,"BankBin" TEXT NOT NULL,"BankName" TEXT NOT NULL,"AccountNumber" TEXT NOT NULL,"AccountHolderName" TEXT NOT NULL,"Method" TEXT NOT NULL,"CanonicalBankKey" TEXT NOT NULL,"NormalizedAccountNumber" TEXT NOT NULL,"QrCodeUrl" TEXT NULL,"VerificationStatus" TEXT NOT NULL,"ActivatedAt" TEXT NULL,"IsDefault" INTEGER NOT NULL,"IsActive" INTEGER NOT NULL,"ReviewedAt" TEXT NULL,"ReviewedBy" TEXT NULL,"CreatedAt" TEXT NOT NULL,"UpdatedAt" TEXT NOT NULL);CREATE UNIQUE INDEX "UX_BankAccounts_ActiveIdentity" ON "BankAccounts"("UserId","Method","CanonicalBankKey","NormalizedAccountNumber") WHERE "IsActive"=1;CREATE UNIQUE INDEX "UX_BankAccounts_Default" ON "BankAccounts"("UserId") WHERE "IsDefault"=1 AND "IsActive"=1;""");return new(c,db);}public BankAccount Add(string number,string status,bool active,DateTime? activated)=>AddCore(number,false,status,active,activated,DateTime.UtcNow.AddDays(-1));public BankAccount AddUsable(string number,bool isDefault=false,DateTime? created=null)=>AddCore(number,isDefault,BankAccountEligibility.Approved,true,DateTime.UtcNow.AddDays(-1),created??DateTime.UtcNow.AddDays(-1));private BankAccount AddCore(string number,bool isDefault,string status,bool active,DateTime? activated,DateTime created){var b=new BankAccount{Id=Guid.NewGuid(),UserId=UserId,BankCode="VCB",BankBin="970436",BankName="VCB",AccountNumber=number,NormalizedAccountNumber=number,CanonicalBankKey="BIN:970436",AccountHolderName="NGUYEN VAN A",Method="BANK",VerificationStatus=status,ActivatedAt=activated,IsDefault=isDefault,IsActive=active,CreatedAt=created,UpdatedAt=created};Db.BankAccounts.Add(b);return b;}public async ValueTask DisposeAsync(){await Db.DisposeAsync();await connection.DisposeAsync();}}
}
