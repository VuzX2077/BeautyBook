using BeautyBookBackend.Data;
using BeautyBookBackend.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BeautyBookBackend.Tests;

public sealed class EmailOtpServiceTests
{
    [Fact]
    public async Task Correct_code_is_single_use_and_bound_to_context_and_email()
    {
        await using var store=await Store.Create();
        await store.Service.IssueAsync("USER@example.com","BANK_ACCOUNT_ADD","payload-a");
        var code=store.Sender.LastCode!;
        Assert.False(await store.Service.ConsumeAsync("user@example.com","BANK_ACCOUNT_ADD","payload-b",code));
        Assert.False(await store.Service.ConsumeAsync("other@example.com","BANK_ACCOUNT_ADD","payload-a",code));
        Assert.False(await store.Service.ConsumeAsync("user@example.com","BANK_ACCOUNT_UPDATE","payload-a",code));
        Assert.True(await store.Service.ConsumeAsync("user@example.com","BANK_ACCOUNT_ADD","payload-a",code));
        Assert.False(await store.Service.ConsumeAsync("user@example.com","BANK_ACCOUNT_ADD","payload-a",code));
    }

    [Fact]
    public async Task Five_wrong_attempts_lock_the_code()
    {
        await using var store=await Store.Create();
        await store.Service.IssueAsync("user@example.com","BANK_ACCOUNT_UPDATE","payload");
        var code=store.Sender.LastCode!;
        Assert.False(await store.Service.ConsumeAsync("user@example.com","BANK_ACCOUNT_ADD","payload",code));
        for(var attempt=0;attempt<5;attempt++)Assert.False(await store.Service.ConsumeAsync("user@example.com","BANK_ACCOUNT_UPDATE","payload","000000"));
        Assert.False(await store.Service.ConsumeAsync("user@example.com","BANK_ACCOUNT_UPDATE","payload",code));
    }

    [Fact]
    public async Task Expired_code_is_rejected_and_new_issue_invalidates_old_code()
    {
        await using var store=await Store.Create();
        await store.Service.IssueAsync("user@example.com","BANK_ACCOUNT_ADD","payload");
        var first=store.Sender.LastCode!;var row=await store.Db.EmailOtps.SingleAsync();row.CreatedAt=DateTime.UtcNow.AddMinutes(-2);await store.Db.SaveChangesAsync();
        await store.Service.IssueAsync("user@example.com","BANK_ACCOUNT_ADD","payload");var second=store.Sender.LastCode!;
        Assert.False(await store.Service.ConsumeAsync("user@example.com","BANK_ACCOUNT_ADD","payload",first));
        Assert.True(await store.Service.ConsumeAsync("user@example.com","BANK_ACCOUNT_ADD","payload",second));
        var latest=await store.Db.EmailOtps.OrderByDescending(x=>x.CreatedAt).FirstAsync();latest.UsedAt=null;latest.ExpiresAt=DateTime.UtcNow.AddSeconds(-1);await store.Db.SaveChangesAsync();
        Assert.False(await store.Service.ConsumeAsync("user@example.com","BANK_ACCOUNT_ADD","payload",second));
    }

    [Fact]
    public async Task Delivery_failure_removes_new_code_and_keeps_older_codes_invalidated()
    {
        await using var store=await Store.Create();await store.Service.IssueAsync("user@example.com","BANK_ACCOUNT_ADD","payload");
        var first=await store.Db.EmailOtps.SingleAsync();first.CreatedAt=DateTime.UtcNow.AddMinutes(-2);await store.Db.SaveChangesAsync();store.Sender.Fail=true;
        await Assert.ThrowsAsync<EmailDeliveryException>(()=>store.Service.IssueAsync("user@example.com","BANK_ACCOUNT_ADD","payload"));
        store.Db.ChangeTracker.Clear();var remaining=await store.Db.EmailOtps.SingleAsync();Assert.NotNull(remaining.UsedAt);Assert.Equal(first.Id,remaining.Id);
    }

    private sealed class Store: IAsyncDisposable
    {
        private readonly SqliteConnection connection;public ApplicationDbContext Db{get;}public CaptureSender Sender{get;}public EmailOtpService Service{get;}
        private Store(SqliteConnection connection,ApplicationDbContext db,CaptureSender sender){this.connection=connection;Db=db;Sender=sender;Service=new(db,sender,Config());}
        public static async Task<Store>Create(){var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);await db.Database.ExecuteSqlRawAsync("""CREATE TABLE "EmailOtps" ("Id" TEXT PRIMARY KEY,"Email" TEXT NOT NULL,"Purpose" TEXT NOT NULL,"ContextHash" TEXT NULL,"CodeHash" TEXT NOT NULL,"CreatedAt" TEXT NOT NULL,"ExpiresAt" TEXT NOT NULL,"FailedAttempts" INTEGER NOT NULL,"UsedAt" TEXT NULL);""");return new(connection,db,new CaptureSender());}
        public async ValueTask DisposeAsync(){await Db.DisposeAsync();await connection.DisposeAsync();}
    }
    private static IConfiguration Config()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Otp:HashKey","test-secret-at-least-32-characters"}}).Build();
    private sealed class CaptureSender:IEmailSender{public string? LastCode{get;private set;}public bool Fail{get;set;}public Task SendOtpAsync(string email,string otp,string purpose,CancellationToken cancellationToken=default){if(Fail)throw new EmailDeliveryException("failed");LastCode=otp;return Task.CompletedTask;}}
}
